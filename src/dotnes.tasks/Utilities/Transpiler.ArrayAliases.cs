using System.Reflection.Metadata;
using dotnes.ObjectModel;
using Local = dotnes.LocalVariableManager.Local;

namespace dotnes;

partial class Transpiler
{
    static (Dictionary<string, Local> Aliases, Dictionary<ILInstruction[], Dictionary<int, Local>> Allocations,
        Dictionary<ILInstruction[], HashSet<int>> ProvenStores)
        PreAllocateArrayAliases(IEnumerable<ILInstruction[]> methods, ReflectionCache reflection, ref int staticBytes,
            IReadOnlyDictionary<string, (ushort Address, int ArraySize)> staticArrays)
    {
        var bodies = methods.ToArray();
        var storage = new ArrayStorageAnalysis(bodies, reflection);
        var bindings = new Dictionary<string, (ILInstruction[] Method, int Producer)>(StringComparer.Ordinal);
        var shared = new HashSet<(ILInstruction[] Method, int Producer)>();
        var provenStores = new Dictionary<ILInstruction[], HashSet<int>>();
        var existingAllocations = new Dictionary<(ILInstruction[] Method, int Producer), Local>();
        foreach (var il in bodies)
        {
            var analysis = new ILValueAnalysis(il, reflection);
            for (int i = 0; i < il.Length; i++)
            {
                if (il[i].OpCode != ILOpCode.Stsfld || il[i].String is not string field ||
                    analysis.Inputs[i].Length != 1)
                    continue;
                var origins = new HashSet<(ILInstruction[] Method, int Producer)>();
                var kind = storage.GetInputStorage(il, i, 0, origins);
                if ((kind & ArrayStorage.Ram) != 0 && kind != ArrayStorage.Ram)
                    throw new TranspileException("A static array alias cannot change identity across unsupported control flow.");
                if (kind != ArrayStorage.Ram)
                    continue;
                if (origins.Count != 1)
                    throw new TranspileException("Reassigning an array alias to a different array is not supported.");
                var origin = origins.Single();
                if (bindings.TryGetValue(field, out var existing) && existing != origin)
                    throw new TranspileException("Reassigning an array alias to a different array is not supported.");
                bindings[field] = origin;
                if (origin.Method == il && origin.Producer == i - 1 && staticArrays.TryGetValue(field, out var allocated))
                    existingAllocations[origin] = new Local(allocated.ArraySize, allocated.Address, ArraySize: allocated.ArraySize);
                if (!provenStores.TryGetValue(il, out var stores))
                    provenStores[il] = stores = [];
                stores.Add(il[i].Offset);
                if (origin.Method != il || origin.Producer != i - 1)
                    shared.Add(origin);
            }
        }

        var aliases = new Dictionary<string, Local>(StringComparer.Ordinal);
        var allocations = new Dictionary<ILInstruction[], Dictionary<int, Local>>();
        foreach (var pair in bindings)
        {
            var origin = pair.Value;
            if (!shared.Contains(origin))
                continue;
            if (!allocations.TryGetValue(origin.Method, out var methodAllocations))
                allocations[origin.Method] = methodAllocations = [];
            int offset = origin.Method[origin.Producer].Offset;
            if (!methodAllocations.TryGetValue(offset, out var array))
            {
                int? count = origin.Producer > 0 ? origin.Method[origin.Producer - 1].GetLdcValue() : null;
                if (count is not > 0 || origin.Method[origin.Producer].String != "Byte")
                    throw new TranspileException("Static array aliases require fixed-size byte-array allocations.");
                if (!existingAllocations.TryGetValue(origin, out array))
                {
                    if (staticBytes + count.Value > NESConstants.MaxLocalBytes)
                        throw new TranspileException("Fixed static array aliases exceed the available NES RAM.");
                    array = new Local(count.Value, NESConstants.LocalStackBase + staticBytes, ArraySize: count.Value);
                    staticBytes += count.Value;
                }
                methodAllocations[offset] = array;
            }
            aliases[pair.Key] = array;
        }
        return (aliases, allocations, provenStores);
    }
}
