using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// Local frame allocation — detects word locals, estimates method stack frame sizes,
/// and computes non-overlapping frame offsets for the user method call graph.
/// </summary>
partial class Transpiler
{
    /// <summary>
    /// Pre-scan IL instructions for conv.u2 + stloc patterns to detect ushort locals.
    /// </summary>
    static HashSet<int> DetectWordLocals(ILInstruction[] instructions, ReflectionCache? reflectionCache = null)
    {
        var result = new HashSet<int>();
        for (int i = 0; i < instructions.Length - 1; i++)
        {
            bool isConvU2 = instructions[i].OpCode == ILOpCode.Conv_u2;
            bool is16BitCall = instructions[i].OpCode == ILOpCode.Call
                && instructions[i].String is not null
                && reflectionCache is not null
                && reflectionCache.TryReturns16Bit(instructions[i].String!);

            if (!isConvU2 && !is16BitCall)
                continue;

            var next = instructions[i + 1];
            int? idx = next.OpCode switch
            {
                ILOpCode.Stloc_0 => 0,
                ILOpCode.Stloc_1 => 1,
                ILOpCode.Stloc_2 => 2,
                ILOpCode.Stloc_3 => 3,
                ILOpCode.Stloc_s => next.Integer,
                _ => null
            };
            if (idx.HasValue)
                result.Add(idx.Value);
        }
        return result;
    }

    /// <summary>
    /// Pre-scan IL to estimate the number of local variable bytes a method will allocate.
    /// Simulates the transpiler's allocation rules: stloc targets (byte/word), newarr byte[]/struct[]
    /// arrays (whether stored to locals or consumed on eval stack), struct locals accessed via
    /// ldfld/stfld, and pad_poll temporaries.
    /// </summary>
    static int EstimateMethodLocalBytes(
        ILInstruction[] instructions,
        HashSet<int> wordLocals,
        Dictionary<string, List<(string Name, int Size)>>? structLayouts)
    {
        int totalBytes = 0;

        // Track which stloc targets are newarr destinations (to avoid double-counting)
        var newarrStlocTargets = new HashSet<int>();

        // Pass 1: Count newarr allocations (both stloc and eval-stack patterns).
        // Every newarr consumes LocalCount bytes regardless of whether it's stored to a local.
        for (int i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].OpCode != ILOpCode.Newarr)
                continue;

            // Look back for ldc to get array size
            int? count = i > 0 ? GetLdcValue(instructions[i - 1]) : null;
            if (!count.HasValue || count.Value <= 0)
                continue;

            string? elementType = instructions[i].String;
            if (structLayouts is not null && elementType is not null
                && structLayouts.TryGetValue(elementType, out var fields))
            {
                int structSize = 0;
                foreach (var f in fields) structSize += f.Size;
                totalBytes += count.Value * structSize;
            }
            else
            {
                totalBytes += count.Value; // byte/primitive array
            }

            // Check if this newarr flows into a stloc (skip dup in between)
            for (int j = i + 1; j < instructions.Length && j <= i + 3; j++)
            {
                if (instructions[j].OpCode == ILOpCode.Dup)
                    continue;
                int? stlocIdx = GetStlocIndex(instructions[j]);
                if (stlocIdx.HasValue)
                    newarrStlocTargets.Add(stlocIdx.Value);
                break;
            }
        }

        // Pass 2: Count scalar stloc targets (excluding newarr destinations)
        var countedLocals = new HashSet<int>();
        for (int i = 0; i < instructions.Length; i++)
        {
            int? stlocIdx = GetStlocIndex(instructions[i]);
            if (stlocIdx.HasValue
                && !countedLocals.Contains(stlocIdx.Value)
                && !newarrStlocTargets.Contains(stlocIdx.Value))
            {
                countedLocals.Add(stlocIdx.Value);
                totalBytes += wordLocals.Contains(stlocIdx.Value) ? 2 : 1;
            }
        }

        // Pass 3: Count struct locals accessed via ldloca → stfld/ldfld
        // (These are allocated on first field access, not via stloc)
        if (structLayouts is not null)
        {
            var structLocalsCounted = new HashSet<int>();
            for (int i = 0; i < instructions.Length; i++)
            {
                if (instructions[i].OpCode is ILOpCode.Ldloca_s or ILOpCode.Ldloca
                    && instructions[i].Integer is int ldlocaIdx
                    && !countedLocals.Contains(ldlocaIdx)
                    && !newarrStlocTargets.Contains(ldlocaIdx)
                    && !structLocalsCounted.Contains(ldlocaIdx))
                {
                    for (int j = i + 1; j < instructions.Length && j <= i + 3; j++)
                    {
                        if (instructions[j].OpCode is ILOpCode.Stfld or ILOpCode.Ldfld
                            && instructions[j].String is string fieldName)
                        {
                            int structSize = FindStructSizeByField(fieldName, structLayouts);
                            if (structSize > 0)
                            {
                                totalBytes += structSize;
                                structLocalsCounted.Add(ldlocaIdx);
                            }
                            break;
                        }
                    }
                }
            }
        }

        // Pass 4: pad_poll allocates 1 temp byte on first call
        for (int i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].OpCode == ILOpCode.Call && instructions[i].String == "pad_poll")
            {
                totalBytes += 1;
                break; // only 1 temp regardless of how many calls
            }
        }

        return totalBytes;
    }

    static int? GetStlocIndex(ILInstruction inst) => inst.OpCode switch
    {
        ILOpCode.Stloc_0 => 0,
        ILOpCode.Stloc_1 => 1,
        ILOpCode.Stloc_2 => 2,
        ILOpCode.Stloc_3 => 3,
        ILOpCode.Stloc_s or ILOpCode.Stloc => inst.Integer,
        _ => null
    };

    static int? GetLdcValue(ILInstruction inst) => inst.OpCode switch
    {
        ILOpCode.Ldc_i4_0 => 0,
        ILOpCode.Ldc_i4_1 => 1,
        ILOpCode.Ldc_i4_2 => 2,
        ILOpCode.Ldc_i4_3 => 3,
        ILOpCode.Ldc_i4_4 => 4,
        ILOpCode.Ldc_i4_5 => 5,
        ILOpCode.Ldc_i4_6 => 6,
        ILOpCode.Ldc_i4_7 => 7,
        ILOpCode.Ldc_i4_8 => 8,
        ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 => inst.Integer,
        ILOpCode.Ldc_i4_m1 => -1,
        _ => null
    };

    /// <summary>
    /// Compute frame offsets for each user method based on the call graph.
    /// Methods called by other user methods get offsets that avoid overlapping
    /// with their callers' locals. Methods not in any call chain use the base offset.
    /// </summary>
    static Dictionary<string, int> ComputeMethodFrameOffsets(
        Dictionary<string, ILInstruction[]> userMethods,
        ReflectionCache? reflectionCache,
        int baseOffset,
        Dictionary<string, List<(string Name, int Size)>>? structLayouts)
    {
        // Step 1: Estimate local byte counts for each method
        var localByteCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kvp in userMethods)
        {
            var wordLocals = DetectWordLocals(kvp.Value, reflectionCache);
            localByteCounts[kvp.Key] = EstimateMethodLocalBytes(kvp.Value, wordLocals, structLayouts);
        }

        // Step 2: Build call graph — which user methods does each method call?
        var callGraph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var kvp in userMethods)
        {
            var callees = new HashSet<string>(StringComparer.Ordinal);
            foreach (var inst in kvp.Value)
            {
                if (inst.OpCode == ILOpCode.Call && inst.String is not null
                    && userMethods.ContainsKey(inst.String))
                {
                    callees.Add(inst.String);
                }
            }
            callGraph[kvp.Key] = callees;
        }

        // Step 3: Fixed-point iteration to propagate offsets along call edges
        // For edge caller → callee: callee.offset >= caller.offset + caller.localBytes
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var method in userMethods.Keys)
            offsets[method] = baseOffset;

        bool changed = true;
        int maxIterations = userMethods.Count + 1; // sufficient for acyclic graphs
        int iteration = 0;
        while (changed && iteration < maxIterations)
        {
            iteration++;
            changed = false;
            foreach (var kvp in callGraph)
            {
                int callerEnd = offsets[kvp.Key] + localByteCounts[kvp.Key];
                foreach (var callee in kvp.Value)
                {
                    if (callerEnd > offsets[callee])
                    {
                        offsets[callee] = callerEnd;
                        changed = true;
                    }
                }
            }
        }

        // If we exhausted iterations and still haven't converged, the call graph
        // has cycles (mutual/self recursion). Fail fast rather than silently
        // returning incorrect overlapping offsets.
        if (changed)
            throw new InvalidOperationException(
                "Recursive call cycle detected among user methods. " +
                "NES programs do not support recursion due to limited stack space.");

        return offsets;
    }
}
