using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

partial class Transpiler
{
    static void ValidateStaticArrayInitialization(ILInstruction[] main,
        IReadOnlyDictionary<string, ILInstruction[]> methods, ReflectionCache reflection,
        IEnumerable<string> aliasNames, IReadOnlyDictionary<ILInstruction[], HashSet<int>> provenStores)
    {
        var aliases = new HashSet<string>(aliasNames, StringComparer.Ordinal);
        if (aliases.Count == 0)
            return;

        var bodies = methods.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        bodies.Add("main", main);
        var analyses = bodies.ToDictionary(pair => pair.Key, pair => new ILValueAnalysis(pair.Value, reflection));
        var initializes = bodies.ToDictionary(pair => pair.Key, _ => new HashSet<string>());
        var requires = bodies.ToDictionary(pair => pair.Key, _ => new HashSet<string>());
        var incoming = new Dictionary<string, HashSet<string>[]>();

        HashSet<string>[] Analyze(string name)
        {
            var il = bodies[name];
            var analysis = analyses[name];
            var before = il.Select(_ => new HashSet<string>(aliases)).ToArray();
            var after = il.Select(_ => new HashSet<string>(aliases)).ToArray();
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < il.Length; i++)
                {
                    var predecessors = analysis.Predecessors[i];
                    var state = new HashSet<string>();
                    if (i > 0 && predecessors.Count > 0)
                    {
                        state.UnionWith(after[predecessors[0]]);
                        foreach (int predecessor in predecessors.Skip(1))
                            state.IntersectWith(after[predecessor]);
                    }
                    before[i] = new HashSet<string>(state);
                    if (i > 0 && predecessors.Count == 0)
                        continue;
                    if (il[i].OpCode == ILOpCode.Stsfld && il[i].String is string field &&
                        aliases.Contains(field) && provenStores.TryGetValue(il, out var stores) && stores.Contains(il[i].Offset))
                        state.Add(field);
                    if (il[i].OpCode == ILOpCode.Call && il[i].String is string callee && initializes.TryGetValue(callee, out var effects))
                        state.UnionWith(effects);
                    if (!after[i].SetEquals(state))
                    {
                        after[i] = state;
                        changed = true;
                    }
                }
            } while (changed);
            return before;
        }

        // Summaries describe only fixed-alias bindings guaranteed on every
        // returning path. No runtime pointer or reference state is introduced.
        bool summariesChanged;
        do
        {
            summariesChanged = false;
            foreach (var pair in bodies)
            {
                var before = incoming[pair.Key] = Analyze(pair.Key);
                var returns = Enumerable.Range(0, pair.Value.Length).Where(i =>
                    pair.Value[i].OpCode == ILOpCode.Ret &&
                    (i == 0 || analyses[pair.Key].Predecessors[i].Count > 0)).ToArray();
                var effects = returns.Length == 0 ? new HashSet<string>() : new HashSet<string>(before[returns[0]]);
                foreach (int ret in returns.Skip(1))
                    effects.IntersectWith(before[ret]);
                if (!initializes[pair.Key].SetEquals(effects))
                {
                    initializes[pair.Key] = effects;
                    summariesChanged = true;
                }
            }
        } while (summariesChanged);

        do
        {
            summariesChanged = false;
            foreach (var pair in bodies)
            {
                var il = pair.Value;
                for (int i = 0; i < il.Length; i++)
                {
                    if (i > 0 && analyses[pair.Key].Predecessors[i].Count == 0)
                        continue;
                    var before = incoming[pair.Key][i];
                    if (il[i].OpCode == ILOpCode.Ldsfld && il[i].String is string field &&
                        aliases.Contains(field) && !before.Contains(field))
                        summariesChanged |= requires[pair.Key].Add(field);
                    if (il[i].OpCode == ILOpCode.Call && il[i].String is string callee && requires.TryGetValue(callee, out var inputs))
                        foreach (string input in inputs.ToArray())
                            if (!before.Contains(input))
                                summariesChanged |= requires[pair.Key].Add(input);
                }
            }
        } while (summariesChanged);

        if (requires["main"].Count > 0)
            throw new TranspileException(
                $"Static array aliases must be initialized on every path before use: {string.Join(", ", requires["main"].OrderBy(name => name))}.",
                "main");
    }
}
