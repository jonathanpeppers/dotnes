using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static dotnes.NESConstants;
using Local = dotnes.LocalVariableManager.Local;

namespace dotnes;

partial class IL2NESWriter
{
    bool ArrayIndexNeedsPreservation(int index) =>
        Instructions is not null && ArrayOperandLowering.IndexNeedsPreservation(Instructions, index);

    internal Dictionary<string, bool[]> UserMethodArrayParameters { get; init; } = new(StringComparer.Ordinal);
    internal ISet<string> UnsupportedArrayHelperSignatures { get; init; } = new HashSet<string>();
    readonly Dictionary<int, int> _arrayArgumentAdjustments = new();

    int ParameterOffset(int parameter)
    {
        int offset = _argStackAdjust;
        for (int i = parameter + 1; i < MethodParamCount; i++)
            offset += i < ParamIsArray.Length && ParamIsArray[i] ? 2 : 1;
        return offset;
    }

    void EmitArrayParameterPointer(Local array)
    {
        if (array.ArrayParameterIndex is not int parameter)
            throw new TranspileException("Expected a byte-array parameter.", MethodName);

        Emit(Opcode.LDY, AddressMode.Immediate, checked((byte)ParameterOffset(parameter)));
        Emit(Opcode.LDA, AddressMode.IndirectIndexed, sp);
        Emit(Opcode.STA, AddressMode.ZeroPage, ptr1);
        Emit(Opcode.INY, AddressMode.Implied);
        Emit(Opcode.LDA, AddressMode.IndirectIndexed, sp);
        Emit(Opcode.STA, AddressMode.ZeroPage, ptr1 + 1);
    }

    void EmitArrayParameterRead(Local array)
    {
        // The index is in X. Resolve the pointer at the access, not before an RHS
        // or a nested call that can reuse zero-page scratch.
        EmitArrayParameterPointer(array);
        Emit(Opcode.TXA, AddressMode.Implied);
        Emit(Opcode.TAY, AddressMode.Implied);
        Emit(Opcode.LDA, AddressMode.IndirectIndexed, ptr1);
    }

    void EmitArrayParameterWrite(Local array)
    {
        // Keep the computed value in A and the index in X across pointer setup.
        Emit(Opcode.PHA, AddressMode.Implied);
        EmitArrayParameterPointer(array);
        Emit(Opcode.TXA, AddressMode.Implied);
        Emit(Opcode.TAY, AddressMode.Implied);
        Emit(Opcode.PLA, AddressMode.Implied);
        Emit(Opcode.STA, AddressMode.IndirectIndexed, ptr1);
    }

    void RemoveArrayArgumentLoads(int offset)
    {
        if (!_blockCountAtILOffset.TryGetValue(offset, out int blockCount))
            throw new TranspileException("Array operands have no recorded instruction boundary.", MethodName);
        RemoveLastInstructions(GetBufferedBlockCount() - blockCount);
        if (_arrayArgumentAdjustments.TryGetValue(offset, out int adjustment))
            _argStackAdjust = adjustment;
        _accState = AccumulatorState.Empty;
        _savedState = SavedValueState.None;
        _lastLoadedLocalIndex = null;
        _lastStaticFieldAddress = null;
    }

    void EmitArrayScalar(ILInstruction source, bool index = false)
    {
        if (source.GetLdcValue() is int constant)
            Emit(index ? Opcode.LDX : Opcode.LDA, AddressMode.Immediate, checked((byte)constant));
        else if (source.GetLdlocIndex() is int localIndex && Locals[localIndex].Address is int address)
            Emit(index ? Opcode.LDX : Opcode.LDA, AddressMode.Absolute, checked((ushort)address));
        else if (source.GetLdargIndex() is int parameter)
        {
            if (index) Emit(Opcode.PHA, AddressMode.Implied);
            Emit(Opcode.LDY, AddressMode.Immediate, checked((byte)ParameterOffset(parameter)));
            Emit(Opcode.LDA, AddressMode.IndirectIndexed, sp);
            if (index)
            {
                Emit(Opcode.TAX, AddressMode.Implied);
                Emit(Opcode.PLA, AddressMode.Implied);
            }
        }
        else if (source.OpCode == ILOpCode.Ldsfld && source.String is string field
            && StaticFieldAddresses.TryGetValue(field, out ushort fieldAddress))
            Emit(index ? Opcode.LDX : Opcode.LDA, AddressMode.Absolute, fieldAddress);
        else
            throw new TranspileException("Array operands must be materialized before byte-array emission.", MethodName);
    }

    bool TryEmitArrayParameterRead()
    {
        if (Instructions is null || Index < 2 ||
            TryResolveArrayLocal(Instructions[Index - 2]) is not { ArrayParameterIndex: not null } array)
            return false;
        RemoveArrayArgumentLoads(Instructions[Index - 2].Offset);
        EmitArrayScalar(Instructions[Index - 1], index: true);
        EmitArrayParameterRead(array);
        Stack.Push(0);
        _runtimeValueInA = true;
        return true;
    }

    bool TryEmitArrayParameterStore()
    {
        if (Instructions is null || Index < 3 ||
            TryResolveArrayLocal(Instructions[Index - 3]) is not { ArrayParameterIndex: not null } array)
            return false;
        RemoveArrayArgumentLoads(Instructions[Index - 3].Offset);
        EmitArrayScalar(Instructions[Index - 1]);
        EmitArrayScalar(Instructions[Index - 2], index: true);
        EmitArrayParameterWrite(array);
        return true;
    }

    bool TryEmitArrayParameterCall(string method)
    {
        if (!UserMethodArrayParameters.TryGetValue(method, out var parameters) ||
            !parameters.Contains(true))
            return false;
        if (_lastByteArrayLabel is not null)
            return false;
        if (Instructions is null || Index < parameters.Length)
            throw new TranspileException("Byte-array helper call has no argument instructions.", MethodName);

        int first = Index - parameters.Length;
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i] && TryResolveArrayLocal(Instructions[first + i])?.LabelName is not null)
                return false;
        if (UnsupportedArrayHelperSignatures.Contains(method))
            throw new TranspileException("Fixed RAM array helpers support only byte-sized scalar parameters and return values.", method);
        RemoveArrayArgumentLoads(Instructions[first].Offset);
        for (int i = 0; i < parameters.Length; i++)
        {
            var source = Instructions[first + i];
            if (parameters[i])
            {
                var array = TryResolveArrayLocal(source);
                if (array?.ArrayParameterIndex is not null)
                {
                    EmitArrayParameterPointer(array);
                    Emit(Opcode.LDA, AddressMode.ZeroPage, ptr1);
                    Emit(Opcode.LDX, AddressMode.ZeroPage, ptr1 + 1);
                }
                else if (array?.ArraySize > 0 && array.Address is int address)
                {
                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)address);
                    Emit(Opcode.LDX, AddressMode.Immediate, (byte)(address >> 8));
                }
                else if (array?.LabelName is string label)
                {
                    EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, label);
                    EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, label);
                }
                else
                    throw new TranspileException("Byte-array helper arguments must reference an existing fixed array.", MethodName);
            }
            else
                EmitArrayScalar(source);

            if (i < parameters.Length - 1)
            {
                string push = parameters[i] ? "pushax" : "pusha";
                EmitJSR(push);
                UsedMethods?.Add(push);
                _argStackAdjust += parameters[i] ? 2 : 1;
            }
        }
        EmitJSR(method);
        for (int i = 0; i < parameters.Length - 1; i++)
            _argStackAdjust -= parameters[i] ? 2 : 1;
        _pokeLastValue = null;
        return true;
    }

    bool TryStoreArrayAlias(ILInstruction instruction)
    {
        if (instruction.GetStlocIndex() is not int destination || Instructions is null || Index == 0)
            return false;
        var source = Instructions[Index - 1];
        var array = TryResolveArrayLocal(source);
        if (array is null || (array.ArraySize == 0 && array.LabelName is null && array.ArrayParameterIndex is null))
            return false;
        if (Locals.TryGetValue(destination, out var existing) &&
            (existing.ArraySize > 0 || existing.LabelName is not null || existing.ArrayParameterIndex is not null) &&
            (existing.Address != array.Address || existing.LabelName != array.LabelName ||
             existing.ArrayParameterIndex != array.ArrayParameterIndex))
            throw new TranspileException("Reassigning an array alias to a different array is not supported.", MethodName);
        RemoveArrayArgumentLoads(source.Offset);
        Locals[destination] = array;
        Stack.Pop();
        previous = instruction.OpCode;
        return true;
    }
}
