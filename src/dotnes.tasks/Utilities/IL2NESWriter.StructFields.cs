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
        bool isWord = Variables.WordStaticFields.Contains(fieldName);
        if (Stack.Count > 0) Stack.Pop();

        if (isWord)
        {
            if (_ushortInAX)
            {
                // 16-bit value in A:X — store both bytes
                Emit(Opcode.STA, AddressMode.Absolute, addr);
                Emit(Opcode.STX, AddressMode.Absolute, (ushort)(addr + 1));
            }
            else if (_runtimeValueInA)
            {
                // 8-bit runtime value — zero-extend high byte
                Emit(Opcode.STA, AddressMode.Absolute, addr);
                Emit(Opcode.LDA, AddressMode.Immediate, 0);
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)(addr + 1));
            }
            else if (_immediateInA != null)
            {
                int val = (int)_immediateInA;
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(val & 0xFF));
                Emit(Opcode.STA, AddressMode.Absolute, addr);
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)((val >> 8) & 0xFF));
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)(addr + 1));
            }
            else
            {
                // Constant from WriteLdc — store low byte, zero high byte
                Emit(Opcode.STA, AddressMode.Absolute, addr);
                Emit(Opcode.LDA, AddressMode.Immediate, 0);
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)(addr + 1));
            }
        }
        else
        {
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
        }
        _runtimeValueInA = false;
        _ushortInAX = false;
        _immediateInA = null;
        _pokeLastValue = null;
    }

    void HandleLdsfld(string fieldName)
    {
        var addr = GetOrAllocateStaticField(fieldName);
        bool isWord = Variables.WordStaticFields.Contains(fieldName);

        // Preserve any pending value in A before clobbering it,
        // mirroring WriteLdloc behavior.
        if (_ushortInAX)
        {
            EmitJSR("pushax");
            UsedMethods?.Add("pushax");
            _ushortInAX = false;
        }
        else if (_runtimeValueInA && !LastLDA)
        {
            // When loading args for a default-path call, push to cc65 stack
            // instead of saving to TEMP (which only holds one value).
            if (ScanForUpcomingMultiArgCall() || IsNextCallToDefaultPathTarget())
            {
                EmitJSR("pusha");
                _runtimeValueInA = false;
            }
            else
            {
                Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
                _savedRuntimeToTemp = true;
            }
        }
        else if (LastLDA)
        {
            EmitJSR("pusha");
        }

        if (isWord)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, addr);
            Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(addr + 1));
            _ushortInAX = true;
        }
        else
        {
            Emit(Opcode.LDA, AddressMode.Absolute, addr);
        }
        _runtimeValueInA = true;
        _immediateInA = null;
        _pokeLastValue = null;
        Stack.Push(0);

        // Look ahead: if the next instruction loads another value and the
        // upcoming Call uses the default call path, push the current value
        // to the cc65 stack so it survives the next load.
        if (!isWord && ScanForUpcomingMultiArgCall())
        {
            EmitJSR("pusha");
            _runtimeValueInA = false;
        }
    }

    /// <summary>
    /// Handles stfld: store a value to a struct field on zero page.
    /// IL pattern: ldloca.s N, ldc.i4 value, stfld fieldName
    /// </summary>
    void HandleStfld(string fieldName)
    {
        // Handle closure struct field store
        if (_pendingStructLocal != null
            && ClosureFieldTypes != null
            && ClosureFieldTypes.ContainsKey(fieldName))
        {
            _pendingStructLocal = null;
            int fieldSize = ClosureFieldTypes[fieldName];

            if (fieldSize == -1) // byte[] field
            {
                // Associate the byte array label from the preceding ldtoken
                if (_lastByteArrayLabel != null)
                {
                    ClosureFieldLabels[fieldName] = _lastByteArrayLabel;
                    _lastByteArrayLabel = null;
                }
                if (Stack.Count > 0) Stack.Pop();
                _runtimeValueInA = false;
                _ushortInAX = false;
                _immediateInA = null;
                return;
            }
            else // scalar field (byte, ushort, short, int captured in closure)
            {
                ushort addr = ClosureFieldAddresses[fieldName];
                int value = Stack.Count > 0 ? Stack.Pop() : 0;

                if (fieldSize == 1)
                {
                    if (_runtimeValueInA)
                    {
                        Emit(Opcode.STA, AddressMode.Absolute, addr);
                        _runtimeValueInA = false;
                    }
                    else
                    {
                        RemoveLastInstructions(1);
                        Emit(Opcode.LDA, AddressMode.Immediate, (byte)(value & 0xFF));
                        Emit(Opcode.STA, AddressMode.Absolute, addr);
                    }
                }
                else
                {
                    // Multi-byte scalar (16-bit word: low/high bytes)
                    if (_runtimeValueInA && _ushortInAX)
                    {
                        Emit(Opcode.STA, AddressMode.Absolute, addr);
                        Emit(Opcode.STX, AddressMode.Absolute, (ushort)(addr + 1));
                    }
                    else
                    {
                        byte low = (byte)(value & 0xFF);
                        byte high = (byte)((value >> 8) & 0xFF);
                        Emit(Opcode.LDA, AddressMode.Immediate, low);
                        Emit(Opcode.STA, AddressMode.Absolute, addr);
                        Emit(Opcode.LDA, AddressMode.Immediate, high);
                        Emit(Opcode.STA, AddressMode.Absolute, (ushort)(addr + 1));
                    }
                    _runtimeValueInA = false;
                    _ushortInAX = false;
                }
                _immediateInA = null;
                return;
            }
        }

        // Check for struct array element access (from ldelema)
        if (_pendingStructElement is PendingStructElement stfldElement)
        {
            string structType = stfldElement.Type;
            int fieldOffset = GetFieldOffset(structType, fieldName);

            // The value to store was pushed by ldc before stfld
            int value = Stack.Count > 0 ? Stack.Pop() : 0;

            if (stfldElement.RuntimeIndex)
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
                // Constant index: ConstantBase has the element base
                ushort fieldAddr = (ushort)(stfldElement.ConstantBase!.Value + fieldOffset);
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

            _pendingStructElement = null;
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
        // Handle closure struct field load (from ldarg.0 in closure methods
        // or ldloca.s in main)
        if ((_pendingClosureAccess || _pendingStructLocal != null)
            && ClosureFieldTypes != null
            && ClosureFieldTypes.ContainsKey(fieldName))
        {
            _pendingClosureAccess = false;
            _pendingStructLocal = null;
            int fieldSize = ClosureFieldTypes[fieldName];

            if (fieldSize == -1) // byte[] field
            {
                if (ClosureFieldLabels.TryGetValue(fieldName, out var label))
                {
                    EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, label);
                    EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, label);
                    _ushortInAX = true;
                }
                else
                {
                    throw new TranspileException(
                        $"Closure byte[] field '{fieldName}' has no ROM data label. " +
                        "Ensure the array is initialized before it is used in a closure method.");
                }
            }
            else // scalar field
            {
                ushort addr = ClosureFieldAddresses[fieldName];
                Emit(Opcode.LDA, AddressMode.Absolute, addr);
                if (fieldSize > 1)
                {
                    Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(addr + 1));
                    _ushortInAX = true;
                }
            }
            _runtimeValueInA = true;
            _immediateInA = null;
            Stack.Push(0);
            return;
        }

        // Check for struct array element access (from ldelema)
        if (_pendingStructElement is PendingStructElement ldfldElement)
        {
            string structType = ldfldElement.Type;
            int fieldOffset = GetFieldOffset(structType, fieldName);

            if (ldfldElement.RuntimeIndex)
            {
                // Variable index: X holds element offset, use AbsoluteX
                ushort fieldAddr = (ushort)(_structArrayBaseForRuntimeIndex + fieldOffset);
                Emit(Opcode.LDA, AddressMode.AbsoluteX, fieldAddr);
            }
            else
            {
                // Constant index: ConstantBase has the element base
                ushort fieldAddr = (ushort)(ldfldElement.ConstantBase!.Value + fieldOffset);
                Emit(Opcode.LDA, AddressMode.Absolute, fieldAddr);
            }

            _pendingStructElement = null;
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
