using System.Reflection.Metadata;

namespace dotnes;

/// <summary>
/// Snapshots array operands at their IL evaluation points. The byte backend can
/// then store a computed value without reconstructing its expression.
/// </summary>
static class ArrayOperandLowering
{
    internal const string UnsupportedSignatureMessage =
        "Fixed RAM array helpers support only primitive byte, sbyte, and bool scalar parameters and return values (or void); enum scalars and captured/by-reference helper contexts are not supported.";

    internal static bool IndexNeedsPreservation(ILInstruction[] instructions, int elementAddress)
    {
        for (int i = elementAddress + 1; i < instructions.Length; i++)
        {
            if (instructions[i].OpCode == ILOpCode.Stind_i1)
                return false;
            if (instructions[i].GetStlocIndex() is not null ||
                instructions[i].OpCode is ILOpCode.Call or ILOpCode.Ldelem_u1 or ILOpCode.Ldelema)
                return true;
        }
        return false;
    }

    public static ILInstruction[] Rewrite(ILInstruction[] instructions, ReflectionCache reflection,
        IReadOnlyDictionary<string, bool[]> arrayParameters, bool[]? methodArrayParameters = null,
        ISet<string>? unsupportedArraySignatures = null, int closureArgIndex = -1, ArrayStorageAnalysis? storage = null,
        Func<ILValueAnalysis, ISet<int>, ILInstruction[]>? materialize = null)
    {
        var analysis = new ILValueAnalysis(instructions, reflection);
        storage ??= new ArrayStorageAnalysis([instructions], reflection);
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

        // Retain only the compact forms the legacy emitter reconstructs exactly.
        // Other forms use producer snapshots rather than guessing their values.
        bool LegacyIndex(int producer)
        {
            producer = Unwrap(producer);
            if (Simple(producer)) return true;
            if (producer < 0 || instructions[producer].OpCode != ILOpCode.Add) return false;
            var inputs = analysis.Inputs[producer];
            return inputs[0] >= 0 && instructions[inputs[0]].GetLdlocIndex() is not null &&
                inputs[1] >= 0 && Simple(inputs[1]);
        }

        bool LegacyMaskedCallIndex(int producer)
        {
            producer = Unwrap(producer);
            while (instructions[producer].OpCode == ILOpCode.And &&
                instructions[analysis.Inputs[producer][1]].GetLdcValue() is not null)
                producer = Unwrap(analysis.Inputs[producer][0]);
            return instructions[producer].OpCode == ILOpCode.Call && analysis.Inputs[producer].Length == 0;
        }

        bool LegacyArrayIndex(int producer)
        {
            producer = Unwrap(producer);
            if (instructions[producer].OpCode == ILOpCode.Add &&
                instructions[analysis.Inputs[producer][1]].GetLdcValue() is not null)
                producer = analysis.Inputs[producer][0];
            if (instructions[producer].OpCode == ILOpCode.Mul &&
                instructions[analysis.Inputs[producer][1]].GetLdcValue() is int scale &&
                scale > 0 && (scale & (scale - 1)) == 0)
                producer = analysis.Inputs[producer][0];
            return instructions[producer].OpCode == ILOpCode.Ldelem_u1 &&
                analysis.Inputs[producer].All(p => instructions[p].GetLdlocIndex() is not null);
        }

        bool LegacyArrayIndexValue(int producer)
        {
            producer = Unwrap(producer);
            // The array-derived-index emitter supports local + immediate, then OR.
            foreach (var op in new[] { ILOpCode.Or, ILOpCode.Add })
                if (instructions[producer].OpCode == op &&
                    instructions[analysis.Inputs[producer][1]].GetLdcValue() is not null)
                    producer = Unwrap(analysis.Inputs[producer][0]);
            return Simple(producer);
        }

        bool IsRomArray(int producer) => (storage.GetStorage(instructions, producer) & ArrayStorage.Rom) != 0;

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

        bool LegacyDynamicValue(int producer, int targetArray, int targetIndex)
        {
            producer = Unwrap(producer);
            if (Simple(producer)) return true;
            var instruction = instructions[producer];
            var inputs = analysis.Inputs[producer];
            bool Local(int p) => instructions[Unwrap(p)].GetLdlocIndex() is not null;
            bool Read(int p) => instructions[Unwrap(p)].OpCode == ILOpCode.Ldelem_u1 &&
                Local(analysis.Inputs[Unwrap(p)][0]) && Local(analysis.Inputs[Unwrap(p)][1]) &&
                !IsRomArray(analysis.Inputs[Unwrap(p)][0]);

            if (instruction.OpCode == ILOpCode.Call)
                return inputs.All(p => instructions[p].GetLdcValue() is not null);
            if (instruction.OpCode == ILOpCode.Ldelem_u1)
                return Read(producer) && !(SameValue(inputs[0], targetArray) && SameValue(inputs[1], targetIndex));
            if (inputs.Length != 2) return false;
            if (instruction.OpCode is ILOpCode.Add or ILOpCode.Sub && inputs.All(Local))
                return true;
            if (instruction.OpCode == ILOpCode.Add && inputs.All(Read))
                return inputs.All(p => SameValue(analysis.Inputs[Unwrap(p)][1], targetIndex));
            if (instructions[inputs[1]].GetLdcValue() is not int immediate)
                return false;
            if (instruction.OpCode is ILOpCode.Shr or ILOpCode.Shr_un)
                return Local(inputs[0]) && immediate > 0;
            if (instruction.OpCode == ILOpCode.Mul)
                return (Local(inputs[0]) || instructions[Unwrap(inputs[0])].OpCode == ILOpCode.Call) &&
                    immediate > 0 && (immediate & (immediate - 1)) == 0 &&
                    LegacyDynamicValue(inputs[0], targetArray, targetIndex);
            bool sameElement = Read(inputs[0]) &&
                SameValue(analysis.Inputs[Unwrap(inputs[0])][0], targetArray) &&
                SameValue(analysis.Inputs[Unwrap(inputs[0])][1], targetIndex);
            return instruction.OpCode is ILOpCode.Add or ILOpCode.Sub or ILOpCode.And or ILOpCode.Or &&
                ValueLeaves(inputs[0]).Distinct().Count() == 1 &&
                ValueLeaves(inputs[0]).Where(p => instructions[p].OpCode == ILOpCode.Ldelem_u1)
                    .All(p => SameValue(analysis.Inputs[p][1], targetIndex)) &&
                (sameElement || LegacyDynamicValue(inputs[0], targetArray, targetIndex));
        }

        HashSet<int> OperandClosure(IEnumerable<int> producers)
        {
            var closure = new HashSet<int>();
            void Visit(int producer)
            {
                if (producer < 0)
                    throw new ObjectModel.TranspileException("Array operands crossing unsupported control flow cannot be materialized.");
                if (!closure.Add(producer)) return;
                foreach (int input in analysis.Inputs[producer])
                    Visit(input);
            }
            foreach (int producer in producers)
                Visit(producer);
            return closure;
        }

        bool HasIndependentEffect(int consumer, HashSet<int> closure, int start) =>
            Enumerable.Range(start, consumer - start).Any(p =>
                !closure.Contains(p) && (instructions[p].GetStlocIndex() is not null ||
                    instructions[p].OpCode is ILOpCode.Call or ILOpCode.Stsfld or ILOpCode.Stfld
                        or ILOpCode.Stelem_i1 or ILOpCode.Stind_i1));

        void SelectInputs(int consumer)
        {
            foreach (int producer in analysis.Inputs[consumer])
            {
                if (producer < 0 || analysis.Escapes[producer])
                    throw new ObjectModel.TranspileException("Array operands crossing unsupported control flow cannot be materialized.");
                if (instructions[producer].OpCode == ILOpCode.Ldelema && instructions[producer].String == "Byte")
                    continue;
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
            if (instruction.OpCode == ILOpCode.Stsfld && inputs.Length == 1 &&
                inputs[0] != i - 1 && storage.GetStorage(instructions, inputs[0]) == ArrayStorage.Ram)
                SelectInputs(i);
            if (instruction.OpCode == ILOpCode.Stelem_i1 && inputs.Length == 3)
            {
                var closure = OperandClosure(inputs);
                bool constantIndex = instructions[inputs[1]].GetLdcValue() is not null;
                bool legacyValue = constantIndex ? LegacyConstantValue(inputs[2]) : LegacyDynamicValue(inputs[2], inputs[0], inputs[1]);
                bool legacyIndex = LegacyIndex(inputs[1]) ||
                    (instructions[Unwrap(inputs[2])].GetLdcValue() is not null && LegacyMaskedCallIndex(inputs[1])) ||
                    (LegacyArrayIndex(inputs[1]) && LegacyArrayIndexValue(inputs[2]));
                // Reconstruction can erase postfix stores or other side effects
                // even when the yielded index/value is a simple local load.
                int firstScalar = OperandClosure(inputs.Skip(1)).Min();
                bool independentEffect = HasIndependentEffect(i, closure, firstScalar);
                if (!legacyIndex || !legacyValue || independentEffect ||
                    HasStaticField(inputs[1]) || HasStaticField(inputs[2]))
                {
                    SelectInputs(i);
                    SelectValueExpression(inputs[1]);
                    SelectValueExpression(inputs[2]);
                }
            }
            else if (instruction.OpCode == ILOpCode.Ldelem_u1 && inputs.Length == 2)
            {
                var closure = OperandClosure(inputs);
                if (!Simple(Unwrap(inputs[1])) || HasIndependentEffect(i, closure, OperandClosure([inputs[1]]).Min()) ||
                    (inputs[0] != i - 2 && instructions[inputs[0]].OpCode == ILOpCode.Newarr))
                {
                    SelectInputs(i);
                    SelectValueExpression(inputs[1]);
                    selected.Add(i);
                }
            }
            else if (instruction.OpCode == ILOpCode.Ldelema && instruction.String == "Byte" &&
                inputs.Length == 2)
            {
                var closure = OperandClosure(inputs);
                if (!Simple(inputs[1]) || IndexNeedsPreservation(instructions, i) ||
                    HasIndependentEffect(i, closure, OperandClosure([inputs[1]]).Min()))
                {
                    SelectInputs(i);
                    SelectValueExpression(inputs[1]);
                }
            }
            else if (instruction.OpCode == ILOpCode.Stind_i1 && inputs.Length == 2 &&
                inputs[0] >= 0 && instructions[inputs[0]].OpCode == ILOpCode.Ldelema &&
                instructions[inputs[0]].String == "Byte")
            {
                var closure = OperandClosure(inputs);
                int value = Unwrap(inputs[1]);
                var valueInputs = analysis.Inputs[value];
                bool simpleIncrement = instructions[value].OpCode is ILOpCode.Add or ILOpCode.Sub &&
                    valueInputs.Length == 2 && instructions[valueInputs[0]].OpCode == ILOpCode.Ldind_u1 &&
                    instructions[valueInputs[1]].GetLdcValue() is not null;
                if (!simpleIncrement || IndexNeedsPreservation(instructions, inputs[0]) ||
                    HasIndependentEffect(i, closure, inputs[0]))
                {
                    SelectInputs(inputs[0]);
                    SelectValueExpression(analysis.Inputs[inputs[0]][1]);
                    selected.Add(inputs[1]);
                    SelectValueExpression(inputs[1]);
                }
            }
            else if (instruction.OpCode == ILOpCode.Call && instruction.String is string method &&
                arrayParameters.TryGetValue(method, out var parameters) && parameters.Contains(true))
            {
                var arrayInputs = inputs.Where((_, argument) => parameters[argument]).ToArray();
                if (arrayInputs.Any(IsRomArray))
                {
                    if (!arrayInputs.All(p => storage.GetStorage(instructions, p) == ArrayStorage.Rom))
                        throw new ObjectModel.TranspileException("Mixing RAM and read-only ROM array arguments in one helper call is not supported.");
                }
                else
                {
                    if (unsupportedArraySignatures?.Contains(method) == true)
                        throw new ObjectModel.TranspileException(UnsupportedSignatureMessage, method);
                    SelectInputs(i);
                }
            }

            if (instruction.OpCode is ILOpCode.Ldelem_u1 or ILOpCode.Stelem_i1 or ILOpCode.Ldelema &&
                inputs.Length >= 2 && inputs[0] >= 0 &&
                instructions[inputs[0]].GetLdargIndex() is int arg &&
                methodArrayParameters is not null && arg < methodArrayParameters.Length && methodArrayParameters[arg])
                SelectInputs(i);
        }

        // A selected operand must be reloaded with every operand above it.
        var readValues = new HashSet<int>(selected.Where(p => instructions[p].OpCode == ILOpCode.Ldelem_u1));
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < instructions.Length; i++)
            {
                // A materialized read result can have an earlier live operand
                // beneath it. Preserve that operand before the array load too.
                if (analysis.Inputs[i].Any(readValues.Contains))
                {
                    int before = selected.Count;
                    SelectInputs(i);
                    foreach (int input in analysis.Inputs[i])
                        SelectValueExpression(input);
                    if (analysis.ProducesValue[i])
                    {
                        selected.Add(i);
                        changed |= readValues.Add(i);
                    }
                    changed |= selected.Count != before;
                }
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

        if (materialize is not null)
            return ArrayReferenceLowering.Rewrite(materialize(analysis, selected), reflection);

        // Only the compiler-managed hidden context is an immutable pointer.
        // Ordinary argument values must retain their original evaluation time.
        var contexts = new HashSet<int>(selected.Where(p =>
            closureArgIndex >= 0 && instructions[p].GetLdargIndex() == closureArgIndex));
        return ArrayReferenceLowering.Rewrite(
            ILExpressionSpiller.Rewrite(instructions, analysis, selected, stableAddressProducers: contexts), reflection);

        void SelectValueExpression(int producer)
        {
            if (instructions[producer].OpCode == ILOpCode.Ldind_u1)
                return;
            SelectInputs(producer);
            foreach (int input in analysis.Inputs[producer])
                SelectValueExpression(input);
        }

    }
}
