using System.Collections.Immutable;
using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static NES.NESLib;
using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;

namespace dotnes;

/// <summary>
/// OAM sprite emission — oam_spr, oam_meta_spr, and oam_meta_spr_pal patterns.
/// </summary>
partial class IL2NESWriter
{
    /// <summary>
    /// Emits oam_spr call using decsp4 + inline STA ($22),Y pattern.
    /// Used when arguments include runtime variables (deferred byte array mode).
    /// When isOamScope is true, scans for 4 args (no sprid) and auto-manages oam_off.
    /// </summary>
    void EmitOamSprDecsp4(bool isOamScope = false)
    {
        if (Instructions is null)
            throw new InvalidOperationException("EmitOamSprDecsp4 requires Instructions");

        // oam_spr has 5 args: x, y, tile, attr, id
        // OamScope.spr has 4 args: x, y, tile, attr (id managed via oam_off)
        // Walk back through IL to find the argument-producing IL instructions.
        // Uses stack-depth tracking to correctly handle compound expressions like
        // (byte)(0x30 + (score >> 4)) as a single argument.
        //
        // Depth tracking: scanning backward, binary ops (add/sub/shr/and/etc.) need
        // 2 inputs and produce 1 output, so they increase depth by 1. Value producers
        // (ldc/ldloc) at depth > 0 satisfy a pending input (depth--). At depth == 0,
        // a value producer is a standalone argument.

        var argInfos = new List<(bool isLocal, int localIndex, int constValue,
            bool hasAdd, int addValue, bool isArrayElem, int arrayLocIdx, int indexLocIdx,
            bool isCompound, int compoundLocalIdx, ILOpCode compoundBinOp,
            int compoundBinOpConst, int compoundAddConst,
            bool isStaticField, string? staticFieldName,
            string? compoundStaticFieldName)>();

        int ilIdx = Index - 1;
        int needed = isOamScope ? 4 : 5;
        int firstArgIlIdx = -1;
        int depth = 0;

        // Compound expression state (built up during backward scan)
        ILOpCode compBinOp = ILOpCode.Nop;
        int compBinOpConst = 0;
        int compLocalIdx = -1;
        string? compStaticFieldName = null;
        int compAddConst = 0;
        bool compHasBinOp = false;
        bool compBinOpConstAssigned = false;

        while (needed > 0 && ilIdx >= 0)
        {
            var il = Instructions[ilIdx];
            switch (il.OpCode)
            {
                case ILOpCode.Ldelem_u1:
                {
                    if (depth > 0)
                    {
                        // Array element consumed by binary op — skip its operands
                        depth--;
                        ilIdx--;
                        if (ilIdx >= 0) ilIdx--; // skip index local
                        // array local is at current ilIdx, will be decremented below
                    }
                    else
                    {
                        // Standalone array element arg
                        ilIdx--;
                        int idxLoc = -1, arrLoc = -1;
                        if (ilIdx >= 0) { idxLoc = GetLdlocIndex(Instructions[ilIdx]) ?? -1; ilIdx--; }
                        if (ilIdx >= 0) { arrLoc = GetLdlocIndex(Instructions[ilIdx]) ?? -1; }
                        argInfos.Add((false, 0, 0, false, 0, true, arrLoc, idxLoc,
                            false, 0, ILOpCode.Nop, 0, 0, false, null, null));
                        needed--;
                        firstArgIlIdx = ilIdx;
                    }
                    break;
                }
                case ILOpCode.Ldloc_0: case ILOpCode.Ldloc_1:
                case ILOpCode.Ldloc_2: case ILOpCode.Ldloc_3:
                {
                    int locIdx = il.OpCode - ILOpCode.Ldloc_0;
                    if (depth > 0)
                    {
                        // Consumed by a pending binary op
                        compLocalIdx = locIdx;
                                               depth--;
                        if (depth == 0)
                        {
                            // Compound expression complete
                            argInfos.Add((false, 0, 0, false, 0, false, 0, 0,
                                true, compLocalIdx, compBinOp, compBinOpConst, compAddConst, false, null, compStaticFieldName));
                            needed--;
                            firstArgIlIdx = ilIdx;
                            compBinOp = ILOpCode.Nop; compBinOpConst = 0; compLocalIdx = -1;
                            compStaticFieldName = null; compAddConst = 0; compHasBinOp = false;
                            compBinOpConstAssigned = false;
                        }
                    }
                    else
                    {
                        argInfos.Add((true, locIdx, 0, false, 0, false, 0, 0,
                            false, 0, ILOpCode.Nop, 0, 0, false, null, null));
                        needed--;
                        firstArgIlIdx = ilIdx;
                    }
                    break;
                }
                case ILOpCode.Ldloc_s:
                {
                    int locIdx = il.Integer ?? 0;
                    if (depth > 0)
                    {
                        compLocalIdx = locIdx;
                                               depth--;
                        if (depth == 0)
                        {
                            argInfos.Add((false, 0, 0, false, 0, false, 0, 0,
                                true, compLocalIdx, compBinOp, compBinOpConst, compAddConst, false, null, compStaticFieldName));
                            needed--;
                            firstArgIlIdx = ilIdx;
                            compBinOp = ILOpCode.Nop; compBinOpConst = 0; compLocalIdx = -1;
                            compStaticFieldName = null; compAddConst = 0; compHasBinOp = false;
                            compBinOpConstAssigned = false;
                        }
                    }
                    else
                    {
                        argInfos.Add((true, locIdx, 0, false, 0, false, 0, 0,
                            false, 0, ILOpCode.Nop, 0, 0, false, null, null));
                        needed--;
                        firstArgIlIdx = ilIdx;
                    }
                    break;
                }
                case ILOpCode.Ldc_i4_0: case ILOpCode.Ldc_i4_1: case ILOpCode.Ldc_i4_2:
                case ILOpCode.Ldc_i4_3: case ILOpCode.Ldc_i4_4: case ILOpCode.Ldc_i4_5:
                case ILOpCode.Ldc_i4_6: case ILOpCode.Ldc_i4_7: case ILOpCode.Ldc_i4_8:
                {
                    int val = il.OpCode - ILOpCode.Ldc_i4_0;
                    if (depth > 0)
                    {
                        // First constant after inner binOp → binOp's operand.
                        // Subsequent constants → outer add's operand.
                        if (compHasBinOp && !compBinOpConstAssigned)
                        {
                            compBinOpConst = val;
                            compBinOpConstAssigned = true;
                        }
                        else
                        {
                            compAddConst = val;
                        }
                        depth--;
                        if (depth == 0)
                        {
                            argInfos.Add((false, 0, 0, false, 0, false, 0, 0,
                                true, compLocalIdx, compBinOp, compBinOpConst, compAddConst, false, null, compStaticFieldName));
                            needed--;
                            firstArgIlIdx = ilIdx;
                            compBinOp = ILOpCode.Nop; compBinOpConst = 0; compLocalIdx = -1;
                            compStaticFieldName = null; compAddConst = 0; compHasBinOp = false;
                            compBinOpConstAssigned = false;
                        }
                    }
                    else
                    {
                        argInfos.Add((false, 0, val, false, 0, false, 0, 0,
                            false, 0, ILOpCode.Nop, 0, 0, false, null, null));
                        needed--;
                        firstArgIlIdx = ilIdx;
                    }
                    break;
                }
                case ILOpCode.Ldc_i4_m1:
                case ILOpCode.Ldc_i4_s:
                case ILOpCode.Ldc_i4:
                {
                    int val = il.OpCode == ILOpCode.Ldc_i4_m1 ? -1 : (il.Integer ?? 0);
                    if (depth > 0)
                    {
                        if (compHasBinOp && !compBinOpConstAssigned)
                        {
                            compBinOpConst = val;
                            compBinOpConstAssigned = true;
                        }
                        else
                        {
                            compAddConst = val;
                        }
                        depth--;
                        if (depth == 0)
                        {
                            argInfos.Add((false, 0, 0, false, 0, false, 0, 0,
                                true, compLocalIdx, compBinOp, compBinOpConst, compAddConst, false, null, compStaticFieldName));
                            needed--;
                            firstArgIlIdx = ilIdx;
                            compBinOp = ILOpCode.Nop; compBinOpConst = 0; compLocalIdx = -1;
                            compStaticFieldName = null; compAddConst = 0; compHasBinOp = false;
                            compBinOpConstAssigned = false;
                        }
                    }
                    else
                    {
                        argInfos.Add((false, 0, val, false, 0, false, 0, 0,
                            false, 0, ILOpCode.Nop, 0, 0, false, null, null));
                        needed--;
                        firstArgIlIdx = ilIdx;
                    }
                    break;
                }
                case ILOpCode.Add: case ILOpCode.Sub:
                case ILOpCode.Shr: case ILOpCode.Shr_un: case ILOpCode.Shl:
                case ILOpCode.And: case ILOpCode.Or: case ILOpCode.Xor:
                case ILOpCode.Mul: case ILOpCode.Div: case ILOpCode.Rem:
                    if (il.OpCode is ILOpCode.Shr or ILOpCode.Shr_un or ILOpCode.Shl
                        or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor
                        or ILOpCode.Mul or ILOpCode.Sub or ILOpCode.Div or ILOpCode.Rem)
                    {
                        compBinOp = il.OpCode;
                        compHasBinOp = true;
                    }
                    // Binary op consumes 2 inputs, produces 1 output.
                    // At depth 0: entering a compound expression, need 2 inputs.
                    // At depth > 0: provides 1 output (depth--), needs 2 inputs (depth+=2), net: depth++.
                    if (depth == 0)
                        depth = 2;
                    else
                        depth++;
                    break;
                case ILOpCode.Conv_u1: case ILOpCode.Conv_u2:
                case ILOpCode.Conv_i1: case ILOpCode.Conv_i2: case ILOpCode.Conv_i4:
                case ILOpCode.Pop:
                    break;
                case ILOpCode.Ldsfld:
                {
                    if (depth > 0)
                    {
                        // Static field consumed by binary op — capture field name
                        compStaticFieldName = il.String;
                        depth--;
                        if (depth == 0)
                        {
                            argInfos.Add((false, 0, 0, false, 0, false, 0, 0,
                                true, compLocalIdx, compBinOp, compBinOpConst, compAddConst, false, null, compStaticFieldName));
                            needed--;
                            firstArgIlIdx = ilIdx;
                            compBinOp = ILOpCode.Nop; compBinOpConst = 0; compLocalIdx = -1;
                            compStaticFieldName = null; compAddConst = 0; compHasBinOp = false;
                            compBinOpConstAssigned = false;
                        }
                    }
                    else
                    {
                        // Standalone static field argument (e.g., oam_off)
                        argInfos.Add((false, 0, 0, false, 0, false, 0, 0,
                            false, 0, ILOpCode.Nop, 0, 0, true, il.String, null));
                        needed--;
                        firstArgIlIdx = ilIdx;
                    }
                    break;
                }
                default:
                    break;
            }
            ilIdx--;
        }

        argInfos.Reverse();

        // Remove all previously emitted instructions for these args
        if (firstArgIlIdx >= 0)
        {
            int firstArgIlOffset = Instructions[firstArgIlIdx].Offset;
            if (_blockCountAtILOffset.TryGetValue(firstArgIlOffset, out int blockCountAtFirstArg))
            {
                int instrToRemove = GetBufferedBlockCount() - blockCountAtFirstArg;
                if (instrToRemove > 0)
                    RemoveLastInstructions(instrToRemove);
            }
        }

        // Mark decsp4 as used
        UsedMethods?.Add("decsp4");

        // Emit: JSR decsp4
        EmitJSR("decsp4");

        // First 4 args go to stack via STA ($22),Y
        for (int i = 0; i < 4 && i < argInfos.Count; i++)
        {
            var arg = argInfos[i];
            if (arg.isCompound)
            {
                // Compound expression: addConst + (source BINOP binOpConst)
                // Source can be a local variable or a static field
                if (arg.compoundStaticFieldName != null)
                {
                    EmitLdsfldForArg(arg.compoundStaticFieldName);
                }
                else if (arg.compoundLocalIdx >= 0)
                {
                    var loc = Locals[arg.compoundLocalIdx];
                    Emit(Opcode.LDA, AddressMode.Absolute, (ushort)loc.Address!);
                }
                else
                {
                    // No source variable — constant-only compound expression.
                    // Apply the binary op to the two captured constants directly.
                    // compAddConst is the first operand, compBinOpConst is the second.
                    int constResult = arg.compoundBinOp switch
                    {
                        ILOpCode.Add => arg.compoundAddConst + arg.compoundBinOpConst,
                        ILOpCode.Sub => arg.compoundAddConst - arg.compoundBinOpConst,
                        ILOpCode.Shl => arg.compoundAddConst << arg.compoundBinOpConst,
                        ILOpCode.Shr or ILOpCode.Shr_un => arg.compoundAddConst >> arg.compoundBinOpConst,
                        ILOpCode.And => arg.compoundAddConst & arg.compoundBinOpConst,
                        ILOpCode.Or => arg.compoundAddConst | arg.compoundBinOpConst,
                        ILOpCode.Xor => arg.compoundAddConst ^ arg.compoundBinOpConst,
                        ILOpCode.Mul => arg.compoundAddConst * arg.compoundBinOpConst,
                        ILOpCode.Div or ILOpCode.Div_un => arg.compoundBinOpConst != 0
                            ? arg.compoundAddConst / arg.compoundBinOpConst : 0,
                        ILOpCode.Rem or ILOpCode.Rem_un => arg.compoundBinOpConst != 0
                            ? arg.compoundAddConst % arg.compoundBinOpConst : 0,
                        ILOpCode.Nop => arg.compoundAddConst, // no binary op, just a constant
                        _ => throw new TranspileException(
                            $"Unsupported binary op '{arg.compoundBinOp}' in constant-only compound oam_spr arg.",
                            MethodName),
                    };
                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)(constResult & 0xFF));
                    goto emitDecspStore;
                }

                // Apply the inner binary operation
                switch (arg.compoundBinOp)
                {
                    case ILOpCode.Shr:
                    case ILOpCode.Shr_un:
                        for (int s = 0; s < arg.compoundBinOpConst; s++)
                            Emit(Opcode.LSR, AddressMode.Accumulator);
                        break;
                    case ILOpCode.Shl:
                        for (int s = 0; s < arg.compoundBinOpConst; s++)
                            Emit(Opcode.ASL, AddressMode.Accumulator);
                        break;
                    case ILOpCode.And:
                        Emit(Opcode.AND, AddressMode.Immediate, checked((byte)arg.compoundBinOpConst));
                        break;
                    case ILOpCode.Or:
                        Emit(Opcode.ORA, AddressMode.Immediate, checked((byte)arg.compoundBinOpConst));
                        break;
                    case ILOpCode.Xor:
                        Emit(Opcode.EOR, AddressMode.Immediate, checked((byte)arg.compoundBinOpConst));
                        break;
                    case ILOpCode.Mul:
                    {
                        // Multiply by power of 2 using ASL shifts
                        int shifts = 0;
                        int v = arg.compoundBinOpConst;
                        while (v > 1) { v >>= 1; shifts++; }
                        for (int s = 0; s < shifts; s++)
                            Emit(Opcode.ASL, AddressMode.Accumulator);
                        break;
                    }
                    case ILOpCode.Sub:
                        Emit(Opcode.SEC, AddressMode.Implied);
                        Emit(Opcode.SBC, AddressMode.Immediate, checked((byte)arg.compoundBinOpConst));
                        break;
                    case ILOpCode.Div: case ILOpCode.Div_un:
                    {
                        int divisor = arg.compoundBinOpConst;
                        if (divisor > 0 && (divisor & (divisor - 1)) == 0)
                        {
                            // Power-of-2: use LSR shifts
                            int shifts = 0;
                            int temp = divisor;
                            while (temp > 1) { temp >>= 1; shifts++; }
                            for (int s = 0; s < shifts; s++)
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                        }
                        else
                        {
                            // General division via repeated subtraction
                            // A = dividend. Result: quotient in X → TXA → A
                            Emit(Opcode.LDX, AddressMode.Immediate, 0xFF);
                            Emit(Opcode.SEC, AddressMode.Implied);
                            Emit(Opcode.INX, AddressMode.Implied);
                            Emit(Opcode.SBC, AddressMode.Immediate, (byte)divisor);
                            Emit(Opcode.BCS, AddressMode.Relative, unchecked((byte)-5));
                            Emit(Opcode.TXA, AddressMode.Implied);
                        }
                        break;
                    }
                    case ILOpCode.Rem: case ILOpCode.Rem_un:
                    {
                        int divisor = arg.compoundBinOpConst;
                        if (divisor > 0 && (divisor & (divisor - 1)) == 0)
                        {
                            // Power-of-2: use AND mask
                            Emit(Opcode.AND, AddressMode.Immediate, (byte)(divisor - 1));
                        }
                        else
                        {
                            // General modulo via repeated subtraction
                            // A = dividend. Result: remainder in A (after adding back divisor)
                            Emit(Opcode.SEC, AddressMode.Implied);
                            Emit(Opcode.SBC, AddressMode.Immediate, (byte)divisor);
                            Emit(Opcode.BCS, AddressMode.Relative, unchecked((byte)-4));
                            Emit(Opcode.ADC, AddressMode.Immediate, (byte)divisor);
                        }
                        break;
                    }
                }

                // Apply the outer add
                if (arg.compoundAddConst != 0)
                {
                    Emit(Opcode.CLC, AddressMode.Implied);
                    Emit(Opcode.ADC, AddressMode.Immediate, checked((byte)arg.compoundAddConst));
                }
            }
            else if (arg.isStaticField)
            {
                // Static field: emit LDA for the known zero-page address
                EmitLdsfldForArg(arg.staticFieldName);
            }
            else if (arg.isArrayElem)
            {
                // Array element: LDX index, LDA array,X
                var idxLocal = Locals[arg.indexLocIdx];
                var arrLocal = Locals[arg.arrayLocIdx];
                if (idxLocal.Address != null)
                    Emit(Opcode.LDX, AddressMode.Absolute, (ushort)idxLocal.Address);
                if (arrLocal.Address != null)
                    Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)arrLocal.Address);
            }
            else if (arg.isLocal)
            {
                var loc = Locals[arg.localIndex];
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)loc.Address!);
                if (arg.hasAdd)
                {
                    Emit(Opcode.CLC, AddressMode.Implied);
                    Emit(Opcode.ADC, AddressMode.Immediate, checked((byte)arg.addValue));
                }
            }
            else
            {
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(arg.constValue & 0xFF));
            }

            emitDecspStore:
            if (i == 0)
                Emit(Opcode.LDY, AddressMode.Immediate, 0x03);
            else
                Emit(Opcode.DEY, AddressMode.Implied);

            Emit(Opcode.STA, AddressMode.IndirectIndexed, (byte)0x22);
        }

        // 5th arg (id) stays in A
        if (isOamScope)
        {
            // OamScope.spr: load oam_off as the sprid argument
            Emit(Opcode.LDA, AddressMode.ZeroPage, (byte)OAM_OFF);
        }
        else if (argInfos.Count >= 5)
        {
            var idArg = argInfos[4];
            if (idArg.isStaticField)
            {
                EmitLdsfldForArg(idArg.staticFieldName);
            }
            else if (idArg.isLocal)
            {
                var loc = Locals[idArg.localIndex];
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)loc.Address!);
            }
            else if (idArg.isArrayElem)
            {
                var idxLocal = Locals[idArg.indexLocIdx];
                var arrLocal = Locals[idArg.arrayLocIdx];
                if (idxLocal.Address != null)
                    Emit(Opcode.LDX, AddressMode.Absolute, (ushort)idxLocal.Address);
                if (arrLocal.Address != null)
                    Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)arrLocal.Address);
            }
            else
            {
                byte idVal = (byte)(idArg.constValue & 0xFF);
                // Check if A already has the right value from the 4th arg STA
                if (argInfos.Count >= 4)
                {
                    var attrArg = argInfos[3];
                    if (!attrArg.isLocal && !attrArg.isArrayElem && !idArg.isLocal
                        && attrArg.constValue == idArg.constValue)
                    {
                        // A already has the right value from attr
                    }
                    else
                    {
                        Emit(Opcode.LDA, AddressMode.Immediate, idVal);
                    }
                }
                else
                {
                    Emit(Opcode.LDA, AddressMode.Immediate, idVal);
                }
            }
        }

        // JSR oam_spr
        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, "oam_spr");
        _immediateInA = null;

        if (isOamScope)
        {
            // Store result back to oam_off
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)OAM_OFF);
            _runtimeValueInA = false;
        }
        else
        {
            _runtimeValueInA = true; // oam_spr returns next OAM offset in A
        }
    }

    /// <summary>
    /// Emits LDA for a static field reference used as an oam_spr argument.
    /// Maps known NES library static fields to their zero-page addresses,
    /// or uses the allocated absolute address for user-defined static fields.
    /// </summary>
    void EmitLdsfldForArg(string? fieldName)
    {
        if (fieldName == nameof(NESLib.oam_off))
            Emit(Opcode.LDA, AddressMode.ZeroPage, (byte)NESConstants.OAM_OFF);
        else if (fieldName != null)
        {
            var addr = GetOrAllocateStaticField(fieldName);
            Emit(Opcode.LDA, AddressMode.Absolute, addr);
        }
        else
            throw new TranspileException("Null static field name in oam_spr argument.", MethodName);
    }

    /// <summary>
    /// Emits oam_meta_spr call with proper argument setup.
    /// Supports constants, locals, and array elements for x, y, sprid args.
    /// Sets up: TEMP = x, TEMP2 = y, PTR = data pointer, A = sprid
    /// When isOamScope is true, skips sprid scan and auto-manages oam_off.
    /// </summary>
    void EmitOamMetaSpr(bool isOamScope = false)
    {
        if (Instructions is null)
            throw new InvalidOperationException("EmitOamMetaSpr requires Instructions");

        // Scan backward from the call to find argument sources
        // oam_meta_spr: x (byte), y (byte), sprid (byte), data (byte[])
        // OamScope.meta_spr: x (byte), y (byte), data (byte[]) — sprid via oam_off
        // We scan in reverse order: data, [sprid], y, x

        int scan = Index - 1;

        // Each arg is one of: constant, local, or array element
        // We record what we find for each

        // --- Arg 4: data (byte[] local with LabelName) ---
        string? dataLabel = null;
        ushort? dataAddress = null;
        if (scan >= 0)
        {
            var dataInstr = Instructions[scan];
            var dataLocIdx = GetLdlocIndex(dataInstr);
            if (dataLocIdx != null && Locals.TryGetValue(dataLocIdx.Value, out var dataLocal))
            {
                if (dataLocal.LabelName != null)
                    dataLabel = dataLocal.LabelName;
                else if (dataLocal.Address != null)
                    dataAddress = (ushort)dataLocal.Address;
                scan--;
            }
        }

        // --- Arg 3: sprid (byte) — skipped for OamScope (uses oam_off) ---
        int? spridConst = null;
        ushort? spridAddr = null;
        if (!isOamScope && scan >= 0)
        {
            var si = Instructions[scan];
            var sLocIdx = GetLdlocIndex(si);
            if (sLocIdx != null && Locals.TryGetValue(sLocIdx.Value, out var sLoc) && sLoc.Address != null)
            {
                spridAddr = (ushort)sLoc.Address;
                scan--;
            }
            else if ((GetLdcValue(si) is int sv))
            {
                spridConst = sv;
                scan--;
            }
        }

        // --- Arg 2: y (byte) ---
        int? yConst = null;
        ushort? yAddr = null;
        int? yArrayIdx = null;
        int? indexIdx = null; // shared index for x and y array access
        if (scan >= 0 && Instructions[scan].OpCode == ILOpCode.Ldelem_u1)
        {
            scan--; // skip ldelem
            if (scan >= 0) { indexIdx = GetLdlocIndex(Instructions[scan]); scan--; }
            if (scan >= 0) { yArrayIdx = GetLdlocIndex(Instructions[scan]); scan--; }
        }
        else if (scan >= 0)
        {
            var yi = Instructions[scan];
            var yLocIdx = GetLdlocIndex(yi);
            if (yLocIdx != null && Locals.TryGetValue(yLocIdx.Value, out var yLoc) && yLoc.Address != null)
            {
                yAddr = (ushort)yLoc.Address;
                scan--;
            }
            else if ((GetLdcValue(yi) is int yv))
            {
                yConst = yv;
                scan--;
            }
        }

        // --- Arg 1: x (byte) ---
        int? xConst = null;
        ushort? xAddr = null;
        int? xArrayIdx = null;
        int firstArgILOffset = -1;
        if (scan >= 0 && Instructions[scan].OpCode == ILOpCode.Ldelem_u1)
        {
            scan--; // skip ldelem
            scan--; // skip index ldloc (same index as y)
            if (scan >= 0)
            {
                xArrayIdx = GetLdlocIndex(Instructions[scan]);
                firstArgILOffset = Instructions[scan].Offset;
            }
        }
        else if (scan >= 0)
        {
            var xi = Instructions[scan];
            var xLocIdx = GetLdlocIndex(xi);
            if (xLocIdx != null && Locals.TryGetValue(xLocIdx.Value, out var xLoc) && xLoc.Address != null)
            {
                xAddr = (ushort)xLoc.Address;
                firstArgILOffset = xi.Offset;
            }
            else if ((GetLdcValue(xi) is int xv))
            {
                xConst = xv;
                firstArgILOffset = xi.Offset;
            }
        }

        // Remove all previously emitted instructions for these arguments
        if (firstArgILOffset >= 0 && _blockCountAtILOffset.TryGetValue(firstArgILOffset, out int blockCount))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCount;
            if (instrToRemove > 0)
                RemoveLastInstructions(instrToRemove);
        }

        // Emit the proper code
        // 1. Load x coordinate into TEMP
        if (xArrayIdx != null && indexIdx != null)
        {
            var xArr = Locals[xArrayIdx.Value];
            var idx = Locals[indexIdx.Value];
            if (idx.Address != null)
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)idx.Address);
            if (xArr.ArraySize > 0 && xArr.Address != null)
                Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)xArr.Address);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
        }
        else if (xConst != null)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)xConst.Value));
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
        }
        else if (xAddr != null)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, xAddr.Value);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
        }

        // 2. Load y coordinate into TEMP2
        if (yArrayIdx != null && indexIdx != null)
        {
            var yArr = Locals[yArrayIdx.Value];
            // X already has the index from step 1
            if (yArr.ArraySize > 0 && yArr.Address != null)
                Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)yArr.Address);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
        }
        else if (yConst != null)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)yConst.Value));
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
        }
        else if (yAddr != null)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, yAddr.Value);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
        }

        // 3. Load data pointer into PTR
        if (dataLabel != null)
        {
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, dataLabel);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.ptr1);
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_HighByte, dataLabel);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)(NESConstants.ptr1 + 1));
        }
        else if (dataAddress != null)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(dataAddress.Value & 0xFF));
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.ptr1);
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(dataAddress.Value >> 8));
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)(NESConstants.ptr1 + 1));
        }

        // 4. Load sprid into A
        if (isOamScope)
        {
            Emit(Opcode.LDA, AddressMode.ZeroPage, (byte)NESConstants.OAM_OFF);
        }
        else if (spridAddr != null)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, spridAddr.Value);
        }
        else if (spridConst != null)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)spridConst.Value));
        }

        // 5. Call oam_meta_spr
        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, nameof(NESLib.oam_meta_spr));
        _immediateInA = null;

        if (isOamScope)
        {
            // Store result back to oam_off
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.OAM_OFF);
            _runtimeValueInA = false;
        }
        else
        {
            _runtimeValueInA = true; // Return value in A
        }
    }

    /// <summary>
    /// Emits oam_meta_spr_pal call with proper argument setup.
    /// Supports constants, locals, and array elements for x, y, pal args.
    /// Sets up: TEMP = x, TEMP2 = y, TEMP3 = pal, PTR = data pointer
    /// 
    /// Sets up: TEMP = x, TEMP2 = y, TEMP3 = pal, PTR = data pointer
    /// Uses OAM_OFF zero-page global for OAM buffer offset.
    /// </summary>
    void EmitOamMetaSprPal()
    {
        if (Instructions is null)
            throw new InvalidOperationException("EmitOamMetaSprPal requires Instructions");

        // Scan backward from the call to find all 4 argument sources
        // Args order: x (byte), y (byte), pal (byte), data (byte[])
        // We scan in reverse order: data, pal, y, x

        int scan = Index - 1;

        // --- Arg 4: data (byte[] local with LabelName or Address) ---
        string? dataLabel = null;
        ushort? dataAddress = null;
        if (scan >= 0)
        {
            var dataInstr = Instructions[scan];
            var dataLocIdx = GetLdlocIndex(dataInstr);
            if (dataLocIdx != null && Locals.TryGetValue(dataLocIdx.Value, out var dataLocal))
            {
                if (dataLocal.LabelName != null)
                    dataLabel = dataLocal.LabelName;
                else if (dataLocal.Address != null)
                    dataAddress = (ushort)dataLocal.Address;
                scan--;
            }
        }

        // --- Arg 3: pal (byte — constant, local, or array element) ---
        int? palConst = null;
        ushort? palAddr = null;
        int? palArrayIdx = null;
        int? palIndexIdx = null;
        if (scan >= 0 && Instructions[scan].OpCode == ILOpCode.Ldelem_u1)
        {
            scan--; // skip ldelem
            if (scan >= 0) { palIndexIdx = GetLdlocIndex(Instructions[scan]); scan--; }
            if (scan >= 0) { palArrayIdx = GetLdlocIndex(Instructions[scan]); scan--; }
        }
        else if (scan >= 0)
        {
            var pi = Instructions[scan];
            var pLocIdx = GetLdlocIndex(pi);
            if (pLocIdx != null && Locals.TryGetValue(pLocIdx.Value, out var pLoc) && pLoc.Address != null)
            {
                palAddr = (ushort)pLoc.Address;
                scan--;
            }
            else if (GetLdcValue(pi) is int pv)
            {
                palConst = pv;
                scan--;
            }
        }

        // --- Arg 2: y (byte — constant, local, or array element) ---
        int? yConst = null;
        ushort? yAddr = null;
        int? yArrayIdx = null;
        int? yIndexIdx = null;
        if (scan >= 0 && Instructions[scan].OpCode == ILOpCode.Ldelem_u1)
        {
            scan--; // skip ldelem
            if (scan >= 0) { yIndexIdx = GetLdlocIndex(Instructions[scan]); scan--; }
            if (scan >= 0) { yArrayIdx = GetLdlocIndex(Instructions[scan]); scan--; }
        }
        else if (scan >= 0)
        {
            var yi = Instructions[scan];
            var yLocIdx = GetLdlocIndex(yi);
            if (yLocIdx != null && Locals.TryGetValue(yLocIdx.Value, out var yLoc) && yLoc.Address != null)
            {
                yAddr = (ushort)yLoc.Address;
                scan--;
            }
            else if (GetLdcValue(yi) is int yv)
            {
                yConst = yv;
                scan--;
            }
        }

        // --- Arg 1: x (byte — constant, local, or array element) ---
        int? xConst = null;
        ushort? xAddr = null;
        int? xArrayIdx = null;
        int? xIndexIdx = null;
        int firstArgILOffset = -1;
        if (scan >= 0 && Instructions[scan].OpCode == ILOpCode.Ldelem_u1)
        {
            scan--; // skip ldelem
            if (scan >= 0) { xIndexIdx = GetLdlocIndex(Instructions[scan]); scan--; }
            if (scan >= 0)
            {
                xArrayIdx = GetLdlocIndex(Instructions[scan]);
                firstArgILOffset = Instructions[scan].Offset;
            }
        }
        else if (scan >= 0)
        {
            var xi = Instructions[scan];
            var xLocIdx = GetLdlocIndex(xi);
            if (xLocIdx != null && Locals.TryGetValue(xLocIdx.Value, out var xLoc) && xLoc.Address != null)
            {
                xAddr = (ushort)xLoc.Address;
                firstArgILOffset = xi.Offset;
            }
            else if (GetLdcValue(xi) is int xv)
            {
                xConst = xv;
                firstArgILOffset = xi.Offset;
            }
        }

        // Remove all previously emitted instructions for these arguments
        if (firstArgILOffset >= 0 && _blockCountAtILOffset.TryGetValue(firstArgILOffset, out int blockCount))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCount;
            if (instrToRemove > 0)
                RemoveLastInstructions(instrToRemove);
        }

        // Emit the proper code
        // Track which index local last loaded into X to avoid redundant LDX
        int? lastLoadedXIndex = null;

        // 1. Load x coordinate into TEMP
        if (xArrayIdx != null && xIndexIdx != null)
        {
            var xArr = Locals[xArrayIdx.Value];
            var idx = Locals[xIndexIdx.Value];
            if (idx.Address != null)
            {
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)idx.Address);
                lastLoadedXIndex = xIndexIdx;
            }
            if (xArr.ArraySize > 0 && xArr.Address != null)
                Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)xArr.Address);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
        }
        else if (xConst != null)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)xConst.Value));
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
        }
        else if (xAddr != null)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, xAddr.Value);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
        }

        // 2. Load y coordinate into TEMP2
        if (yArrayIdx != null && yIndexIdx != null)
        {
            var yArr = Locals[yArrayIdx.Value];
            var yIdx = Locals[yIndexIdx.Value];
            if (yIdx.Address != null && yIndexIdx != lastLoadedXIndex)
            {
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)yIdx.Address);
                lastLoadedXIndex = yIndexIdx;
            }
            if (yArr.ArraySize > 0 && yArr.Address != null)
                Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)yArr.Address);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
        }
        else if (yConst != null)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)yConst.Value));
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
        }
        else if (yAddr != null)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, yAddr.Value);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
        }

        // 3. Load palette into TEMP3
        if (palArrayIdx != null && palIndexIdx != null)
        {
            var pArr = Locals[palArrayIdx.Value];
            var pIdx = Locals[palIndexIdx.Value];
            if (pIdx.Address != null && palIndexIdx != lastLoadedXIndex)
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)pIdx.Address);
            if (pArr.ArraySize > 0 && pArr.Address != null)
                Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)pArr.Address);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP3);
        }
        else if (palConst != null)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)palConst.Value));
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP3);
        }
        else if (palAddr != null)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, palAddr.Value);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP3);
        }

        // 4. Load data pointer into PTR
        if (dataLabel != null)
        {
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, dataLabel);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.ptr1);
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_HighByte, dataLabel);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)(NESConstants.ptr1 + 1));
        }
        else if (dataAddress != null)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(dataAddress.Value & 0xFF));
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.ptr1);
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(dataAddress.Value >> 8));
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)(NESConstants.ptr1 + 1));
        }

        // 5. Call oam_meta_spr_pal (uses OAM_OFF global, no sprid param)
        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, nameof(NESLib.oam_meta_spr_pal));
        _immediateInA = null;
        _runtimeValueInA = false; // void return
    }

    /// <summary>
    /// Emits oam_spr_2x2 call: builds a 17-byte metasprite array at compile time from
    /// 4 tile constants + attr, then emits an oam_meta_spr call with x, y, sprid.
    /// Args: x, y, topLeft, bottomLeft, topRight, bottomRight, attr, sprid (8 total).
    /// </summary>
    void EmitOamSpr2x2()
    {
        if (Instructions is null)
            throw new InvalidOperationException("EmitOamSpr2x2 requires Instructions");

        // Scan backward through IL to classify all 8 argument sources.
        // Args in IL order: x, y, topLeft, bottomLeft, topRight, bottomRight, attr, sprid
        // We scan in reverse: sprid, attr, bottomRight, topRight, bottomLeft, topLeft, y, x
        //
        // Roslyn's Release optimizer may interleave Stloc instructions between argument pushes
        // (e.g., "ldc 40, ldc 40, stloc.0, ldloc.0" for inline constant+init).
        // When scanning backward, we skip Stloc and the value it consumed.

        int scan = Index - 1;

        // Each arg: (isConst, constValue, localIdx)
        var args = new (bool isConst, int value, int localIdx, bool isStaticField, string? staticFieldName)[8];
        int firstArgILOffset = -1;
        int skip = 0; // values to skip (consumed by Stloc interleaved in the arg sequence)

        for (int argIdx = 7; argIdx >= 0 && scan >= 0;)
        {
            var il = Instructions[scan];

            // Skip Stloc instructions — they consume one stack value that isn't a call arg
            if (il.OpCode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2
                or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc
                or ILOpCode.Pop)
            {
                skip++; // The next value-producing instruction we find was consumed by this stloc
                scan--;
                continue;
            }

            bool isValueProducer = GetLdcValue(il) != null || GetLdlocIndex(il) != null
                || il.OpCode is ILOpCode.Ldsfld;

            // Conv_u1 doesn't produce a new value (just converts top-of-stack), skip it
            if (il.OpCode == ILOpCode.Conv_u1)
            {
                scan--;
                continue;
            }

            if (skip > 0 && isValueProducer)
            {
                skip--;
                scan--;
                continue;
            }

            var ldcValue = GetLdcValue(il);
            if (ldcValue != null)
            {
                args[argIdx] = (true, ldcValue.Value, -1, false, null);
                if (argIdx == 0) firstArgILOffset = il.Offset;
                scan--;
                argIdx--;
            }
            else
            {
                var locIdx = GetLdlocIndex(il);
                if (locIdx != null)
                {
                    args[argIdx] = (false, 0, locIdx.Value, false, null);
                    if (argIdx == 0) firstArgILOffset = il.Offset;
                    scan--;
                    argIdx--;
                }
                else if (il.OpCode == ILOpCode.Ldsfld)
                {
                    args[argIdx] = (false, 0, -1, true, il.String);
                    if (argIdx == 0) firstArgILOffset = il.Offset;
                    scan--;
                    argIdx--;
                }
                else
                {
                    throw new TranspileException(
                        $"oam_spr_2x2 argument {argIdx} has unsupported IL opcode: {il.OpCode}",
                        MethodName);
                }
            }
        }

        // Extract tile and attr constants (args 2-6 must be compile-time constants)
        for (int i = 2; i <= 6; i++)
        {
            if (!args[i].isConst)
                throw new TranspileException(
                    $"oam_spr_2x2: argument {i} (tile/attr) must be a compile-time constant.",
                    MethodName);
        }

        int topLeft = args[2].value;
        int bottomLeft = args[3].value;
        int topRight = args[4].value;
        int bottomRight = args[5].value;
        int attr = args[6].value;

        // Remove all previously emitted instructions for these 8 arguments
        if (firstArgILOffset >= 0 && _blockCountAtILOffset.TryGetValue(firstArgILOffset, out int blockCount))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCount;
            if (instrToRemove > 0)
                RemoveLastInstructions(instrToRemove);
        }

        // Build the 17-byte metasprite array (same layout as meta_spr_2x2)
        byte[] data = new byte[]
        {
            0, 0, (byte)topLeft, (byte)attr,
            0, 8, (byte)bottomLeft, (byte)attr,
            8, 0, (byte)topRight, (byte)attr,
            8, 8, (byte)bottomRight, (byte)attr,
            128 // end marker
        };

        // Register as byte array data
        string byteArrayLabel = $"bytearray_{_byteArrayLabelIndex}";
        _byteArrayLabelIndex++;
        _byteArrays.Add(data.ToImmutableArray());

        // Emit oam_meta_spr calling convention:
        // 1. Load x into TEMP
        EmitOamSpr2x2Arg(args[0], (byte)NESConstants.TEMP);

        // 2. Load y into TEMP2
        EmitOamSpr2x2Arg(args[1], (byte)NESConstants.TEMP2);

        // 3. Load data pointer into PTR
        EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, byteArrayLabel);
        Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.ptr1);
        EmitWithLabel(Opcode.LDA, AddressMode.Immediate_HighByte, byteArrayLabel);
        Emit(Opcode.STA, AddressMode.ZeroPage, (byte)(NESConstants.ptr1 + 1));

        // 4. Load sprid into A
        if (args[7].isConst)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)args[7].value));
        }
        else if (args[7].isStaticField)
        {
            EmitLdsfldForArg(args[7].staticFieldName);
        }
        else if (Locals.TryGetValue(args[7].localIdx, out var spridLocal) && spridLocal.Address != null)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, (ushort)spridLocal.Address);
        }
        else
        {
            throw new TranspileException("oam_spr_2x2: unsupported sprid argument type.", MethodName);
        }

        // 5. Call oam_meta_spr
        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, nameof(NESLib.oam_meta_spr));
        UsedMethods?.Add(nameof(NESLib.oam_meta_spr));
        _immediateInA = null;
        _runtimeValueInA = true; // oam_meta_spr returns next OAM offset in A
    }

    /// <summary>
    /// Emits code to load an oam_spr_2x2 argument (constant or local) into a zero-page target.
    /// </summary>
    void EmitOamSpr2x2Arg(
        (bool isConst, int value, int localIdx, bool isStaticField, string? staticFieldName) arg,
        byte target)
    {
        if (arg.isConst)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)arg.value));
            Emit(Opcode.STA, AddressMode.ZeroPage, target);
        }
        else if (arg.isStaticField)
        {
            EmitLdsfldForArg(arg.staticFieldName);
            Emit(Opcode.STA, AddressMode.ZeroPage, target);
        }
        else if (Locals.TryGetValue(arg.localIdx, out var local) && local.Address != null)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, (ushort)local.Address);
            Emit(Opcode.STA, AddressMode.ZeroPage, target);
        }
        else
        {
            throw new TranspileException("oam_spr_2x2: unsupported x/y argument type.", MethodName);
        }
    }
}
