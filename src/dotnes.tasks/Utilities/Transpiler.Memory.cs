using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

partial class Transpiler
{
    ILInstruction[] MaterializeSharedMemoryAddresses(ILInstruction[] instructions,
        ReflectionCache reflection, string methodName)
    {
        if (!instructions.Any(i => i.OpCode == ILOpCode.Call
            && i.String is nameof(NESLib.peek) or nameof(NESLib.poke)))
            return instructions;

        var analysis = new ILValueAnalysis(instructions, reflection);
        var selected = new HashSet<int>();
        var words = new HashSet<int>();
        for (int i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].OpCode != ILOpCode.Call
                || instructions[i].String is not (nameof(NESLib.peek) or nameof(NESLib.poke)))
                continue;
            int address = analysis.Inputs[i][0];
            if (address < 0 || !analysis.Consumers[address].Any(c => instructions[c].OpCode == ILOpCode.Dup))
                continue;
            selected.Add(address);
            words.Add(address);
        }
        if (selected.Count == 0)
            return instructions;

        // If an address is spilled, later operands of the same consumer must
        // also be materialized so reloading it cannot reverse evaluation order.
        var pending = new Queue<int>(selected);
        while (pending.Count > 0)
        {
            int producer = pending.Dequeue();
            if (analysis.Escapes[producer])
                throw new TranspileException("A shared memory address cannot cross an unknown evaluation-stack boundary.", methodName);
            foreach (int consumer in analysis.Consumers[producer])
            {
                var inputs = analysis.Inputs[consumer];
                for (int j = Array.IndexOf(inputs, producer) + 1; j < inputs.Length; j++)
                {
                    int sibling = inputs[j];
                    if (sibling < 0)
                        throw new TranspileException("Unable to preserve a shared memory address with an unknown operand.", methodName);
                    if (selected.Add(sibling))
                        pending.Enqueue(sibling);
                }
            }
        }
        if (!NumericTypes.TryGetValue(methodName, out var signature))
            throw new TranspileException("Unable to resolve primitive types for shared memory operands.", methodName);
        var types = GetExpressionValueTypes(instructions, analysis, reflection, methodName);
        var spillLocals = new Dictionary<int, int>();
        var rewritten = ILExpressionSpiller.Rewrite(instructions, analysis, selected, words,
            spillLocals, signature.Locals.Length);
        var locals = signature.Locals.ToBuilder();
        foreach (var pair in spillLocals)
        {
            var type = words.Contains(pair.Key) ? PrimitiveTypeCode.UInt16 : types[pair.Key];
            if (type is null)
                throw new TranspileException("Unable to classify a shared memory operand's primitive type.", methodName);
            while (locals.Count <= pair.Value)
                locals.Add(null);
            locals[pair.Value] = type;
        }
        NumericTypes[methodName] = signature with { Locals = locals.ToImmutable() };
        return rewritten;
    }
}
