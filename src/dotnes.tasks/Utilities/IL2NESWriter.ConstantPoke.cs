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

    void RemoveOperandInstructions(int firstArgument, int count)
    {
        if (Instructions is not null && firstArgument >= 0 && firstArgument < Instructions.Length
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
        Local? local = null;
        int? localIndex = Instructions is null
            ? _lastLoadedLocalIndex : Instructions[Index - 1].GetLdlocIndex();
        bool valueIsLocal = localIndex.HasValue && Locals.TryGetValue(localIndex.Value, out local)
            && local.Address.HasValue;
        ushort? staticAddress = Instructions is null ? _lastStaticFieldAddress
            : Instructions[Index - 1].OpCode == ILOpCode.Ldsfld
                && Instructions[Index - 1].String is string field
                && StaticFieldAddresses.TryGetValue(field, out ushort location)
                    ? location : null;

        // Deferred literals may emit nothing. Never remove earlier control flow
        // or infer this call's value from stale accumulator/local bookkeeping.
        RemoveOperandInstructions(Index - 2, address > byte.MaxValue ? 4 : 3);
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
        else if (!CanReuseConstantPokeValue((byte)value))
        {
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)value);
            _pokeLastValue = (byte)value;
            _immediateInA = (byte)value;
        }
        Emit(Opcode.STA, AddressMode.Absolute, (ushort)address);
        // Argument pushes removed above cannot remain live after this consumes the whole IL stack.
        if (Stack.Count == 0)
            _savedState = SavedValueState.None;
        _lastLoadedLocalIndex = null;
        _lastStaticFieldAddress = null;
    }
}
