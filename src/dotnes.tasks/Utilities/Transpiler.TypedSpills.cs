using System.Reflection.Metadata;

namespace dotnes;

partial class Transpiler
{
    ILInstruction[] RewriteTypedExpressionValues(ILInstruction[] instructions, ILValueAnalysis analysis,
        ISet<int> selected, IReadOnlyList<PrimitiveTypeCode?> types, string method)
    {
        if (!NumericTypes.TryGetValue(method, out var signature))
            throw new InvalidOperationException($"Cannot allocate typed spills without the signature for '{method}'.");
        if (types.Count != instructions.Length)
            throw new ArgumentException("Producer types must correspond to the input IL instructions.", nameof(types));
        if (selected.Any(i => i < 0 || i >= types.Count
            || (types[i] is null or PrimitiveTypeCode.Void && !ILExpressionSpiller.CanRematerialize(instructions[i]))))
            throw new InvalidOperationException($"Cannot spill an untyped expression value in '{method}'.");

        var words = new HashSet<int>(selected.Where(i => types[i] is PrimitiveTypeCode.UInt16
            or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32));
        var spillLocals = new Dictionary<int, int>();
        var rewritten = ILExpressionSpiller.Rewrite(instructions, analysis, selected, words,
            spillLocals, signature.Locals.Length);
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
