using System.Reflection;
using System.Reflection.Metadata;

namespace dotnes;

partial class Transpiler
{
    const int MaxByteExpressionInstructions = 16;
    readonly Dictionary<string, Dictionary<int, (ILInstruction Instruction, int Expression)>> _inlinedByteExpressionValues
        = new(StringComparer.Ordinal);

    ILInstruction[] InlineByteExpressions(ILInstruction[] instructions, ReflectionCache reflection, string method)
    {
        _inlinedByteExpressionValues.Remove(method);
        if (instructions.Length == 0 || _ambiguousByteHelperNames
            || !NumericTypes.TryGetValue(method, out var callerTypes))
            return instructions;
        var analysis = new ILValueAnalysis(instructions, reflection);
        var result = new List<ILInstruction>();
        int syntheticOffset = Math.Min(-1, instructions.Min(i => i.Offset) - 1);
        for (int index = 0; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.OpCode != ILOpCode.Call || call.String is not string name
                || _methodRegions.ContainsKey(name)
                || !UserMethods.TryGetValue(name, out var body)
                || !_byteHelperDefinitions.TryGetValue(name, out var definition)
                || (definition.Attributes & MethodAttributes.Static) == 0
                || definition.ImplAttributes != MethodImplAttributes.IL
                || definition.GetCustomAttributes().Any(h =>
                    GetAttributeTypeName(_reader.GetCustomAttribute(h)) != "CompilerGeneratedAttribute")
                || !NumericTypes.TryGetValue(name, out var types)
                || types.ReturnType != PrimitiveTypeCode.Byte
                || types.Parameters.Length < 2
                || types.Parameters.Any(t => t != PrimitiveTypeCode.Byte)
                || body.Length > MaxByteExpressionInstructions
                || body.Length == 0 || body[body.Length - 1].OpCode != ILOpCode.Ret
                || IL2NESWriter.NumericArgIndex(body.First(i => i.OpCode != ILOpCode.Nop)) != 0
                || body.Select((instruction, i) => (instruction, i)).Any(pair =>
                    pair.instruction.OpCode is ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un
                    && (pair.i == 0 || body[pair.i - 1].GetLdcValue() is not (>= 0 and <= 31)))
                || !body.Where(i => IL2NESWriter.NumericArgIndex(i).HasValue)
                    .Select(i => IL2NESWriter.NumericArgIndex(i)!.Value)
                    .SequenceEqual(Enumerable.Range(0, types.Parameters.Length))
                || body.Take(body.Length - 1).Any(i =>
                    IL2NESWriter.NumericArgIndex(i) == null && i.GetLdcValue() == null
                    && i.OpCode is not (ILOpCode.Nop or ILOpCode.Add or ILOpCode.Sub
                        or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or ILOpCode.Shl
                        or ILOpCode.Shr or ILOpCode.Shr_un or ILOpCode.Conv_u1)))
            {
                result.Add(call);
                continue;
            }

            int count = types.Parameters.Length;
            int first = index - count;
            bool ByteLoad(ILInstruction instruction) =>
                instruction.GetLdcValue() is >= 0 and <= byte.MaxValue
                || instruction.GetLdlocIndex() is int local && local < callerTypes.Locals.Length
                    && callerTypes.Locals[local] == PrimitiveTypeCode.Byte
                || IL2NESWriter.NumericArgIndex(instruction) is int argument
                    && argument < callerTypes.Parameters.Length
                    && callerTypes.Parameters[argument] == PrimitiveTypeCode.Byte;
            if (first < 0 || !analysis.Inputs[index].SequenceEqual(Enumerable.Range(first, count))
                || analysis.Predecessors[first].Any(p => analysis.Outputs[p].Length != 0)
                || ILBranchTargets.HasEntryAfter(instructions, first, index)
                || Enumerable.Range(first, count).Any(i => !ByteLoad(instructions[i])
                    || !analysis.Consumers[i].SequenceEqual([index])))
            {
                result.Add(call);
                continue;
            }

            // Each argument is read once, in its original order. The callee has
            // no stores, calls, branches, address-taking, or observable frame use.
            result.RemoveRange(result.Count - count, count);
            if (Enumerable.Range(first, count).All(i => instructions[i].GetLdcValue().HasValue))
            {
                var values = new Stack<int>();
                foreach (var instruction in body)
                {
                    if (IL2NESWriter.NumericArgIndex(instruction) is int argument)
                        values.Push(instructions[first + argument].GetLdcValue()!.Value);
                    else if (instruction.GetLdcValue() is int constant)
                        values.Push(constant);
                    else if (instruction.OpCode == ILOpCode.Conv_u1)
                        values.Push(unchecked((byte)values.Pop()));
                    else if (instruction.OpCode is not (ILOpCode.Nop or ILOpCode.Ret))
                    {
                        int right = values.Pop(), left = values.Pop();
                        values.Push(instruction.OpCode switch
                        {
                            ILOpCode.Add => unchecked(left + right),
                            ILOpCode.Sub => unchecked(left - right),
                            ILOpCode.And => left & right,
                            ILOpCode.Or => left | right,
                            ILOpCode.Xor => left ^ right,
                            ILOpCode.Shl => left << right,
                            ILOpCode.Shr => left >> right,
                            ILOpCode.Shr_un => (int)((uint)left >> right),
                            _ => throw new InvalidOperationException("Unexpected byte-expression instruction."),
                        });
                    }
                }
                result.Add(new ILInstruction(ILOpCode.Ldc_i4, instructions[first].Offset,
                    Integer: unchecked((byte)values.Pop())));
                continue;
            }
            foreach (var instruction in body.Take(body.Length - 1))
            {
                if (IL2NESWriter.NumericArgIndex(instruction) is int argument)
                    AddExpressionInstruction(instructions[first + argument]);
                else
                    AddExpressionInstruction(instruction with { Offset = syntheticOffset-- });
            }
            AddExpressionInstruction(new ILInstruction(ILOpCode.Conv_u1, call.Offset));

            void AddExpressionInstruction(ILInstruction instruction)
            {
                result.Add(instruction);
                if (!_inlinedByteExpressionValues.TryGetValue(method, out var values))
                    _inlinedByteExpressionValues.Add(method, values = []);
                values.Add(instruction.Offset, (instruction, call.Offset));
            }
        }
        return result.ToArray();
    }
}
