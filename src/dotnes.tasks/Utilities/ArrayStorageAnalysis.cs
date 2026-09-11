using System.Reflection.Metadata;

namespace dotnes;

[Flags]
enum ArrayStorage
{
    Unknown = 1,
    Ram = 2,
    Rom = 4,
}

/// <summary>
/// Resolves array storage through local aliases and shared static-field bindings.
/// It does not infer mutability or introduce a general runtime array-reference ABI.
/// </summary>
sealed class ArrayStorageAnalysis
{
    readonly Dictionary<ILInstruction[], ILValueAnalysis> methods;
    readonly Dictionary<string, List<(ILInstruction[] Method, int Producer)>> fields = new(StringComparer.Ordinal);

    public ArrayStorageAnalysis(IEnumerable<ILInstruction[]> instructions, ReflectionCache reflection)
    {
        methods = instructions.ToDictionary(il => il, il => new ILValueAnalysis(il, reflection));
        foreach (var pair in methods)
        {
            var il = pair.Key;
            var analysis = pair.Value;
            for (int i = 0; i < il.Length; i++)
            {
                if (il[i].OpCode != ILOpCode.Stsfld || il[i].String is not string name)
                    continue;
                if (!fields.TryGetValue(name, out var sources))
                    fields[name] = sources = [];
                sources.Add((il, analysis.Inputs[i].Length == 1 ? analysis.Inputs[i][0] : -1));
            }
        }
    }

    public ArrayStorage GetStorage(ILInstruction[] instructions, int producer,
        ISet<(ILInstruction[] Method, int Producer)>? allocations = null)
    {
        var visited = new HashSet<(ILInstruction[], int)>();
        ArrayStorage result = Resolve(instructions, producer);
        return result == 0 ? ArrayStorage.Unknown : result;

        ArrayStorage Resolve(ILInstruction[] il, int p)
        {
            if (p < 0)
                return ArrayStorage.Unknown;
            if (!visited.Add((il, p)))
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
            if (il[p].OpCode == ILOpCode.Ldsfld && il[p].String is string field)
                return fields.TryGetValue(field, out var sources)
                    ? sources.Aggregate((ArrayStorage)0, (storage, source) => storage | Resolve(source.Method, source.Producer))
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
                    found |= analysis.Inputs[i].Length == 1 ? Resolve(il, analysis.Inputs[i][0]) : ArrayStorage.Unknown;
                else
                    foreach (int predecessor in analysis.Predecessors[i])
                        pending.Push(predecessor);
            }
            return found;
        }

    }

    public ArrayStorage GetInputStorage(ILInstruction[] instructions, int consumer, int argument,
        ISet<(ILInstruction[] Method, int Producer)> allocations)
    {
        var analysis = methods[instructions];
        int producer = analysis.Inputs[consumer][argument];
        if (producer >= 0)
            return GetStorage(instructions, producer, allocations);
        var visited = new HashSet<(int Instruction, int Slot)>();
        ArrayStorage result = 0;
        foreach (int predecessor in analysis.Predecessors[consumer])
            result |= Resolve(predecessor, analysis.Outputs[predecessor].Length - analysis.Inputs[consumer].Length + argument);
        return result == 0 ? ArrayStorage.Unknown : result;

        ArrayStorage Resolve(int index, int slot)
        {
            if (slot < 0 || slot >= analysis.Outputs[index].Length)
                return ArrayStorage.Unknown;
            if (!visited.Add((index, slot)))
                return 0;
            int value = analysis.Outputs[index][slot];
            if (value >= 0)
                return GetStorage(instructions, value, allocations);
            // Unknown identities are inherited stack slots at a join. A dup's
            // extra slot refers to the preceding top slot, not a new allocation.
            if (instructions[index].OpCode == ILOpCode.Dup && slot == analysis.Outputs[index].Length - 1)
                slot--;
            ArrayStorage found = 0;
            foreach (int predecessor in analysis.Predecessors[index])
                found |= Resolve(predecessor, slot);
            return found;
        }
    }
}
