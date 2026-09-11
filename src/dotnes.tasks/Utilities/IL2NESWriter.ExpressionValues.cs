using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

partial class IL2NESWriter
{
    bool TryWriteLocalBinary(ILOpCode op)
    {
        if (Instructions == null || Index < 2
            || op is not (ILOpCode.Add or ILOpCode.Sub or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor)
            || Instructions[Index - 1].GetLdlocIndex() is not int rightIndex
            || !Locals.TryGetValue(rightIndex, out var right)
            || right.Address is not int rightAddress || right.ArraySize != 0 || right.IsWord)
            return false;

        int? leftAddress = null;
        int? constant = Instructions[Index - 2].GetLdcValue();
        if (Instructions[Index - 2].GetLdlocIndex() is int leftIndex
            && Locals.TryGetValue(leftIndex, out var left) && left.Address is int address
            && !left.IsWord && left.ArraySize == 0)
            leftAddress = address;
        else if (constant is not (>= 0 and <= 255) || op is ILOpCode.Add or ILOpCode.Sub)
            return false;

        int start = _blockCountAtILOffset[Instructions[Index - 2].Offset];
        RemoveLastInstructions(GetBufferedBlockCount() - start);
        if (leftAddress.HasValue)
            Emit(Opcode.LDA, AddressMode.Absolute, (ushort)leftAddress.Value);
        else
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)constant!.Value);
        if (op is ILOpCode.Add or ILOpCode.Sub)
            Emit(op == ILOpCode.Add ? Opcode.CLC : Opcode.SEC, AddressMode.Implied);
        Emit(op switch
        {
            ILOpCode.Add => Opcode.ADC,
            ILOpCode.Sub => Opcode.SBC,
            ILOpCode.And => Opcode.AND,
            ILOpCode.Or => Opcode.ORA,
            _ => Opcode.EOR
        }, AddressMode.Absolute, (ushort)rightAddress);
        Stack.Pop();
        Stack.Pop();
        Stack.Push(0);
        _lastLoadedLocalIndex = null;
        _savedRuntimeToTemp = false;
        _savedConstantViaPusha = false;
        _ushortInAX = false;
        _runtimeValueInA = true;
        previous = op;
        return true;
    }

}
