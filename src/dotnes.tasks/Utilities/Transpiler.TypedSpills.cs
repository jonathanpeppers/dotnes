using System.Reflection.Metadata;

namespace dotnes;

partial class Transpiler
{
    HashSet<int> GetStableClosureArguments(ILInstruction[] instructions, string method)
    {
        if (!_closureMethodArgIndex.TryGetValue(method, out int argument))
            return [];
        return new HashSet<int>(instructions.Select((instruction, index) => (instruction, index))
            .Where(pair => IL2NESWriter.NumericArgIndex(pair.instruction) == argument)
            .Select(pair => pair.index));
    }

    internal ILInstruction[] RewriteTypedExpressionValues(ILInstruction[] instructions, ILValueAnalysis analysis,
        ISet<int> selected, IReadOnlyList<PrimitiveTypeCode?> types, string method,
        ISet<int>? arrayProducers = null)
    {
        if (selected.Count == 0)
            return instructions;
        if (!NumericTypes.TryGetValue(method, out var signature))
            throw new InvalidOperationException($"Cannot allocate typed spills without the signature for '{method}'.");
        if (types.Count != instructions.Length)
            throw new ArgumentException("Producer types must correspond to the input IL instructions.", nameof(types));
        var stableAddresses = GetStableClosureArguments(instructions, method);
        if (selected.Any(i => i < 0 || i >= types.Count
            || types[i] == PrimitiveTypeCode.Void
            || (types[i] is null && arrayProducers?.Contains(i) != true
                && !ILExpressionSpiller.CanRematerialize(instructions[i]) && !stableAddresses.Contains(i))))
            throw new InvalidOperationException($"Cannot spill an untyped expression value in '{method}'.");

        foreach (int producer in selected)
            NumericStorage.RequireNarrowType(types[producer], method);
        var words = new HashSet<int>(selected.Where(i => NumericStorage.IsWord(types[i])));
        var signedWords = new HashSet<int>(words.Where(i => NumericStorage.IsSigned(types[i])));
        var spillLocals = new Dictionary<int, int>();
        var rewritten = ILExpressionSpiller.Rewrite(instructions, analysis, selected, words,
            spillLocals, signature.Locals.Length, stableAddressProducers: stableAddresses,
            signedWordProducers: signedWords);
        if (spillLocals.Count == 0)
            return rewritten;

        var locals = signature.Locals.ToBuilder();
        foreach (var pair in spillLocals)
        {
            while (locals.Count <= pair.Value)
                locals.Add(null);
            locals[pair.Value] = types[pair.Key];
        }
        NumericTypes[method] = signature with { Locals = locals.ToImmutable() };
        return rewritten;
    }
}
