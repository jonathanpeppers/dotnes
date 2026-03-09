using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static NES.NESLib;
using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;

namespace dotnes;

/// <summary>
/// Struct field management — field offset calculation, load/store, static fields.
/// </summary>
partial class IL2NESWriter
{
    /// <summary>
    /// Allocates an absolute address for a user-defined static field.
    /// Static fields share the same $0325+ address space as locals.
    /// </summary>
    ushort GetOrAllocateStaticField(string fieldName)
    {
        if (_staticFieldAddresses.TryGetValue(fieldName, out var addr))
            return addr;
        addr = (ushort)(local + LocalCount);
        LocalCount += 1;
        _staticFieldAddresses[fieldName] = addr;
        return addr;
    }

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
    }

    void HandleLdsfld(string fieldName)
    {
        var addr = GetOrAllocateStaticField(fieldName);
        Emit(Opcode.LDA, AddressMode.Absolute, addr);
        _runtimeValueInA = true;
        _immediateInA = null;
        Stack.Push(0);
    }

    /// <summary>
    /// Gets the zero-page address and allocates storage for a struct local.
    /// </summary>
    ushort GetOrAllocateStructLocal(int localIndex, string structType)
    {
        if (Locals.TryGetValue(localIndex, out var existing) && existing.Address is not null)
            return (ushort)existing.Address;

        // Allocate struct on zero page
        int structSize = 0;
        if (StructLayouts.TryGetValue(structType, out var fields))
        {
            foreach (var f in fields)
                structSize += f.Size;
        }
        if (structSize == 0)
            structSize = 1;

        ushort addr = (ushort)(local + LocalCount);
        LocalCount += structSize;
        Locals[localIndex] = new Local(0, addr);
        _structLocalTypes[localIndex] = structType;
        return addr;
    }

    /// <summary>
    /// Gets the byte offset of a field within a struct type.
    /// </summary>
    int GetFieldOffset(string structType, string fieldName)
    {
        if (!StructLayouts.TryGetValue(structType, out var fields))
            throw new InvalidOperationException($"Unknown struct type '{structType}'");

        int offset = 0;
        foreach (var f in fields)
        {
            if (f.Name == fieldName)
                return offset;
            offset += f.Size;
        }
        throw new InvalidOperationException($"Field '{fieldName}' not found in struct '{structType}'");
    }

    /// <summary>
    /// Gets the total size in bytes of a struct type.
    /// </summary>
    int GetStructSize(string structType)
    {
        if (!StructLayouts.TryGetValue(structType, out var fields))
            throw new InvalidOperationException($"Unknown struct type '{structType}'");
        int size = 0;
        foreach (var f in fields)
            size += f.Size;
        return size > 0 ? size : 1;
    }

    /// <summary>
    /// Resolves the struct type for a local by checking _structLocalTypes or matching
    /// the field name against known struct layouts.
    /// </summary>
    string ResolveStructType(int localIndex, string fieldName)
    {
        if (_structLocalTypes.TryGetValue(localIndex, out var knownType))
            return knownType;

        // Search all struct layouts for a matching field name
        foreach (var kvp in StructLayouts.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            foreach (var f in kvp.Value)
            {
                if (f.Name == fieldName)
                {
                    _structLocalTypes[localIndex] = kvp.Key;
                    return kvp.Key;
                }
            }
        }
        throw new InvalidOperationException($"Cannot resolve struct type for local {localIndex} with field '{fieldName}'");
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
            else if (_lastLoadedLocalIndex is not null && _lastLoadedLocalIndex >= 0 && _structLocalTypes.ContainsKey(_lastLoadedLocalIndex.Value))
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
