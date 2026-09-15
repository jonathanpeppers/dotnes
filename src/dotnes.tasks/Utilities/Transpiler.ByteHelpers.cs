using System.Reflection;
using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

partial class Transpiler
{
    readonly Dictionary<string, MethodDefinition> _byteHelperDefinitions = new(StringComparer.Ordinal);
    bool _ambiguousByteHelperNames;

    /// <summary>
    /// Opts into private, non-reentrant single-byte parameter homes. Call sites retain
    /// the standard A-register ABI; unproven methods keep their software-stack frames.
    /// </summary>
    public bool OptimizeByteHelpers { get; init; }

    void RecordByteHelperDefinition(string name, MethodDefinition definition)
    {
        if (_byteHelperDefinitions.ContainsKey(name))
            _ambiguousByteHelperNames = true;
        else
            _byteHelperDefinitions.Add(name, definition);
    }

    HashSet<string> FindByteHelpers(ILInstruction[] main)
    {
        // Assembly can call private labels or install interrupts outside the managed
        // call graph. Without an effect/ABI contract, no private home is proven safe.
        if (ExternMethods.Count != 0 || _ambiguousByteHelperNames
            || main.Concat(UserMethods.Values.SelectMany(il => il)).Any(i =>
                i.OpCode is ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Calli))
        {
            _logger.WriteLine($"Byte helper optimization disabled: extern, indirect call, or ambiguous method identity.");
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        AddCallees(main, reachable);
        var flagDependentCalls = FindFlagDependentCalls(main);
        var externallyReachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in _byteHelperDefinitions)
        {
            if (!IsPrivateOrLocalFunction(entry.Value))
            {
                externallyReachable.Add(entry.Key);
                AddCallees(UserMethods[entry.Key], externallyReachable);
            }
        }

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in _byteHelperDefinitions)
        {
            if (reachable.Contains(entry.Key) && !externallyReachable.Contains(entry.Key)
                && !flagDependentCalls.Contains(entry.Key)
                && HasByteHelperSignature(entry.Value) && HasByteHelperBody(entry.Key, entry.Value))
                candidates.Add(entry.Key);
        }

        // A method is safe only after every callee is proven safe. Cycles and their
        // callers never enter the set, and unknown/built-in calls are not whitelisted.
        var safe = new HashSet<string>(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var name in candidates)
            {
                if (!safe.Contains(name) && UserMethods[name].All(i =>
                    i.OpCode != ILOpCode.Call || i.String is string callee && safe.Contains(callee)))
                    changed |= safe.Add(name);
            }
        } while (changed);

        return safe;
    }

    internal bool TryOptimizeByteHelpers(Program6502 program, ILInstruction[] main, int localHighWater, bool hasNativeCode,
        out int localBytes, IReadOnlyDictionary<string, string>? frameEntries = null)
    {
        localBytes = localHighWater;
        // Native entry points are independent of C# extern declarations. Linked
        // assembly and PRG bank payloads have no proven reentrancy contract.
        if (hasNativeCode)
        {
            _logger.WriteLine($"Byte helper optimization disabled: linked native code has unproven entry points.");
            return false;
        }
        var candidates = FindByteHelpers(main);
        if (candidates.Count == 0)
            return false;

        // Work entirely from final emitted frames/stack effects before touching IR.
        // Unknown effects, recursion and nonzero stack changes around loops fail closed.
        var analyzer = new ByteHelperStackAnalysis(program, NESConstants.LocalStackBase + localHighWater, frameEntries);
        if (!analyzer.TryAnalyze("main", out var stack) || stack.Minimum < 0 || stack.Delta != 0
            || NESConstants.LocalStackBase + localHighWater + candidates.Count > 0x0800 - stack.Maximum)
        {
            _logger.WriteLine($"Byte helper optimization disabled: stack bound or disjoint RAM capacity is unproven.");
            return false;
        }

        var plans = new List<(string Name, Block Block, HashSet<int> Loads, Dictionary<int, string> Targets)>();
        foreach (var name in candidates.OrderBy(n => n, StringComparer.Ordinal))
        {
            var block = program.GetBlock(name)!;
            if (!TryPlanByteHelper(name, block, out var loads, out var targets))
            {
                _logger.WriteLine($"Byte helper optimization disabled: unexpected emitted parameter frame in '{name}'.");
                return false;
            }
            plans.Add((name, block, loads, targets));
        }

        program.InvalidateAddresses();
        foreach (var (name, block, loads, targets) in plans)
        {
            ushort home = (ushort)(NESConstants.LocalStackBase + localBytes++);
            var original = block.InstructionsWithLabels.ToArray();
            var offsets = InstructionOffsets(block);
            block.Clear();
            for (int i = 0; i < original.Length; i++)
            {
                if (original[i].Label is string label)
                    block.SetNextLabel(label);
                if (targets.TryGetValue(i, out var target))
                    block.SetNextLabel(target);
                if (loads.Contains(i) || i == original.Length - 2)
                    continue; // LDY before ldarg, or parameter cleanup

                var instruction = original[i].Instruction;
                if (i == 0)
                    instruction = new Instruction(Opcode.STA, AddressMode.Absolute, new AbsoluteOperand(home));
                else if (loads.Contains(i - 1))
                    instruction = new Instruction(Opcode.LDA, AddressMode.Absolute, new AbsoluteOperand(home));
                else if (instruction.Operand is RelativeByteOperand relative)
                {
                    int targetOffset = offsets[i] + instruction.Size + relative.Offset;
                    int targetIndex = Array.IndexOf(offsets, targetOffset);
                    instruction = new Instruction(instruction.Opcode, AddressMode.Relative, new RelativeOperand(targets[targetIndex]));
                }
                block.Emit(instruction);
            }
            _logger.WriteLine($"Byte helper '{name}' parameter home: ${home:X4}; software-stack bound {stack.Maximum} bytes.");
        }
        return true;
    }

    bool TryPlanByteHelper(string name, Block block, out HashSet<int> loads, out Dictionary<int, string> targets)
    {
        loads = new HashSet<int>();
        targets = new Dictionary<int, string>();
        if (block.Count < 3 || !Calls(block[0], "pusha")
            || !Calls(block[block.Count - 2], "incsp1") || block[block.Count - 1].Opcode != Opcode.RTS)
            return false;

        var labels = InstructionLabels(block);
        foreach (var instruction in UserMethods[name])
        {
            if (instruction.OpCode != ILOpCode.Ldarg_0)
                continue;
            if (!labels.TryGetValue($"{name}_instruction_{instruction.Offset:X2}", out int index)
                || index + 1 >= block.Count
                || block[index].Opcode != Opcode.LDY || block[index].Mode != AddressMode.Immediate
                || block[index + 1].Opcode != Opcode.LDA || block[index + 1].Mode != AddressMode.IndirectIndexed
                || block[index + 1].Operand is not ImmediateOperand { Value: NESConstants.sp })
                return false;
            loads.Add(index);
        }

        var offsets = InstructionOffsets(block);
        for (int i = 0; i < block.Count; i++)
        {
            if (block[i].Mode == AddressMode.IndirectIndexed && !loads.Contains(i - 1))
                return false;
            if (block[i].Operand is RelativeByteOperand branch)
            {
                int targetIndex = Array.IndexOf(offsets, offsets[i] + block[i].Size + branch.Offset);
                if (targetIndex < 0)
                    return false;
                targets[targetIndex] = $"@bytehelper_target_{targetIndex}";
            }
        }
        return true;
    }

    static bool Calls(Instruction instruction, string name) =>
        instruction.Opcode == Opcode.JSR && instruction.Operand is LabelOperand label && label.Label == name;

    static int[] InstructionOffsets(Block block)
    {
        var result = new int[block.Count];
        for (int i = 1; i < block.Count; i++)
            result[i] = result[i - 1] + block[i - 1].Size;
        return result;
    }

    static Dictionary<string, int> InstructionLabels(Block block)
    {
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < block.Count; i++)
            if (block.GetLabelAt(i) is string label)
                labels[label] = i;
        foreach (var alias in block.LabelAliases)
        {
            string target = alias.Value;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (!labels.ContainsKey(target) && block.LabelAliases.TryGetValue(target, out var next) && visited.Add(target))
                target = next;
            if (labels.TryGetValue(target, out int index))
                labels[alias.Key] = index;
        }
        if (block.Label is string name)
            labels[name] = 0;
        return labels;
    }

    internal readonly record struct ByteHelperStackEffect(int Delta, int Minimum, int Maximum);

    internal sealed class ByteHelperStackAnalysis(Program6502 program, int localEnd,
        IReadOnlyDictionary<string, string>? frameEntries = null)
    {
        const int MaxByteHelperCallDepth = 128;

        readonly Dictionary<string, ByteHelperStackEffect> _effects = new(StringComparer.Ordinal);
        readonly HashSet<string> _active = new(StringComparer.Ordinal);
        readonly Dictionary<string, string> _frameEntries = frameEntries?.ToDictionary(p => p.Value, p => p.Key)
            ?? new Dictionary<string, string>();
        static readonly Instruction[] incsp2 = BuiltInSubroutines.Incsp2().InstructionsWithLabels.Select(i => i.Instruction).ToArray();
        static readonly Instruction[] addysp = BuiltInSubroutines.Addysp().InstructionsWithLabels.Select(i => i.Instruction).ToArray();

        public bool TryAnalyze(string name, out ByteHelperStackEffect effect)
        {
            effect = default;
            int? primitive = name switch
            {
                "pusha" => 1, "pushax" => 2, "decsp4" => 4,
                "popa" or "incsp1" => -1, "popax" or "incsp2" => -2,
                _ => null,
            };
            if (primitive is int delta)
            {
                effect = new(delta, Math.Min(0, delta), Math.Max(0, delta));
                return true;
            }
            if (_effects.TryGetValue(name, out effect))
                return true;
            var block = program.GetBlock(name);
            int start = 0;
            if (block == null && _frameEntries.TryGetValue(name, out string? method))
            {
                block = program.GetBlock(method);
                if (block == null || !InstructionLabels(block).TryGetValue(name, out start))
                    return false;
            }
            if (block is null || block.IsDataBlock || block.Count == 0
                || _active.Count >= MaxByteHelperCallDepth || !_active.Add(name))
                return false;
            bool success = AnalyzeBlock(block, start, out effect);
            _active.Remove(name);
            if (success)
                _effects[name] = effect;
            return success;
        }

        bool AnalyzeBlock(Block block, int start, out ByteHelperStackEffect effect)
        {
            effect = default;
            var labels = InstructionLabels(block);
            var offsets = InstructionOffsets(block);
            bool IsBranchTarget(int target) => Enumerable.Range(0, block.Count).Any(i => block[i].Operand switch
            {
                LabelOperand label when block[i].Opcode == Opcode.JMP && labels.TryGetValue(label.Label, out int value) => value == target,
                RelativeOperand label when labels.TryGetValue(label.Label, out int value) => value == target,
                RelativeByteOperand branch => offsets[i] + block[i].Size + branch.Offset == offsets[target],
                _ => false,
            });
            var depths = new Dictionary<int, int>();
            var pending = new Stack<(int Index, int Depth)>();
            pending.Push((start, 0));
            int minimum = 0, maximum = 0;
            int? exitDepth = null;
            while (pending.Count > 0)
            {
                var (index, depth) = pending.Pop();
                if (index < 0 || index >= block.Count)
                    return false;
                if (depths.TryGetValue(index, out int prior))
                {
                    if (prior != depth)
                        return false;
                    continue;
                }
                depths.Add(index, depth);
                var instruction = block[index];
                bool Matches(int at, params Instruction[] expected) => at + expected.Length <= block.Count
                    && expected.Select((value, offset) => block[at + offset] == value).All(equal => equal);
                if (index + 6 <= block.Count
                    && block[index + 2] is { Opcode: Opcode.SBC, Mode: AddressMode.Immediate,
                        Operand: ImmediateOperand { Value: > 0 } amount }
                    && Matches(index,
                        Asm.LDA_zpg(NESConstants.sp), Asm.SEC(), Asm.SBC(amount.Value),
                        Asm.STA_zpg(NESConstants.sp), Asm.BCS(2), Asm.DEC_zpg(NESConstants.sp + 1)))
                {
                    depth += amount.Value;
                    maximum = Math.Max(maximum, depth);
                    pending.Push((index + 6, depth));
                    continue;
                }
                int cleanup = 0;
                if (index + incsp2.Length == block.Count && Matches(index, incsp2))
                    cleanup = 2;
                else if (index + addysp.Length + 1 == block.Count
                    && instruction is { Opcode: Opcode.LDY, Mode: AddressMode.Immediate,
                        Operand: ImmediateOperand { Value: > 0 } count }
                    && Matches(index + 1, addysp))
                    cleanup = count.Value;
                if (cleanup > 0)
                {
                    depth -= cleanup;
                    minimum = Math.Min(minimum, depth);
                    if (exitDepth.HasValue && exitDepth != depth)
                        return false;
                    exitDepth = depth;
                    continue;
                }
                if (instruction.Mode is AddressMode.AbsoluteX or AddressMode.AbsoluteY
                    or AddressMode.IndexedIndirect or AddressMode.ZeroPageX or AddressMode.ZeroPageY
                    || instruction.Mode == AddressMode.IndirectIndexed
                        && instruction.Operand is not ImmediateOperand { Value: NESConstants.sp }
                    || instruction.Operand is AbsoluteOperand memory
                        && instruction.Opcode is not (Opcode.JSR or Opcode.JMP)
                        && (memory.Address >= localEnd && memory.Address < 0x0800
                            || memory.Address is >= 0x0800 and < 0x2000))
                    return false;
                // Dynamic writes can overwrite sp or newly allocated homes. Even
                // direct access to callback vectors makes interrupt effects unknown.
                int yLoad = index - 1;
                if (yLoad >= 0 && block[yLoad] is { Opcode: Opcode.LDA,
                    Mode: AddressMode.Absolute or AddressMode.Immediate })
                    yLoad--;
                bool frameStore = instruction is { Opcode: Opcode.STA, Mode: AddressMode.IndirectIndexed,
                        Operand: ImmediateOperand { Value: NESConstants.sp } }
                    && yLoad >= 0 && block[yLoad] is { Opcode: Opcode.LDY, Mode: AddressMode.Immediate,
                        Operand: ImmediateOperand slot } && slot.Value < depth
                    && !Enumerable.Range(yLoad + 1, index - yLoad).Any(IsBranchTarget);
                if (!frameStore && (instruction.Opcode is Opcode.STA or Opcode.STX or Opcode.STY or Opcode.INC or Opcode.DEC
                    || instruction.Mode != AddressMode.Accumulator
                        && instruction.Opcode is Opcode.ASL or Opcode.LSR or Opcode.ROL or Opcode.ROR))
                {
                    int address = instruction.Operand switch
                    {
                        ImmediateOperand value => value.Value,
                        AbsoluteOperand absolute => absolute.Address,
                        _ => -1,
                    };
                    if (instruction.Mode is not (AddressMode.Absolute or AddressMode.ZeroPage)
                        || address is NESConstants.sp or NESConstants.sp + 1
                        || address is >= NESConstants.NMI_CALLBACK and <= NESConstants.NMI_CALLBACK + 2
                        || address is >= NESConstants.IRQ_CALLBACK and <= NESConstants.IRQ_CALLBACK + 2)
                        return false;
                }
                if (instruction.Opcode == Opcode.JSR)
                {
                    if (instruction.Operand is not LabelOperand call)
                        return false;
                    if (!TryAnalyze(call.Label, out var callee))
                        return false;
                    minimum = Math.Min(minimum, depth + callee.Minimum);
                    maximum = Math.Max(maximum, depth + callee.Maximum);
                    depth += callee.Delta;
                }
                if (instruction.Opcode == Opcode.RTS)
                {
                    if (exitDepth.HasValue && exitDepth != depth)
                        return false;
                    exitDepth = depth;
                    continue;
                }
                if (instruction.Opcode == Opcode.JMP || instruction.Mode == AddressMode.Relative)
                {
                    int target = instruction.Operand switch
                    {
                        LabelOperand label when labels.TryGetValue(label.Label, out int value) => value,
                        RelativeOperand label when labels.TryGetValue(label.Label, out int value) => value,
                        RelativeByteOperand relative => Array.IndexOf(offsets, offsets[index] + instruction.Size + relative.Offset),
                        _ => -1,
                    };
                    if (target < 0)
                        return false;
                    // The main fixture's terminal self-loop is an exit, not a call.
                    if (block.Label == "main" && target == index && instruction.Opcode == Opcode.JMP)
                    {
                        if (exitDepth.HasValue && exitDepth != depth)
                            return false;
                        exitDepth = depth;
                        continue;
                    }
                    pending.Push((target, depth));
                    if (instruction.Opcode == Opcode.JMP)
                        continue;
                }
                pending.Push((index + 1, depth));
            }
            if (!exitDepth.HasValue)
                return false;
            effect = new(exitDepth.Value, minimum, maximum);
            return true;
        }
    }

    HashSet<string> FindFlagDependentCalls(ILInstruction[] main)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var body in UserMethods.Values.Concat(new[] { main }))
        {
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i].OpCode != ILOpCode.Call || body[i].String is not string name)
                    continue;
                int next = i + 1;
                while (next < body.Length && body[next].OpCode is ILOpCode.Nop or ILOpCode.Conv_u1)
                    next++;
                // Current boolean call-result branches rely on callee flags rather
                // than testing A. Do not mask that lowering defect by removing incsp1.
                if (next < body.Length && body[next].OpCode is
                    ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s)
                    result.Add(name);
            }
        }
        return result;
    }

    void AddCallees(ILInstruction[] instructions, HashSet<string> reachable)
    {
        var pending = new Stack<ILInstruction[]>();
        pending.Push(instructions);
        while (pending.Count > 0)
        {
            foreach (var instruction in pending.Pop())
            {
                if (instruction.OpCode == ILOpCode.Call && instruction.String is string name
                    && UserMethods.TryGetValue(name, out var body) && reachable.Add(name))
                    pending.Push(body);
            }
        }
    }

    bool IsPrivateOrLocalFunction(MethodDefinition method)
    {
        if ((method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private)
            return true;

        // Roslyn gives static local functions assembly visibility even though their
        // authored accessibility is local to the containing method.
        string name = _reader.GetString(method.Name);
        return name.StartsWith("<", StringComparison.Ordinal) && name.Contains(">g__")
            && method.GetCustomAttributes().Any(h =>
                GetAttributeTypeName(_reader.GetCustomAttribute(h)) == "CompilerGeneratedAttribute");
    }

    bool HasByteHelperSignature(MethodDefinition method)
    {
        if (!IsPrivateOrLocalFunction(method)
            || (method.Attributes & MethodAttributes.Static) == 0
            || method.ImplAttributes != MethodImplAttributes.IL
            || method.GetCustomAttributes().Any(h =>
                GetAttributeTypeName(_reader.GetCustomAttribute(h)) != "CompilerGeneratedAttribute"))
            return false;

        var signature = _reader.GetBlobReader(method.Signature);
        return signature.Length == 4
            && signature.ReadByte() == 0 // non-generic static managed method
            && signature.ReadCompressedInteger() == 1
            && signature.ReadByte() is 0x01 or 0x05 // void or byte
            && signature.ReadByte() == 0x05;
    }

    bool HasByteHelperBody(string name, MethodDefinition method)
    {
        var body = _pe.GetMethodBody(method.RelativeVirtualAddress);
        if (body.ExceptionRegions.Length != 0 || UserMethods[name].Length > 64)
            return false;
        if (!body.LocalSignature.IsNil)
        {
            var signature = _reader.GetBlobReader(_reader.GetStandaloneSignature(body.LocalSignature).Signature);
            if (signature.ReadByte() != 0x07)
                return false;
            int count = signature.ReadCompressedInteger();
            if (count > 4 || signature.RemainingBytes != count)
                return false;
            for (int i = 0; i < count; i++)
                if (signature.ReadByte() != 0x05)
                    return false;
        }

        foreach (var instruction in UserMethods[name])
        {
            if (instruction.GetLdcValue() is int value)
            {
                if (value is < 0 or > byte.MaxValue)
                    return false;
                continue;
            }
            if (instruction.OpCode is not (ILOpCode.Nop or ILOpCode.Ldarg_0
                or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s
                or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Stloc_s
                or ILOpCode.Add or ILOpCode.Sub or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor
                or ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un or ILOpCode.Conv_u1
                or ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un
                or ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s
                or ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Beq or ILOpCode.Beq_s
                or ILOpCode.Bne_un or ILOpCode.Bne_un_s or ILOpCode.Bgt or ILOpCode.Bgt_s
                or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s or ILOpCode.Bge or ILOpCode.Bge_s
                or ILOpCode.Bge_un or ILOpCode.Bge_un_s or ILOpCode.Blt or ILOpCode.Blt_s
                or ILOpCode.Blt_un or ILOpCode.Blt_un_s or ILOpCode.Ble or ILOpCode.Ble_s
                or ILOpCode.Ble_un or ILOpCode.Ble_un_s or ILOpCode.Call or ILOpCode.Ret))
                return false;
        }
        return true;
    }
}
