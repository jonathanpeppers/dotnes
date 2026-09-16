using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// Checks source-visible mapper effects without modifying interrupt callbacks.
/// The caller supplies the non-nesting interrupt contract and owns gates and dispatchers.
/// </summary>
internal static class ManagedMapperSafety
{
    internal static void Prepare(IReadOnlyList<Program6502> programs,
        IReadOnlyCollection<string> callbackLabels, IReadOnlyCollection<string> foregroundLabels,
        IReadOnlyCollection<string> bankedMethodLabels, ushort selectorShadow, int homeBank,
        IReadOnlyList<NativeRamCode>? nativeRamCode = null,
        IReadOnlyCollection<Block>? managedBlocks = null, IReadOnlyCollection<Block>? compilerOwnedBlocks = null,
        IReadOnlyList<CompiledPrgAsset>? prgAssets = null)
    {
        new Analysis(programs, callbackLabels, foregroundLabels, bankedMethodLabels,
            selectorShadow, homeBank, nativeRamCode ?? Array.Empty<NativeRamCode>(),
            managedBlocks, compilerOwnedBlocks, prgAssets ?? Array.Empty<CompiledPrgAsset>()).Run();
    }

    [Flags]
    enum Context { Foreground = 1, Callback = 2, Banked = 4 }

    // Known bits retain useful pointer bounds even when a pointer's low byte is dynamic.
    readonly record struct Value(byte Bits, byte Mask)
    {
        public static Value Unknown => default;
        public static Value Constant(int value) => new((byte)value, 255);
        public int Min => Bits & Mask;
        public int Max => Min | (255 ^ Mask);
        public bool IsConstant => Mask == 255;
        public Value Merge(Value other)
        {
            byte mask = (byte)(Mask & other.Mask & ~(Bits ^ other.Bits));
            return new((byte)(Bits & mask), mask);
        }
        public Value And(Value other)
        {
            int zero = (Mask & ~Bits) | (other.Mask & ~other.Bits);
            int one = Mask & Bits & other.Mask & other.Bits;
            return new((byte)one, (byte)(zero | one));
        }
        public Value Or(Value other)
        {
            int one = (Mask & Bits) | (other.Mask & other.Bits);
            int zero = Mask & ~Bits & other.Mask & ~other.Bits;
            return new((byte)one, (byte)(zero | one));
        }
        public Value Xor(Value other)
        {
            byte mask = (byte)(Mask & other.Mask);
            return new((byte)((Bits ^ other.Bits) & mask), mask);
        }
        public Value Add(int delta) => IsConstant ? Constant(Bits + delta) : Unknown;
    }

    sealed class State
    {
        public Value A, X, Y;
        public Value Selector = new(6, 0x7f);
        public byte SelectorRegisters = 1 << 6;
        public Value Carry;
        public Value Zero, Negative, Overflow;
        public Value R7;
        public readonly Dictionary<int, Value> Memory = new();
        public readonly List<(bool Status, Value Value)> Stack = new();

        public State Copy()
        {
            var copy = new State
            {
                A = A, X = X, Y = Y, Selector = Selector, SelectorRegisters = SelectorRegisters,
                Carry = Carry, Zero = Zero, Negative = Negative, Overflow = Overflow, R7 = R7
            };
            foreach (var item in Memory)
                copy.Memory.Add(item.Key, item.Value);
            copy.Stack.AddRange(Stack);
            return copy;
        }

        public bool Merge(State other, Node node)
        {
            bool changed = false;
            void MergeValue(ref Value value, Value incoming)
            {
                Value merged = value.Merge(incoming);
                if (merged != value)
                {
                    value = merged;
                    changed = true;
                }
            }
            MergeValue(ref A, other.A);
            MergeValue(ref X, other.X);
            MergeValue(ref Y, other.Y);
            MergeValue(ref Selector, other.Selector);
            MergeValue(ref Carry, other.Carry);
            MergeValue(ref Zero, other.Zero);
            MergeValue(ref Negative, other.Negative);
            MergeValue(ref Overflow, other.Overflow);
            MergeValue(ref R7, other.R7);
            byte registers = (byte)(SelectorRegisters | other.SelectorRegisters);
            if (registers != SelectorRegisters)
            {
                SelectorRegisters = registers;
                changed = true;
            }
            if (Stack.Count != other.Stack.Count)
                throw Error(node, "inconsistent hardware-stack depth across control-flow paths");
            for (int i = 0; i < Stack.Count; i++)
            {
                if (Stack[i].Status != other.Stack[i].Status)
                    throw Error(node, "inconsistent PHA/PHP stack contents across control-flow paths");
                Value merged = Stack[i].Value.Merge(other.Stack[i].Value);
                if (merged != Stack[i].Value)
                {
                    Stack[i] = (Stack[i].Status, merged);
                    changed = true;
                }
            }
            foreach (int address in Memory.Keys.ToArray())
            {
                Value merged = Memory[address].Merge(other.Read(address));
                if (merged != Memory[address])
                {
                    if (merged.Mask == 0)
                        Memory.Remove(address);
                    else
                        Memory[address] = merged;
                    changed = true;
                }
            }
            return changed;
        }

        static int Physical(int address) => address < 0x2000 ? address & 0x7ff : address;
        public Value Read(int address) =>
            Memory.TryGetValue(Physical(address), out var value) ? value : Value.Unknown;

        public void Write(int address, Value value)
        {
            address = Physical(address);
            if (value.Mask == 0)
                Memory.Remove(address);
            else if (address < 0x800 || address >= 0x6000 && address < 0x8000)
                Memory[address] = value;
        }

        public void ForgetRange(int min, int max)
        {
            foreach (int address in Memory.Keys.ToArray())
            {
                bool overlaps = address >= min && address <= max;
                if (address < 0x800)
                    for (int mirror = 0x800; mirror < 0x2000; mirror += 0x800)
                        overlaps |= address + mirror >= min && address + mirror <= max;
                if (overlaps)
                    Memory.Remove(address);
            }
        }

        public void SetZeroNegative(Value value)
        {
            Zero = value.Max == 0 ? Value.Constant(1) : value.Min != 0 ? Value.Constant(0) : Value.Unknown;
            Negative = (value.Mask & 0x80) != 0 ? Value.Constant(value.Bits >> 7) : Value.Unknown;
        }
    }

    sealed class Node(Program6502 program, Block block, int index, int address)
    {
        public readonly Program6502 Program = program;
        public readonly Block Block = block;
        public readonly int Index = index;
        public readonly int Address = address;
        public Instruction Instruction => Block[Index];
        public bool Native => Program.IsNativeBlock(Block);
        public override string ToString() => $"{Block.Label ?? "<anonymous>"}+{Block.GetOffsetAt(Index):X} (${Address:X4})";
    }

    readonly record struct Location(Node Node, string Calls);
    sealed record Frame(Node Return, Node Entry, int StackDepth);
    sealed record Invocation(Node Root, Context Context, bool Native);

    sealed class Analysis
    {
        static readonly Instruction[][] stackReleasePatterns =
        [
            BuiltInSubroutines.Incsp1().InstructionsWithLabels.Select(item => item.Instruction).ToArray(),
            BuiltInSubroutines.Incsp2().InstructionsWithLabels.Select(item => item.Instruction).ToArray(),
            BuiltInSubroutines.Addysp().InstructionsWithLabels.Select(item => item.Instruction).ToArray()
        ];
        readonly IReadOnlyList<Program6502> programs;
        readonly IReadOnlyCollection<string> callbacks, foreground, banked;
        readonly ushort shadow;
        readonly int home;
        readonly IReadOnlyList<NativeRamCode> nativeRamCode;
        readonly IReadOnlyCollection<Block>? suppliedManagedBlocks, compilerOwnedBlocks;
        readonly Dictionary<Program6502, PrgBankAsset> nativePlacements = new();
        readonly HashSet<Node> reachedForeground = new();
        readonly HashSet<Block> reachedBankedBlocks = new();
        readonly Dictionary<Block, Node?> gateEntries = new();
        readonly Dictionary<Program6502, Dictionary<int, Node>> byImage = new();
        readonly Dictionary<Program6502, int> imageIds = new();
        readonly Dictionary<int, List<Node>> byAddress = new();
        readonly Dictionary<string, List<Node>> symbols = new(StringComparer.Ordinal);
        readonly Dictionary<Node, Context> effects = new();
        readonly HashSet<Node> publications = new();
        readonly HashSet<int> foregroundModes = new(), callbackModes = new(), r7Banks = new();
        readonly HashSet<Node> bankedEntries = new();
        readonly HashSet<Block> sourceBlocks = new();
        readonly HashSet<Block> managedBlocks = new();
        int generatedLabel;

        public Analysis(IReadOnlyList<Program6502> programs, IReadOnlyCollection<string> callbacks,
            IReadOnlyCollection<string> foreground, IReadOnlyCollection<string> banked, ushort shadow, int home,
            IReadOnlyList<NativeRamCode> nativeRamCode, IReadOnlyCollection<Block>? managedBlocks,
            IReadOnlyCollection<Block>? compilerOwnedBlocks, IReadOnlyList<CompiledPrgAsset> prgAssets)
        {
            this.programs = programs;
            this.callbacks = callbacks;
            this.foreground = foreground;
            this.banked = banked;
            this.shadow = shadow;
            this.home = home;
            this.nativeRamCode = nativeRamCode;
            suppliedManagedBlocks = managedBlocks;
            this.compilerOwnedBlocks = compilerOwnedBlocks;
            foreach (var asset in prgAssets)
                if (asset.Program != null)
                {
                    if (nativePlacements.ContainsKey(asset.Program))
                        throw new TranspileException("Managed MMC3 native program has more than one physical bank placement.");
                    nativePlacements.Add(asset.Program, asset.Placement);
                }
        }

        public void Run()
        {
            if (home < 0 || home > 63)
                throw new TranspileException("Managed MMC3 home bank must fit the six-bit R6 bank register.");
            IndexPrograms();
            foreach (string label in banked)
                bankedEntries.Add(Root(label));
            var roots = new List<Invocation>();
            roots.AddRange(callbacks.Select(label => new Invocation(Root(label), Context.Callback, true)));
            foreach (string label in foreground)
            {
                if (TryRamRoot(label, out int address))
                {
                    RequireRamContract(address, Context.Foreground);
                    continue;
                }
                Node root = Root(label);
                if (!bankedEntries.Contains(root))
                    roots.Add(new Invocation(root, Context.Foreground, root.Native));
            }
            roots.AddRange(bankedEntries.Select(node => new Invocation(node, Context.Banked, node.Native)));
            if (symbols.TryGetValue("main", out var main))
                roots.Add(new Invocation(Unique(main, "main"), Context.Foreground, false));
            foreach (var root in roots)
                if (!root.Root.Native)
                {
                    if (suppliedManagedBlocks != null && !suppliedManagedBlocks.Contains(root.Root.Block))
                        throw Error(root.Root, "managed root is not registered in the compiler's managed block set");
                    sourceBlocks.Add(root.Root.Block);
                }
            if (suppliedManagedBlocks != null)
                foreach (Block block in suppliedManagedBlocks)
                {
                    if (programs.Any(program => program.IsNativeBlock(block)))
                        throw new TranspileException("Managed MMC3 source-native block cannot be declared compiler-owned managed code.");
                    managedBlocks.Add(block);
                }
            if (compilerOwnedBlocks != null)
                foreach (Block block in compilerOwnedBlocks)
                    if (programs.Any(program => program.IsNativeBlock(block)))
                        throw new TranspileException("Managed MMC3 source-native block cannot be declared a trusted compiler helper.");
            foreach (var invocation in roots.Distinct().OrderBy(root =>
                root.Context == Context.Foreground && root.Root.Block.Label == "main" ? 0 : 1))
            {
                if (invocation.Context == Context.Foreground && invocation.Root.Native && reachedForeground.Contains(invocation.Root))
                    continue;
                if (invocation.Context == Context.Banked && reachedBankedBlocks.Contains(invocation.Root.Block))
                    continue;
                if (invocation.Context == Context.Callback && !invocation.Root.Native)
                    throw Error(invocation.Root, "interrupt callback must resolve to source-visible native code");
                Analyze(invocation);
            }
            foreach (var item in effects)
                if ((item.Value & Context.Callback) != 0 && (item.Value & (Context.Foreground | Context.Banked)) != 0)
                    throw Error(item.Key, "native mapper-writing helper is shared by foreground and interrupt contexts; split the helper");
            if (callbackModes.Count > 1 || callbackModes.Any(mode => foregroundModes.Any(other => mode != other)))
                throw new TranspileException(
                    "Managed MMC3 native callbacks and foreground code disagree on CHR inversion (selector bit 7). " +
                    "Use one agreed CHR inversion mode; callback selector writes are not rewritten.");
            if (r7Banks.Count > 1)
                throw new TranspileException("Managed MMC3 foreground code changes R7 after initialization; use one constant R7 mapping.");
            Instrument();
        }

        void IndexPrograms()
        {
            foreach (var program in programs)
            {
                program.ResolveAddresses();
                var nodes = new Dictionary<int, Node>();
                imageIds.Add(program, imageIds.Count);
                byImage.Add(program, nodes);
                int address = program.BaseAddress;
                foreach (var block in program.Blocks)
                {
                    if (!block.IsDataBlock)
                    {
                        for (int i = 0; i < block.Count; i++)
                        {
                            var node = new Node(program, block, i, address);
                            nodes.Add(address, node);
                            if (!byAddress.TryGetValue(address, out var atAddress))
                                byAddress.Add(address, atAddress = new());
                            atAddress.Add(node);
                            address = checked(address + block[i].Size);
                        }
                    }
                    else
                        address = checked(address + block.Size);
                }
                if (address > 0x10000)
                    throw new TranspileException("Managed MMC3 safety analysis cannot inspect an image extending beyond $FFFF.");
                foreach (var label in program.GetDefinedLabels())
                {
                    if (!nodes.TryGetValue(label.Value, out var node))
                        continue;
                    if (!symbols.TryGetValue(label.Key, out var definitions))
                        symbols.Add(label.Key, definitions = new());
                    if (!definitions.Contains(node))
                        definitions.Add(node);
                }
            }
        }

        static Node Unique(List<Node> nodes, string label)
        {
            if (nodes.Count != 1)
                throw new TranspileException($"Managed MMC3 target '{label}' is ambiguous across program images; use an unambiguous symbolic target.");
            return nodes[0];
        }

        Node Root(string label)
        {
            if (symbols.TryGetValue(label, out var nodes))
                return Unique(nodes, label);
            // The assembler also supports legacy, bare native export spellings.
            if (label.StartsWith("_", StringComparison.Ordinal) && symbols.TryGetValue(label.Substring(1), out nodes))
                return Unique(nodes, label);
            throw new TranspileException($"Managed MMC3 target '{label}' has no source-visible instruction body; provide a native .s definition.");
        }

        bool TryRamRoot(string label, out int address)
        {
            address = -1;
            foreach (var program in programs)
            {
                if (!program.Labels.Labels.TryGetValue(label, out ushort candidate))
                    continue;
                if (address != -1 && address != candidate)
                    throw new TranspileException($"Managed MMC3 native entry '{label}' has ambiguous addresses across program images.");
                address = candidate;
            }
            return address >= 0x6000 && address < 0x8000;
        }

        void RequireRamContract(int address, Context context)
        {
            if (context != Context.Foreground)
                throw new TranspileException(
                    $"Managed MMC3 native RAM entry ${address:X4} is foreground-only; callbacks and banked methods cannot call RAM code.");
            NativeRamCode? declaration = null;
            foreach (var entry in nativeRamCode)
                if (entry.Address == address)
                {
                    if (declaration != null)
                        throw new TranspileException($"Managed MMC3 native RAM entry ${address:X4} has multiple declarations.");
                    declaration = entry;
                }
            if (declaration == null || declaration.Contract != NativeRamCodeContract.ForegroundRtsPreservesMapperContext)
                throw new TranspileException(
                    $"Managed MMC3 RAM transfer to ${address:X4} requires an exact NativeRamCode entry with " +
                    "ForegroundRtsPreservesMapperContext; undeclared and interior RAM entries are unsupported.");
            if (declaration.Size <= 0 || (long)declaration.Address + declaration.Size > 0x8000)
                throw new TranspileException($"Managed MMC3 native RAM entry '{declaration.Name}' must fit completely in $6000-$7FFF.");
        }

        Node Target(Node source)
        {
            Instruction instruction = source.Instruction;
            string? label = instruction.Operand switch
            {
                RelativeOperand value => value.Label,
                LabelOperand value => value.Label,
                LabelOffsetOperand value => value.Label,
                _ => null
            };
            if (label != null)
            {
                string scoped = Scope(source.Block, label);
                if (!symbols.TryGetValue(scoped, out var matches) && !symbols.TryGetValue(label, out matches))
                    throw Error(source, $"unresolved source-visible control-flow target '{label}'");
                Node target = Unique(matches, label);
                if (instruction.Operand is LabelOffsetOperand offset && offset.Offset != 0)
                {
                    if (!byImage[target.Program].TryGetValue(target.Address + offset.Offset, out var offsetTarget))
                        throw Error(source, $"target '{offset}' is not an instruction boundary");
                    target = offsetTarget;
                }
                if (instruction.Mode == AddressMode.Relative && source.Program != target.Program)
                    throw Error(source, "relative branch crosses program images");
                return target;
            }
            if (instruction.Operand is RelativeByteOperand relative)
            {
                if (byImage[source.Program].TryGetValue(source.Address + instruction.Size + relative.Offset, out var target))
                    return target;
                throw Error(source, "numeric relative branch leaves its image or does not land on an instruction boundary");
            }
            if (instruction.Operand is AbsoluteOperand absolute && byAddress.TryGetValue(absolute.Address, out var targets))
                return Unique(targets, $"${absolute.Address:X4} (numeric control flow)");
            throw Error(source, "unresolved control flow; use a direct symbolic target with a source-visible body");
        }

        Node Next(Node source)
        {
            if (byImage[source.Program].TryGetValue(source.Address + source.Instruction.Size, out var next))
                return next;
            throw Error(source, "control flow falls into data or outside the source-visible image instead of returning");
        }

        static string Scope(Block block, string label) =>
            label.StartsWith("@", StringComparison.Ordinal) && block.Label != null ? $"{block.Label}:{label}" : label;

        Node? GateEntry(Block block)
        {
            if (gateEntries.TryGetValue(block, out var entry))
                return entry;
            foreach (var image in byImage.Values)
                foreach (var node in image.Values)
                {
                    if (node.Block != block || node.Instruction.Opcode is not (Opcode.JSR or Opcode.JMP) ||
                        node.Instruction.Operand is not LabelOperand label ||
                        !symbols.TryGetValue(Scope(block, label.Label), out var targets))
                        continue;
                    Node target = Unique(targets, label.Label);
                    if (!bankedEntries.Any(method => method.Block == target.Block))
                        continue;
                    if (entry != null && entry != target)
                        throw Error(node, "compiler gate contains multiple managed entry targets");
                    entry = target;
                }
            gateEntries.Add(block, entry);
            return entry;
        }

        void Analyze(Invocation invocation, State? incoming = null)
        {
            if (invocation.Root.Address >= 0x6000 && invocation.Root.Address < 0x8000)
                RequireRamContract(invocation.Root.Address, invocation.Context);
            var states = new Dictionary<Location, State>();
            var frames = new Dictionary<string, List<Frame>> { [""] = new() };
            var edges = new Dictionary<Location, HashSet<Location>>();
            var returns = new HashSet<Location>();
            var gateCalls = new Dictionary<Location, Node>();
            var queue = new Queue<Location>();
            var initial = incoming?.Copy() ?? new State();
            if (invocation.Context == Context.Callback ||
                invocation.Context == Context.Foreground && invocation.Root.Block.Label != "main")
            {
                initial.Selector = Value.Unknown;
                initial.SelectorRegisters = 255;
            }
            var start = new Location(invocation.Root, "");
            states.Add(start, initial);
            queue.Enqueue(start);
            int iterations = 0;
            while (queue.Count != 0)
            {
                if (++iterations > 100000)
                    throw Error(invocation.Root, "control-flow analysis exceeded its bound; simplify native helper/control-flow structure");
                Location location = queue.Dequeue();
                Node node = location.Node;
                var stack = frames[location.Calls];
                State state = states[location].Copy();
                Instruction instruction = node.Instruction;
                edges[location] = new();
                if (invocation.Context == Context.Foreground)
                    reachedForeground.Add(node);
                else if (invocation.Context == Context.Banked)
                    reachedBankedBlocks.Add(node.Block);
                ValidateNativePlacement(node, state, invocation.Context);

                void Flow(Node next, string calls)
                {
                    if (next.Address >= 0x6000 && next.Address < 0x8000)
                        throw Error(node, "RAM code requires an exact declared foreground JSR/JMP entry, not a branch or fallthrough");
                    if (node.Native && !next.Native)
                        throw Error(node, $"native control flow enters managed/compiler code '{next.Block.Label}'; native callbacks/helpers cannot enter managed banking gates");
                    if (next.Native && (invocation.Context & (Context.Callback | Context.Banked)) != 0 &&
                        next.Address >= 0x8000 && next.Address < 0xa000 && next.Program != invocation.Root.Program)
                        throw Error(node, "native helper in the switchable R6 window is not stably mapped in this context");
                    var successor = new Location(next, calls);
                    edges[location].Add(successor);
                    if (!states.TryGetValue(successor, out var existing))
                    {
                        states.Add(successor, state.Copy());
                        queue.Enqueue(successor);
                    }
                    else if (existing.Merge(state, next))
                        queue.Enqueue(successor);
                }

                void Return()
                {
                    int expectedDepth = stack.Count == 0 ? 0 : stack[stack.Count - 1].StackDepth;
                    if (state.Stack.Count != expectedDepth)
                        throw Error(node, "RTS has an unbalanced PHA/PHP/PLA/PLP hardware stack");
                    if (stack.Count == 0)
                        returns.Add(location);
                    else
                    {
                        Frame frame = stack[stack.Count - 1];
                        string parent = location.Calls.Substring(0, location.Calls.LastIndexOf('/'));
                        // Returning to a managed caller is the one legal native-to-managed edge.
                        var successor = new Location(frame.Return, parent);
                        edges[location].Add(successor);
                        if (!states.TryGetValue(successor, out var existing))
                        {
                            states.Add(successor, state.Copy());
                            queue.Enqueue(successor);
                        }
                        else if (existing.Merge(state, frame.Return))
                            queue.Enqueue(successor);
                    }
                }

                if (node.Native && invocation.Context == Context.Callback && node.Address >= 0x8000 && node.Address < 0xa000)
                    throw Error(node, "interrupt callback/helper resides in the switchable R6 window; place it in fixed code or the stable R7 window");
                if (instruction.Opcode is Opcode.RTI or Opcode.BRK || node.Native && instruction.Opcode == Opcode.TXS)
                    throw Error(node, "native callback/helper must preserve its return stack and use RTS (RTI, BRK, and TXS are unsupported)");
                if (instruction.Opcode == Opcode.RTS)
                {
                    Return();
                    continue;
                }
                if (instruction.Opcode is Opcode.JSR or Opcode.JMP)
                {
                    if (instruction.Mode != AddressMode.Absolute)
                        throw Error(node, "indirect calls/tail jumps are unsupported; use direct source-visible control flow");
                    if (ResolveAddress(node, instruction.Operand, out int address) && address >= 0x6000 && address < 0x8000)
                    {
                        RequireRamContract(address, invocation.Context);
                        state.A = state.X = state.Y = state.Carry = Value.Unknown;
                        state.Zero = state.Negative = state.Overflow = Value.Unknown;
                        state.Memory.Clear();
                        for (int i = 0; i < state.Stack.Count; i++)
                            state.Stack[i] = (state.Stack[i].Status, Value.Unknown);
                        if (instruction.Opcode == Opcode.JSR)
                            Flow(Next(node), location.Calls);
                        else
                            Return();
                        continue;
                    }
                    Node target = Target(node);
                    if (node.Native && bankedEntries.Contains(target))
                        throw Error(node, "native callback/helper calls banked managed code; reentrant banking is unsupported");
                    if (!node.Native && !target.Native && !sourceBlocks.Contains(target.Block) && !managedBlocks.Contains(target.Block))
                    {
                        if (compilerOwnedBlocks == null || !compilerOwnedBlocks.Contains(target.Block))
                            throw Error(node, $"target '{target.Block.Label}' is neither authored managed code nor a registered compiler-owned helper");
                        Node? entry = GateEntry(target.Block);
                        if (entry != null)
                        {
                            if (invocation.Context != Context.Foreground)
                                throw Error(node, "banked code cannot reenter a managed banking gate");
                            gateCalls[location] = entry;
                        }
                        // Compiler-owned calls/gates are validated by managed call-graph analysis.
                        // Never enter startup, runtime dispatchers or generated gates here.
                        if (instruction.Opcode == Opcode.JMP)
                        {
                            if (target.Block == node.Block)
                                Flow(target, location.Calls);
                            else
                            {
                                state.A = state.X = state.Y = state.Carry = Value.Unknown;
                                state.Zero = state.Negative = state.Overflow = Value.Unknown;
                                state.Memory.Clear();
                                Return();
                            }
                        }
                        else
                        {
                            state.A = state.X = state.Y = state.Carry = Value.Unknown;
                            state.Zero = state.Negative = state.Overflow = Value.Unknown;
                            state.Memory.Clear();
                            Flow(Next(node), location.Calls);
                        }
                        continue;
                    }
                    if (instruction.Opcode == Opcode.JMP)
                        Flow(target, location.Calls);
                    else
                    {
                        if (stack.Count >= 32 || target == invocation.Root || stack.Any(frame => frame.Entry == target))
                            throw Error(node, "recursive native helper calls are unsupported by managed mapper safety");
                        Node next = Next(node);
                        string calls = location.Calls + "/" + imageIds[node.Program] + ":" + node.Address;
                        if (!frames.ContainsKey(calls))
                        {
                            var nested = new List<Frame>(stack) { new(next, target, state.Stack.Count) };
                            frames.Add(calls, nested);
                        }
                        Flow(target, calls);
                    }
                    continue;
                }
                Transfer(node, state, stack.Count == 0 ? 0 : stack[stack.Count - 1].StackDepth);
                if (instruction.Mode == AddressMode.Relative)
                {
                    bool? taken = BranchTaken(instruction.Opcode, state);
                    if (taken != false)
                        Flow(Target(node), location.Calls);
                    if (taken == true)
                        continue;
                }
                Flow(Next(node), location.Calls);
            }

            // An exit must be reachable from every callback path. Conditional polling loops
            // are allowed; this is not a proof of callback timing or interrupt non-nesting.
            if (invocation.Context == Context.Callback)
            {
                var canReturn = new HashSet<Location>(returns);
                bool changed;
                do
                {
                    changed = false;
                    foreach (var edge in edges)
                        if (!canReturn.Contains(edge.Key) && edge.Value.Any(canReturn.Contains))
                            changed |= canReturn.Add(edge.Key);
                } while (changed);
                foreach (Location location in states.Keys)
                    if (!canReturn.Contains(location))
                        throw Error(location.Node, "callback control-flow path cannot return through the stock dispatcher");
            }
            foreach (var entry in states)
                ValidateStore(entry.Key.Node, entry.Value, invocation.Context);
            foreach (var call in gateCalls)
            {
                State input = states[call.Key].Copy();
                input.Stack.Clear();
                input.Selector = new Value((byte)((input.Selector.Bits & 0x80) | 6),
                    (byte)((input.Selector.Mask & 0x80) | 0x7f));
                input.SelectorRegisters = 1 << 6;
                Analyze(new Invocation(call.Value, Context.Banked, false), input);
            }
        }

        bool ResolveAddress(Node node, Operand? operand, out int address)
        {
            switch (operand)
            {
                case AbsoluteOperand value:
                    address = value.Address;
                    return true;
                case ImmediateOperand value:
                    address = value.Value;
                    return true;
                case LabelOperand value:
                    return ResolveLabelAddress(node, value.Label, out address);
                case LabelOffsetOperand value:
                    if (ResolveLabelAddress(node, value.Label, out address))
                    {
                        address = (address + value.Offset) & 0xffff;
                        return true;
                    }
                    break;
            }
            address = 0;
            return false;
        }

        void ValidateNativePlacement(Node node, State state, Context context)
        {
            if (!node.Native || node.Address < 0x8000 || node.Address >= 0xc000)
                return;
            if (!nativePlacements.TryGetValue(node.Program, out var placement) ||
                node.Program.BaseAddress != placement.CpuAddress + placement.Offset)
                throw Error(node, "switchable native code has no matching physical PRG asset placement; pass its compiled asset metadata");
            if (placement.CpuAddress == 0x8000)
            {
                if (context != Context.Foreground || placement.Bank != home)
                    throw Error(node, $"native R6 code is not in foreground home bank {home}; use a fixed or proven R7 placement");
            }
            else if (placement.CpuAddress == 0xa000)
            {
                if (!state.R7.IsConstant || state.R7.Bits != placement.Bank)
                    throw Error(node, $"native R7 code requires physical bank {placement.Bank}; initialize R7 to that bank on every incoming call path");
            }
            else
                throw Error(node, "native PRG asset must use an R6 $8000 or R7 $A000 window");
        }

        static bool ResolveLabelAddress(Node node, string label, out int address)
        {
            var labels = node.Program.Labels.Labels;
            if (labels.TryGetValue(Scope(node.Block, label), out ushort value) || labels.TryGetValue(label, out value))
            {
                address = value;
                return true;
            }
            address = 0;
            return false;
        }

        bool HasManagedSoftwareStackProof(Node node) =>
            !node.Native && managedBlocks.Contains(node.Block) &&
            node.Instruction.Mode == AddressMode.IndirectIndexed &&
            ResolveAddress(node, node.Instruction.Operand, out int address) && (address & 255) == NESConstants.sp;

        (int Min, int Max) AddressRange(Node node, State state)
        {
            Instruction instruction = node.Instruction;
            if (!ResolveAddress(node, instruction.Operand, out int address))
                return (0, 65535);
            Value index = instruction.Mode is AddressMode.AbsoluteX or AddressMode.ZeroPageX or AddressMode.IndexedIndirect
                ? state.X : state.Y;
            switch (instruction.Mode)
            {
                case AddressMode.ZeroPage:
                    return (address & 255, address & 255);
                case AddressMode.ZeroPageX:
                case AddressMode.ZeroPageY:
                    if (index.IsConstant)
                        return ((address + index.Bits) & 255, (address + index.Bits) & 255);
                    return (0, 255);
                case AddressMode.Absolute:
                    return (address, address);
                case AddressMode.AbsoluteX:
                case AddressMode.AbsoluteY:
                    if (address + index.Max > 65535)
                        return index.IsConstant ? ((address + index.Bits) & 65535, (address + index.Bits) & 65535) : (0, 65535);
                    return (address + index.Min, address + index.Max);
                case AddressMode.IndexedIndirect:
                    if (!index.IsConstant)
                        return (0, 65535);
                    address = (address + index.Bits) & 255;
                    index = Value.Constant(0);
                    break;
                case AddressMode.IndirectIndexed:
                    address &= 255;
                    // Managed argument lowering and the frame allocator jointly establish
                    // this bound. Source-native accesses never inherit that compiler proof.
                    if (HasManagedSoftwareStackProof(node))
                        return (NESConstants.LocalStackBase, NESConstants.LocalStackBase + NESConstants.MaxLocalBytes - 1);
                    break;
                default:
                    return (0, 65535);
            }
            Value low = state.Read(address), high = state.Read((address + 1) & 255);
            int min = high.Min * 256 + low.Min + index.Min;
            int max = high.Max * 256 + low.Max + index.Max;
            if (max > 65535)
                return min == max ? (min & 65535, max & 65535) : (0, 65535);
            return (min, max);
        }

        Value Load(Node node, State state)
        {
            Instruction instruction = node.Instruction;
            if (instruction.Mode == AddressMode.Immediate && instruction.Operand is ImmediateOperand immediate)
                return Value.Constant(immediate.Value);
            if (instruction.Operand is LowByteOperand or HighByteOperand)
            {
                string label = instruction.Operand is LowByteOperand low ? low.Label : ((HighByteOperand)instruction.Operand).Label;
                // RAM constants do not move during branch relaxation, unlike code addresses.
                if (ResolveLabelAddress(node, label, out int address) && address < 0x8000)
                    return Value.Constant(instruction.Operand is LowByteOperand ? address : address >> 8);
                return Value.Unknown;
            }
            var range = AddressRange(node, state);
            return range.Min == range.Max ? state.Read(range.Min) : Value.Unknown;
        }

        static bool IsStore(Instruction instruction) =>
            instruction.Opcode is Opcode.STA or Opcode.STX or Opcode.STY ||
            instruction.Mode != AddressMode.Accumulator && instruction.Opcode is Opcode.INC or Opcode.DEC or Opcode.ASL or Opcode.LSR or Opcode.ROL or Opcode.ROR;

        static Value StoreValue(Instruction instruction, State state) => instruction.Opcode switch
        {
            Opcode.STA => state.A,
            Opcode.STX => state.X,
            Opcode.STY => state.Y,
            _ => Value.Unknown
        };

        static bool? BranchTaken(Opcode opcode, State state)
        {
            Value flag = opcode switch
            {
                Opcode.BEQ or Opcode.BNE => state.Zero,
                Opcode.BCC or Opcode.BCS => state.Carry,
                Opcode.BMI or Opcode.BPL => state.Negative,
                Opcode.BVC or Opcode.BVS => state.Overflow,
                _ => Value.Unknown
            };
            if (!flag.IsConstant)
                return null;
            bool whenSet = opcode is Opcode.BEQ or Opcode.BCS or Opcode.BMI or Opcode.BVS;
            return (flag.Bits != 0) == whenSet;
        }

        void Transfer(Node node, State state, int stackFloor)
        {
            Instruction instruction = node.Instruction;
            Value value = Load(node, state);
            switch (instruction.Opcode)
            {
                case Opcode.LDA: state.A = value; break;
                case Opcode.LDX: state.X = value; break;
                case Opcode.LDY: state.Y = value; break;
                case Opcode.TAX: state.X = state.A; break;
                case Opcode.TAY: state.Y = state.A; break;
                case Opcode.TXA: state.A = state.X; break;
                case Opcode.TYA: state.A = state.Y; break;
                case Opcode.TSX: state.X = Value.Unknown; break;
                case Opcode.AND: state.A = state.A.And(value); break;
                case Opcode.ORA: state.A = state.A.Or(value); break;
                case Opcode.EOR: state.A = state.A.Xor(value); break;
                case Opcode.INX: state.X = state.X.Add(1); break;
                case Opcode.INY: state.Y = state.Y.Add(1); break;
                case Opcode.DEX: state.X = state.X.Add(-1); break;
                case Opcode.DEY: state.Y = state.Y.Add(-1); break;
                case Opcode.CLC: state.Carry = Value.Constant(0); break;
                case Opcode.SEC: state.Carry = Value.Constant(1); break;
                case Opcode.CLV: state.Overflow = Value.Constant(0); break;
                case Opcode.BIT:
                    Value tested = state.A.And(value);
                    state.Zero = tested.Max == 0 ? Value.Constant(1) : tested.Min != 0 ? Value.Constant(0) : Value.Unknown;
                    state.Negative = (value.Mask & 0x80) != 0 ? Value.Constant(value.Bits >> 7) : Value.Unknown;
                    state.Overflow = (value.Mask & 0x40) != 0 ? Value.Constant((value.Bits >> 6) & 1) : Value.Unknown;
                    break;
                case Opcode.CMP:
                case Opcode.CPX:
                case Opcode.CPY:
                    Value compared = instruction.Opcode == Opcode.CMP ? state.A : instruction.Opcode == Opcode.CPX ? state.X : state.Y;
                    state.Carry = compared.IsConstant && value.IsConstant ? Value.Constant(compared.Bits >= value.Bits ? 1 : 0) : Value.Unknown;
                    state.SetZeroNegative(compared.IsConstant && value.IsConstant ? Value.Constant(compared.Bits - value.Bits) : Value.Unknown);
                    break;
                case Opcode.ADC:
                case Opcode.SBC:
                    if (state.A.IsConstant && value.IsConstant && state.Carry.IsConstant)
                    {
                        int sum = state.A.Bits + (instruction.Opcode == Opcode.ADC ? value.Bits : 255 - value.Bits) + state.Carry.Bits;
                        int overflow = instruction.Opcode == Opcode.ADC
                            ? ~(state.A.Bits ^ value.Bits) & (state.A.Bits ^ sum)
                            : (state.A.Bits ^ value.Bits) & (state.A.Bits ^ sum);
                        state.Overflow = Value.Constant((overflow >> 7) & 1);
                        state.A = Value.Constant(sum);
                        state.Carry = Value.Constant(sum > 255 ? 1 : 0);
                    }
                    else
                        state.A = state.Carry = state.Overflow = Value.Unknown;
                    break;
                case Opcode.PHA: state.Stack.Add((false, state.A)); break;
                case Opcode.PHP: state.Stack.Add((true, state.Carry)); break;
                case Opcode.PLA:
                case Opcode.PLP:
                    bool status = instruction.Opcode == Opcode.PLP;
                    if (state.Stack.Count <= stackFloor || state.Stack[state.Stack.Count - 1].Status != status)
                        throw Error(node, "unmatched or type-mismatched hardware-stack pull; cannot prove the native return address is preserved");
                    Value pulled = state.Stack[state.Stack.Count - 1].Value;
                    state.Stack.RemoveAt(state.Stack.Count - 1);
                    if (status)
                    {
                        state.Carry = pulled;
                        state.Zero = state.Negative = state.Overflow = Value.Unknown;
                    }
                    else state.A = pulled;
                    break;
                case Opcode.ASL:
                case Opcode.LSR:
                case Opcode.ROL:
                case Opcode.ROR:
                    Value input = instruction.Mode == AddressMode.Accumulator ? state.A : value;
                    bool left = instruction.Opcode is Opcode.ASL or Opcode.ROL;
                    bool rotate = instruction.Opcode is Opcode.ROL or Opcode.ROR;
                    Value shifted = input.IsConstant && (!rotate || state.Carry.IsConstant)
                        ? Value.Constant(left ? input.Bits * 2 + (rotate ? state.Carry.Bits : 0) :
                            input.Bits / 2 + (rotate ? state.Carry.Bits * 128 : 0))
                        : Value.Unknown;
                    state.Carry = input.IsConstant ? Value.Constant(left ? input.Bits >> 7 : input.Bits & 1) : Value.Unknown;
                    if (instruction.Mode == AddressMode.Accumulator)
                        state.A = shifted;
                    value = shifted;
                    state.SetZeroNegative(shifted);
                    break;
                case Opcode.INC: value = value.Add(1); break;
                case Opcode.DEC: value = value.Add(-1); break;
            }
            switch (instruction.Opcode)
            {
                case Opcode.LDA:
                case Opcode.TXA:
                case Opcode.TYA:
                case Opcode.AND:
                case Opcode.ORA:
                case Opcode.EOR:
                case Opcode.ADC:
                case Opcode.SBC:
                case Opcode.PLA:
                    state.SetZeroNegative(state.A);
                    break;
                case Opcode.LDX:
                case Opcode.TAX:
                case Opcode.TSX:
                case Opcode.INX:
                case Opcode.DEX:
                    state.SetZeroNegative(state.X);
                    break;
                case Opcode.LDY:
                case Opcode.TAY:
                case Opcode.INY:
                case Opcode.DEY:
                    state.SetZeroNegative(state.Y);
                    break;
                case Opcode.INC:
                case Opcode.DEC:
                    state.SetZeroNegative(value);
                    break;
            }
            if (!IsStore(instruction))
                return;
            var range = AddressRange(node, state);
            Value stored = instruction.Opcode is Opcode.STA or Opcode.STX or Opcode.STY ? StoreValue(instruction, state) : value;
            if (range.Min == range.Max)
            {
                state.Write(range.Min, stored);
                if (range.Min >= 0x8000 && range.Min <= 0x9fff && (range.Min & 1) == 0)
                {
                    state.Selector = stored;
                    state.SelectorRegisters = 0;
                    for (int register = 0; register < 8; register++)
                        if ((register & stored.Mask & 7) == (stored.Bits & stored.Mask & 7))
                            state.SelectorRegisters |= (byte)(1 << register);
                }
                else if (range.Min >= 0x8000 && range.Min <= 0x9fff && (state.SelectorRegisters & (1 << 7)) != 0)
                {
                    Value bank = stored.And(Value.Constant(0x3f));
                    state.R7 = state.SelectorRegisters == (1 << 7) ? bank : state.R7.Merge(bank);
                }
            }
            else
                state.ForgetRange(range.Min, range.Max);
        }

        void ValidateStore(Node node, State state, Context context)
        {
            Instruction instruction = node.Instruction;
            if (!IsStore(instruction))
                return;
            var range = AddressRange(node, state);
            // The allocator reserves adjacent selector/saved-selector bytes. Only its
            // proven software-stack accesses can exclude those bytes from a coarse RAM bound.
            if (!HasManagedSoftwareStackProof(node) &&
                (IntersectsRamAddress(range, shadow) || IntersectsRamAddress(range, shadow + 1)))
                throw Error(node,
                    "source store may overwrite the compiler-owned selector context (selector or saved selector); " +
                    "use a RAM buffer that excludes both reserved bytes and their NES RAM mirrors");
            bool touchesSoftwareStack = false;
            for (int mirror = 0; mirror < 0x2000; mirror += 0x800)
                touchesSoftwareStack |= range.Min <= mirror + NESConstants.sp + 1 && range.Max >= mirror + NESConstants.sp;
            if (touchesSoftwareStack && !IsManagedStackAdjustment(node))
                throw Error(node,
                    "source store may overwrite the compiler-owned software-stack pointer $22/$23; " +
                    "preserve that pointer and use a separately bounded native RAM pointer");
            if (node.Native)
            {
                for (int page = 0x100; page < 0x2000; page += 0x800)
                    if (range.Min <= page + 0xff && range.Max >= page)
                        throw Error(node, "native store may overwrite the hardware return stack; use a provably separate RAM buffer");
            }
            if (range.Max < 0x8000)
                return;
            if (range.Min != range.Max)
                throw Error(node, "indexed/indirect store may reach MMC3 registers; establish a bounded RAM pointer/index or use a direct mapper address");
            effects[node] = effects.TryGetValue(node, out var previous) ? previous | context : context;
            if (range.Min >= 0xa000)
                return;
            if (instruction.Opcode is not (Opcode.STA or Opcode.STX or Opcode.STY))
                throw Error(node, "read/modify/write of MMC3 registers is unsupported; use STA, STX, or STY");
            Value stored = StoreValue(instruction, state);
            if ((range.Min & 1) == 0)
            {
                if (!stored.IsConstant)
                    throw Error(node, "dynamic MMC3 selector cannot be proven safe; load a constant selector (PRG mode 0) before the store");
                if ((stored.Bits & 0x40) != 0)
                    throw Error(node, "MMC3 PRG mode 1 is incompatible with managed R6 banking; use selector bit 6 = 0");
                if ((context & Context.Callback) != 0)
                    callbackModes.Add(stored.Bits & 0x80);
                else
                {
                    foregroundModes.Add(stored.Bits & 0x80);
                    publications.Add(node);
                }
                return;
            }
            if ((state.Selector.Mask & 0x40) == 0 || (state.Selector.Bits & 0x40) != 0)
                throw Error(node, "MMC3 data store has an unknown selector; select a known register with PRG mode 0 on every incoming path");
            if ((state.SelectorRegisters & 0xc0) == 0)
                return;
            if (context is Context.Callback or Context.Banked)
                throw Error(node, "native callback or banked method changes PRG R6/R7; only CHR R0-R5 writes are supported in this context");
            if (!stored.IsConstant)
                throw Error(node, "foreground PRG R6/R7 data must be constant; dynamic PRG remapping is incompatible with managed banking");
            if ((state.SelectorRegisters & (1 << 6)) != 0 && (stored.Bits & 0x3f) != home)
                throw Error(node, $"foreground PRG R6 write must preserve managed home bank {home}");
            if ((state.SelectorRegisters & (1 << 7)) != 0)
                r7Banks.Add(stored.Bits & 0x3f);
        }

        static bool IntersectsRamAddress((int Min, int Max) range, int address)
        {
            if (address >= 0x2000)
                return range.Min <= address && range.Max >= address;
            for (int mirror = address & 0x7ff; mirror < 0x2000; mirror += 0x800)
                if (range.Min <= mirror && range.Max >= mirror)
                    return true;
            return false;
        }

        bool IsManagedStackAdjustment(Node node)
        {
            if (node.Native || !managedBlocks.Contains(node.Block))
                return false;
            Block block = node.Block;
            foreach (var pattern in stackReleasePatterns)
            {
                int releaseStart = block.Count - pattern.Length;
                if (releaseStart < 0 || node.Index < releaseStart)
                    continue;
                bool matches = true;
                for (int index = 0; index < pattern.Length; index++)
                {
                    Instruction instruction = block[releaseStart + index];
                    matches &= instruction.Opcode == pattern[index].Opcode &&
                        instruction.Mode == pattern[index].Mode && instruction.Operand == pattern[index].Operand;
                }
                if (matches)
                    return true;
            }
            int start = node.Instruction.Opcode == Opcode.STA ? node.Index - 3 :
                node.Instruction.Opcode == Opcode.DEC ? node.Index - 5 : -1;
            if (start < 0 || start + 5 >= block.Count)
                return false;
            bool IsPointerInstruction(int index, Opcode opcode, int address) =>
                block[index].Opcode == opcode && block[index].Mode == AddressMode.ZeroPage &&
                block[index].Operand is ImmediateOperand operand && operand.Value == address;
            // Canonical byte-call frame reservation: subtract size and propagate borrow.
            return IsPointerInstruction(start, Opcode.LDA, NESConstants.sp) &&
                block[start + 1].Opcode == Opcode.SEC &&
                block[start + 2] is { Opcode: Opcode.SBC, Mode: AddressMode.Immediate, Operand: ImmediateOperand } &&
                IsPointerInstruction(start + 3, Opcode.STA, NESConstants.sp) &&
                block[start + 4] is { Opcode: Opcode.BCS, Mode: AddressMode.Relative, Operand: RelativeByteOperand { Offset: 2 } } &&
                IsPointerInstruction(start + 5, Opcode.DEC, NESConstants.sp + 1);
        }

        void Instrument()
        {
            if (publications.Count == 0)
                return;
            var relocations = new List<(Node Source, Node Target)>();
            // Insertion changes numeric offsets even when the branch itself is not reachable
            // from a mapper-writing root. Capture every affected image before any mutation.
            foreach (var program in programs)
                foreach (var node in byImage[program].Values)
                {
                    if (node.Instruction.Operand is RelativeByteOperand relative)
                    {
                        int address = node.Address + node.Instruction.Size + relative.Offset;
                        if (publications.Any(store => store.Program == program &&
                            store.Address >= Math.Min(node.Address, address) &&
                            store.Address <= Math.Max(node.Address, address)))
                            relocations.Add((node, Target(node)));
                    }
                    else if (node.Instruction.Opcode is Opcode.JMP or Opcode.JSR && node.Instruction.Operand is AbsoluteOperand absolute &&
                        byAddress.TryGetValue(absolute.Address, out var candidates) &&
                        candidates.Any(target => publications.Any(store => store.Program == target.Program && store.Address <= target.Address)))
                        relocations.Add((node, Unique(candidates, $"${absolute.Address:X4} (numeric control flow)")));
                }
            foreach (var relocation in relocations)
            {
                Node target = relocation.Target;
                string? label = target.Block.GetLabelAt(target.Index);
                if (label == null && target.Block.Label != null && target.Block.GetOffsetAt(target.Index) == target.Block.LabelOffset)
                    label = target.Block.Label;
                if (label == null)
                {
                    do { label = $"__managed_mapper_target_{generatedLabel++}"; }
                    while (symbols.ContainsKey(label) || programs.Any(program => program.Labels.Labels.ContainsKey(label)));
                    target.Block.SetLabel(target.Index, label);
                }
                label = Scope(target.Block, label);
                Instruction original = relocation.Source.Instruction;
                Operand operand = original.Mode == AddressMode.Relative
                    ? new RelativeOperand(label)
                    : new LabelOperand(label, OperandSize.Word);
                relocation.Source.Block.Replace(relocation.Source.Index, original with { Operand = operand });
            }
            foreach (var group in publications.GroupBy(node => node.Block))
            {
                Block block = group.Key;
                foreach (Node node in group.OrderByDescending(node => node.Index))
                {
                    int offset = block.GetOffsetAt(node.Index);
                    string? label = block.GetLabelAt(node.Index);
                    block.SetLabel(node.Index, null);
                    block.Insert(node.Index, new Instruction(node.Instruction.Opcode, AddressMode.Absolute,
                        new AbsoluteOperand(shadow), "Publish foreground MMC3 selector before the hardware write"), label);
                    if (offset < block.LabelOffset)
                        block.LabelOffset += 3;
                }
            }
            foreach (var program in programs)
                program.InvalidateAddresses();
        }
    }

    static TranspileException Error(Node node, string message) =>
        new($"Managed MMC3 mapper safety at {node}: {message}.");
}
