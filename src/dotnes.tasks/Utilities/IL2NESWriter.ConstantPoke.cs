using System.Reflection.Metadata;
using dotnes.ObjectModel;
using Local = dotnes.LocalVariableManager.Local;

namespace dotnes;

partial class IL2NESWriter
{
    bool CanReuseConstantPokeValue(byte value)
    {
        if (_pokeLastValue != value || Instructions is null || _numericValues is null || Index < 3
            || Instructions[Index - 3].OpCode != ILOpCode.Call
            || Instructions[Index - 3].String != nameof(NESLib.poke))
            return false;
        return !Enumerable.Range(Index - 2, 3)
            .Any(i => _numericValues.Predecessors[i].Any(predecessor => predecessor != i - 1));
    }

    void RemoveMemoryArgumentInstructions(int firstArgument, int count)
    {
        if (Instructions is not null
            && _blockCountAtILOffset.TryGetValue(Instructions[firstArgument].Offset, out int start))
            count = Math.Min(count, GetBufferedBlockCount() - start);
        if (count > 0)
            RemoveLastInstructions(count);
    }

    void EmitConstantPoke()
    {
        if (Stack.Count < 2)
            throw new TranspileException("poke requires an address and a byte value.", MethodName);
        int value = Stack.Pop();
        int address = Stack.Pop();
        int producer = Index - 1;
        int firstArgument = Index - 2;
        bool convertedValue = false;
        if (Instructions is not null && _numericValues is not null && _numericValues.Inputs[Index].Length == 2)
        {
            producer = _numericValues.Inputs[Index][1];
            if (_numericValues.Inputs[Index][0] >= 0)
                firstArgument = _numericValues.Inputs[Index][0];
            while (producer >= 0 && Instructions[producer].OpCode is
                ILOpCode.Conv_u1 or ILOpCode.Conv_i1 or ILOpCode.Conv_u2 or ILOpCode.Conv_i2
                && _numericValues.Inputs[producer].Length == 1)
            {
                convertedValue = true;
                producer = _numericValues.Inputs[producer][0];
            }
        }
        Local? local = null;
        int? localIndex = Instructions is null
            ? _lastLoadedLocalIndex : producer >= 0 ? Instructions[producer].GetLdlocIndex() : null;
        bool valueIsLocal = localIndex.HasValue && Locals.TryGetValue(localIndex.Value, out local)
            && local.Address.HasValue;
        ushort? staticAddress = Instructions is null ? _lastStaticFieldAddress
            : producer >= 0 && Instructions[producer].OpCode == ILOpCode.Ldsfld
                && Instructions[producer].String is string field && StaticFieldAddresses.TryGetValue(field, out ushort location)
                    ? location : null;
        bool valueIsArgument = Instructions is not null && producer >= 0
            && NumericArgIndex(Instructions[producer]).HasValue && PureNumericOperand(producer, out _);

        // Deferred literals may emit nothing. Never remove earlier control flow
        // or infer this call's value from stale accumulator/local bookkeeping.
        if (convertedValue || valueIsArgument)
        {
            if (!TryNumericOperands(out _, out _))
                throw new TranspileException(
                    "poke cannot preserve this converted operand directly. Store the converted byte in an " +
                    "explicit byte local before calling poke.", MethodName);
        }
        else
            RemoveMemoryArgumentInstructions(firstArgument, address > byte.MaxValue ? 4 : 3);
        if (valueIsLocal)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, (ushort)local!.Address!.Value);
            _pokeLastValue = null;
            _immediateInA = null;
        }
        else if (staticAddress.HasValue)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, staticAddress.Value);
            _pokeLastValue = null;
            _immediateInA = null;
        }
        else if (valueIsArgument)
        {
            EmitNumericOperand(producer);
            _pokeLastValue = null;
            _immediateInA = null;
        }
        else if (!CanReuseConstantPokeValue((byte)value))
        {
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)value);
            _pokeLastValue = (byte)value;
            _immediateInA = (byte)value;
        }
        Emit(Opcode.STA, AddressMode.Absolute, (ushort)address);
        _lastLoadedLocalIndex = null;
        _lastStaticFieldAddress = null;
    }
}
