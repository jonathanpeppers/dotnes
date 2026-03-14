using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static NES.NESLib;
using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;
using Local = dotnes.LocalVariableManager.Local;

namespace dotnes;

/// <summary>
/// Struct field management — field offset calculation, load/store, static fields.
/// Delegates to <see cref="LocalVariableManager"/> for pure allocation and query methods.
/// </summary>
partial class IL2NESWriter
{
    // Forwarding methods for backward compatibility — delegate to Variables
    ushort GetOrAllocateStaticField(string fieldName) => Variables.GetOrAllocateStaticField(fieldName);
    ushort GetOrAllocateStructLocal(int localIndex, string structType) => Variables.GetOrAllocateStructLocal(localIndex, structType);
    int GetFieldOffset(string structType, string fieldName) => Variables.GetFieldOffset(structType, fieldName);
    int GetStructSize(string structType) => Variables.GetStructSize(structType);
    string ResolveStructType(int localIndex, string fieldName) => Variables.ResolveStructType(localIndex, fieldName);

    void HandleStsfld(string fieldName)
    {
        var addr = GetOrAllocateStaticField(fieldName);
        if (Stack.Count > 0) Stack.Pop();

        if (_runtimeValueInA)
        {
            Emit(Opcode.STA, AddressMode.Absolute, addr);
        }
        else if (_immediateInA != null)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)_immediateInA);
            Emit(Opcode.STA, AddressMode.Absolute, addr);
        }
        else
        {
            // Constant from WriteLdc — remove previous LDA, re-emit with STA
            Emit(Opcode.STA, AddressMode.Absolute, addr);
        }
        _runtimeValueInA = false;
        _immediateInA = null;
        _lastStaticFieldAddress = null;
        _pokeLastValue = null;
    }

    void HandleLdsfld(string fieldName)
    {
        var addr = GetOrAllocateStaticField(fieldName);
        Emit(Opcode.LDA, AddressMode.Absolute, addr);
        _runtimeValueInA = true;
        _immediateInA = null;
        _lastStaticFieldAddress = addr;
        Stack.Push(0);
    }

    /// <summary>
    /// Handles stfld: store a value to a struct field on zero page.
    /// IL pattern: ldloca.s N, ldc.i4 value, stfld fieldName
    /// </summary>
    void HandleStfld(string fieldName)
    {
        // Check for struct array element access (from ldelema)
        if (_pendingStructElementType != null)
        {
            string structType = _pendingStructElementType;
            int fieldOffset = GetFieldOffset(structType, fieldName);

            // The value to store was pushed by ldc before stfld
            int value = Stack.Count > 0 ? Stack.Pop() : 0;

            if (_pendingStructArrayRuntimeIndex)
            {
                // Variable index: X holds element offset, use AbsoluteX
                ushort fieldAddr = (ushort)(_structArrayBaseForRuntimeIndex + fieldOffset);
                if (_runtimeValueInA)
                {
                    Emit(Opcode.STA, AddressMode.AbsoluteX, fieldAddr);
                    _runtimeValueInA = false;
                }
                else
                {
                    RemoveLastInstructions(1);
                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)(value & 0xFF));
                    Emit(Opcode.STA, AddressMode.AbsoluteX, fieldAddr);
                }
            }
            else
            {
                // Constant index: _pendingStructElementBase has the element base
                ushort fieldAddr = (ushort)(_pendingStructElementBase!.Value + fieldOffset);
                if (_runtimeValueInA)
                {
                    Emit(Opcode.STA, AddressMode.Absolute, fieldAddr);
                    _runtimeValueInA = false;
                }
                else
                {
                    RemoveLastInstructions(1);
                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)(value & 0xFF));
                    Emit(Opcode.STA, AddressMode.Absolute, fieldAddr);
                }
            }

            _pendingStructElementType = null;
            _pendingStructElementBase = null;
            _pendingStructArrayRuntimeIndex = false;
            _immediateInA = null;
            return;
        }

        if (_pendingStructLocal is null)
            throw new InvalidOperationException($"stfld '{fieldName}' without preceding ldloca.s or ldelema");

        {
            int localIndex = _pendingStructLocal.Value;
            _pendingStructLocal = null;

            string structType = ResolveStructType(localIndex, fieldName);
            ushort baseAddr = GetOrAllocateStructLocal(localIndex, structType);
            int fieldOffset = GetFieldOffset(structType, fieldName);
            ushort fieldAddr = (ushort)(baseAddr + fieldOffset);

        // The value to store was pushed by ldc before stfld
        int value = Stack.Count > 0 ? Stack.Pop() : 0;

        if (_runtimeValueInA)
        {
            // A has the runtime value — just store it
            Emit(Opcode.STA, AddressMode.Absolute, fieldAddr);
            _runtimeValueInA = false;
        }
        else
        {
            // Remove the LDA #imm that WriteLdc emitted
            RemoveLastInstructions(1);
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(value & 0xFF));
            Emit(Opcode.STA, AddressMode.Absolute, fieldAddr);
        }
        _immediateInA = null;
        }
    }

    /// <summary>
    /// Handles ldfld: load a value from a struct field on zero page.
    /// IL patterns: (ldloca.s N | ldloc.N), ldfld fieldName
    /// </summary>
    void HandleLdfld(string fieldName)
    {
        // Check for struct array element access (from ldelema)
        if (_pendingStructElementType != null)
        {
            string structType = _pendingStructElementType;
            int fieldOffset = GetFieldOffset(structType, fieldName);

            if (_pendingStructArrayRuntimeIndex)
            {
                // Variable index: X holds element offset, use AbsoluteX
                ushort fieldAddr = (ushort)(_structArrayBaseForRuntimeIndex + fieldOffset);
                Emit(Opcode.LDA, AddressMode.AbsoluteX, fieldAddr);
            }
            else
            {
                // Constant index: _pendingStructElementBase has the element base
                ushort fieldAddr = (ushort)(_pendingStructElementBase!.Value + fieldOffset);
                Emit(Opcode.LDA, AddressMode.Absolute, fieldAddr);
            }

            _pendingStructElementType = null;
            _pendingStructElementBase = null;
            _pendingStructArrayRuntimeIndex = false;
            _runtimeValueInA = true;
            _immediateInA = null;
            Stack.Push(0);
            return;
        }

        {
            int localIndex;
            if (_pendingStructLocal is not null)
            {
                localIndex = _pendingStructLocal.Value;
                _pendingStructLocal = null;
            }
            else if (_lastLoadedLocalIndex is not null && _lastLoadedLocalIndex >= 0 && Variables.IsStructLocal(_lastLoadedLocalIndex.Value))
            {
                // ldloc loaded the struct value — undo the WriteLdloc LDA and use field access instead
                localIndex = _lastLoadedLocalIndex.Value;
                RemoveLastInstructions(1);
                if (Stack.Count > 0) Stack.Pop(); // Remove the value WriteLdloc pushed
            }
            else
            {
                throw new InvalidOperationException($"ldfld '{fieldName}' without preceding ldloca.s or struct ldloc");
            }

            string structType = ResolveStructType(localIndex, fieldName);
            ushort baseAddr = GetOrAllocateStructLocal(localIndex, structType);
            int fieldOffset = GetFieldOffset(structType, fieldName);
            ushort fieldAddr = (ushort)(baseAddr + fieldOffset);

            Emit(Opcode.LDA, AddressMode.Absolute, fieldAddr);
            _runtimeValueInA = true;
            _immediateInA = null;

            // Push the field value onto the IL stack (unknown at compile time)
            Stack.Push(0);
        }
    }
}
