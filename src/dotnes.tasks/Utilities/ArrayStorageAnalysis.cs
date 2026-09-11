using System.Reflection.Metadata;

namespace dotnes;

[Flags]
enum ArrayStorage
{
    Unknown = 1,
    Ram = 2,
    Rom = 4,
    Parameter = 8,
}

/// <summary>
/// Resolves array storage through local aliases and shared static-field bindings.
/// It does not infer mutability or introduce a general runtime array-reference ABI.
/// </summary>
sealed class ArrayStorageAnalysis
{
    readonly Dictionary<ILInstruction[], ILValueAnalysis> methods;
    readonly Dictionary<string, List<(ILInstruction[] Method, int Store)>> fields = new(StringComparer.Ordinal);
    readonly IReadOnlyDictionary<ILInstruction[], bool[]> parameters;

    public ArrayStorageAnalysis(IEnumerable<ILInstruction[]> instructions, ReflectionCache reflection,
        IReadOnlyDictionary<ILInstruction[], bool[]>? arrayParameters = null)
    {
        parameters = arrayParameters ?? new Dictionary<ILInstruction[], bool[]>();
        methods = instructions.ToDictionary(il => il, il => new ILValueAnalysis(il, reflection));
        foreach (var pair in methods)
        {
            var il = pair.Key;
            for (int i = 0; i < il.Length; i++)
            {
                if (il[i].OpCode != ILOpCode.Stsfld || il[i].String is not string name)
                    continue;
                if (!fields.TryGetValue(name, out var sources))
                    fields[name] = sources = [];
                sources.Add((il, i));
            }
        }
    }

    public ArrayStorage GetStorage(ILInstruction[] instructions, int producer,
        ISet<(ILInstruction[] Method, int Producer)>? allocations = null) =>
        Query(instructions, producer, null, allocations);

    public ArrayStorage GetInputStorage(ILInstruction[] instructions, int consumer, int argument,
        ISet<(ILInstruction[] Method, int Producer)>? allocations = null) =>
        Query(instructions, consumer, argument, allocations);

    ArrayStorage Query(ILInstruction[] instructions, int instruction, int? argument,
        ISet<(ILInstruction[] Method, int Producer)>? allocations)
    {
        var values = new HashSet<(ILInstruction[], int)>();
        var inputs = new HashSet<(ILInstruction[], int, int)>();
        var slots = new HashSet<(ILInstruction[], int, int)>();
        ArrayStorage result = argument.HasValue ? Input(instructions, instruction, argument.Value) : Value(instructions, instruction);
        return result == 0 ? ArrayStorage.Unknown : result;

        ArrayStorage Value(ILInstruction[] il, int p)
        {
            if (p < 0)
                return ArrayStorage.Unknown;
            if (!values.Add((il, p)))
                return 0;
            if (il[p].OpCode == ILOpCode.Ldtoken)
                return ArrayStorage.Rom;
            if (il[p].OpCode == ILOpCode.Call &&
                il[p].String is nameof(NESLib.meta_spr_2x2) or nameof(NESLib.meta_spr_2x2_flip))
                return ArrayStorage.Rom;
            if (il[p].OpCode == ILOpCode.Newarr)
            {
                allocations?.Add((il, p));
                return ArrayStorage.Ram;
            }
            if (il[p].GetLdargIndex() is int arg && parameters.TryGetValue(il, out var declared) &&
                arg < declared.Length && declared[arg])
                return ArrayStorage.Parameter;
            if (il[p].OpCode == ILOpCode.Ldsfld && il[p].String is string field)
                return fields.TryGetValue(field, out var sources)
                    ? sources.Aggregate((ArrayStorage)0, (storage, source) => storage | Input(source.Method, source.Store, 0))
                    : ArrayStorage.Unknown;
            if (il[p].GetLdlocIndex() is not int local)
                return ArrayStorage.Unknown;

            // Stop each predecessor path at its reaching assignment; future or
            // overwritten bindings must not change the identity at this load.
            var analysis = methods[il];
            var pending = new Stack<int>(analysis.Predecessors[p]);
            var seen = new HashSet<int>();
            ArrayStorage found = 0;
            while (pending.Count > 0)
            {
                int i = pending.Pop();
                if (!seen.Add(i))
                    continue;
                if (il[i].GetStlocIndex() == local)
                    found |= Input(il, i, 0);
                else
                    foreach (int predecessor in analysis.Predecessors[i])
                        pending.Push(predecessor);
            }
            return found;
        }

        ArrayStorage Input(ILInstruction[] il, int consumer, int arg)
        {
            var analysis = methods[il];
            if (arg >= analysis.Inputs[consumer].Length)
                return ArrayStorage.Unknown;
            if (!inputs.Add((il, consumer, arg)))
                return 0;
            int producer = analysis.Inputs[consumer][arg];
            if (producer >= 0)
                return Value(il, producer);
            ArrayStorage found = 0;
            foreach (int predecessor in analysis.Predecessors[consumer])
                found |= Slot(il, predecessor, analysis.Outputs[predecessor].Length - analysis.Inputs[consumer].Length + arg);
            return found;
        }

        ArrayStorage Slot(ILInstruction[] il, int index, int slot)
        {
            var analysis = methods[il];
            if (slot < 0 || slot >= analysis.Outputs[index].Length)
                return ArrayStorage.Unknown;
            if (!slots.Add((il, index, slot)))
                return 0;
            int value = analysis.Outputs[index][slot];
            if (value >= 0)
                return Value(il, value);
            // Unknown identities are inherited stack slots at a join. A dup's
            // extra slot refers to the preceding top slot, not a new allocation.
            if (il[index].OpCode == ILOpCode.Dup && slot == analysis.Outputs[index].Length - 1)
                slot--;
            ArrayStorage found = 0;
            foreach (int predecessor in analysis.Predecessors[index])
                found |= Slot(il, predecessor, slot);
            return found;
        }
    }
}
