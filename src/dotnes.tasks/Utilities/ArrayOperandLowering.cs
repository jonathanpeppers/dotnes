using System.Reflection.Metadata;

namespace dotnes;

/// <summary>
/// Snapshots array operands at their IL evaluation points. The byte backend can
/// then store a computed value without reconstructing its expression.
/// </summary>
static class ArrayOperandLowering
{
    internal static bool IndexNeedsPreservation(ILInstruction[] instructions, int elementAddress)
    {
        for (int i = elementAddress + 1; i < instructions.Length; i++)
        {
            if (instructions[i].OpCode == ILOpCode.Stind_i1)
                return false;
            if (instructions[i].OpCode is ILOpCode.Call or ILOpCode.Ldelem_u1 or ILOpCode.Ldelema)
                return true;
        }
        return false;
    }

    public static ILInstruction[] Rewrite(ILInstruction[] instructions, ReflectionCache reflection,
        IReadOnlyDictionary<string, bool[]> arrayParameters, bool[]? methodArrayParameters = null)
    {
        var analysis = new ILValueAnalysis(instructions, reflection);
        var selected = new HashSet<int>();

        bool Simple(int producer) => producer >= 0 &&
            (instructions[producer].GetLdcValue() is not null ||
             instructions[producer].GetLdlocIndex() is not null ||
             instructions[producer].OpCode == ILOpCode.Ldsfld);

        int Unwrap(int producer)
        {
            while (producer >= 0 && instructions[producer].OpCode is ILOpCode.Conv_u1 or ILOpCode.Conv_u2 or ILOpCode.Conv_i1)
                producer = analysis.Inputs[producer][0];
            return producer;
        }

        bool ConstantFirstIndex(int producer)
        {
            producer = Unwrap(producer);
            return producer >= 0 && instructions[producer].OpCode == ILOpCode.Add &&
                analysis.Inputs[producer][0] >= 0 &&
                instructions[analysis.Inputs[producer][0]].GetLdcValue() is not null;
        }

        bool IsRomArray(int producer)
        {
            if (producer >= 0 && instructions[producer].OpCode == ILOpCode.Ldtoken)
                return true;
            if (producer < 0 || instructions[producer].GetLdlocIndex() is not int local)
                return false;
            return Enumerable.Range(1, instructions.Length - 1).Any(i =>
                instructions[i].GetStlocIndex() == local && instructions[i - 1].OpCode == ILOpCode.Ldtoken);
        }

        bool HasRuntimeCall(int producer)
        {
            if (producer < 0) return false;
            if (instructions[producer].OpCode == ILOpCode.Call &&
                analysis.Inputs[producer].Any(p => p < 0 || instructions[p].GetLdcValue() is null))
                return true;
            return analysis.Inputs[producer].Any(HasRuntimeCall);
        }

        bool SameValue(int left, int right)
        {
            if (left < 0 || right < 0) return false;
            if (left == right) return true;
            var a = instructions[left];
            var b = instructions[right];
            if (a.GetLdcValue() is int constant) return b.GetLdcValue() == constant;
            if (a.GetLdlocIndex() is int local) return b.GetLdlocIndex() == local;
            if (a.GetLdargIndex() is int arg) return b.GetLdargIndex() == arg;
            return false;
        }

        bool HasDifferentReadIndex(int producer, int targetIndex)
        {
            if (producer < 0) return false;
            if (instructions[producer].OpCode == ILOpCode.Ldelem_u1)
                return !SameValue(analysis.Inputs[producer][1], targetIndex);
            return analysis.Inputs[producer].Any(p => HasDifferentReadIndex(p, targetIndex));
        }

        bool HasStaticField(int producer) => producer >= 0 &&
            (instructions[producer].OpCode == ILOpCode.Ldsfld || analysis.Inputs[producer].Any(HasStaticField));

        bool LegacyConstantValue(int producer)
        {
            producer = Unwrap(producer);
            if (Simple(producer)) return true;
            if (producer < 0) return false;
            var inputs = analysis.Inputs[producer];
            if (inputs.Length != 2 || inputs[0] < 0 || inputs[1] < 0 ||
                instructions[inputs[1]].GetLdcValue() is not int immediate || immediate == 0)
                return false;
            return instructions[producer].OpCode switch
            {
                ILOpCode.Shr or ILOpCode.Shr_un => instructions[inputs[0]].GetLdlocIndex() is not null,
                ILOpCode.Add or ILOpCode.Sub or ILOpCode.And or ILOpCode.Or => LegacyConstantValue(inputs[0]),
                _ => false,
            };
        }

        void SelectInputs(int consumer)
        {
            foreach (int producer in analysis.Inputs[consumer])
            {
                if (producer < 0 || analysis.Escapes[producer])
                    throw new ObjectModel.TranspileException("Array operands crossing unsupported control flow cannot be materialized.");
                selected.Add(producer);
                SelectArgumentOperands(producer);
            }
        }

        void SelectArgumentOperands(int producer)
        {
            if (producer < 0) return;
            var inputs = analysis.Inputs[producer];
            if (inputs.Any(p => p >= 0 && instructions[p].GetLdargIndex() is not null) &&
                instructions[producer].OpCode is ILOpCode.Add or ILOpCode.Sub or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor)
                foreach (int input in inputs)
                    selected.Add(input);
            foreach (int input in inputs)
                SelectArgumentOperands(input);
        }

        IEnumerable<int> ValueLeaves(int producer)
        {
            if (producer < 0) yield break;
            if (analysis.Inputs[producer].Length == 0 ||
                instructions[producer].OpCode is ILOpCode.Call or ILOpCode.Ldelem_u1)
            {
                if (instructions[producer].GetLdcValue() is null)
                    yield return producer;
                yield break;
            }
            foreach (int input in analysis.Inputs[producer])
                foreach (int leaf in ValueLeaves(input))
                    yield return leaf;
        }

        for (int i = 0; i < instructions.Length; i++)
        {
            var instruction = instructions[i];
            var inputs = analysis.Inputs[i];
            if (instruction.OpCode == ILOpCode.Stelem_i1 && inputs.Length == 3)
            {
                if (inputs.Any(p => Unwrap(p) < 0))
                    throw new ObjectModel.TranspileException("Array operands crossing unsupported control flow cannot be materialized.");
                // Keep compact, already supported stores unchanged. Constant-index
                // stores and computed indexes otherwise lose their RHS provenance.
                bool complexIndex = ConstantFirstIndex(inputs[1]) || HasRuntimeCall(inputs[1]) ||
                    instructions[Unwrap(inputs[1])].OpCode == ILOpCode.Call;
                bool complexConstantStore = instructions[inputs[1]].GetLdcValue() is not null &&
                    !LegacyConstantValue(inputs[2]);
                var leaves = ValueLeaves(inputs[2]).Distinct().ToArray();
                bool helperValue = HasRuntimeCall(inputs[2]) ||
                    (leaves.Length > 1 && leaves.Any(p => instructions[p].OpCode == ILOpCode.Call));
                bool differingIndexes = instructions[Unwrap(inputs[2])].OpCode != ILOpCode.Ldelem_u1 &&
                    HasDifferentReadIndex(inputs[2], inputs[1]);
                if (complexIndex || complexConstantStore || helperValue || differingIndexes || HasStaticField(inputs[2]))
                {
                    SelectInputs(i);
                    SelectValueExpression(inputs[2]);
                }
            }
            else if (instruction.OpCode == ILOpCode.Ldelem_u1 &&
                inputs.Length == 2 && ConstantFirstIndex(inputs[1]))
                SelectInputs(i);
            else if (instruction.OpCode == ILOpCode.Ldelema && instruction.String == "Byte" &&
                inputs.Length == 2 && (!Simple(inputs[1]) || IndexNeedsPreservation(instructions, i)))
                SelectInputs(i);
            else if (instruction.OpCode == ILOpCode.Stind_i1 && inputs.Length == 2 &&
                inputs[0] >= 0 && instructions[inputs[0]].OpCode == ILOpCode.Ldelema &&
                IndexNeedsPreservation(instructions, inputs[0]))
            {
                selected.Add(inputs[1]);
                SelectArrayReads(inputs[1]);
            }
            else if (instruction.OpCode == ILOpCode.Call && instruction.String is string method &&
                arrayParameters.TryGetValue(method, out var parameters) && parameters.Contains(true))
            {
                var arrayInputs = inputs.Where((_, argument) => parameters[argument]).ToArray();
                if (arrayInputs.Any(IsRomArray))
                {
                    if (!arrayInputs.All(IsRomArray))
                        throw new ObjectModel.TranspileException("Mixing RAM and read-only ROM array arguments in one helper call is not supported.");
                }
                else
                    SelectInputs(i);
            }

            if (instruction.OpCode is ILOpCode.Ldelem_u1 or ILOpCode.Stelem_i1 or ILOpCode.Ldelema &&
                inputs.Length >= 2 && inputs[0] >= 0 &&
                instructions[inputs[0]].GetLdargIndex() is int arg &&
                methodArrayParameters is not null && arg < methodArrayParameters.Length && methodArrayParameters[arg])
                SelectInputs(i);
        }

        // A selected operand must be reloaded with every operand above it.
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < instructions.Length; i++)
            {
                bool reload = false;
                foreach (int producer in analysis.Inputs[i])
                {
                    reload |= selected.Contains(producer);
                    if (reload && producer >= 0 && !selected.Contains(producer))
                    {
                        selected.Add(producer);
                        changed = true;
                    }
                }
            }
        } while (changed);

        return ILExpressionSpiller.Rewrite(instructions, analysis, selected);

        void SelectValueExpression(int producer)
        {
            SelectInputs(producer);
            foreach (int input in analysis.Inputs[producer])
                SelectValueExpression(input);
        }

        void SelectArrayReads(int producer)
        {
            if (producer < 0) return;
            if (instructions[producer].OpCode == ILOpCode.Ldelem_u1)
            {
                selected.Add(producer);
                SelectInputs(producer);
            }
            else if (instructions[producer].OpCode is ILOpCode.Ldind_u1 or ILOpCode.Call)
                selected.Add(producer);
            foreach (int input in analysis.Inputs[producer])
                SelectArrayReads(input);
        }
    }
}
