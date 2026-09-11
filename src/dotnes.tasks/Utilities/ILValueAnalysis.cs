using System.Reflection.Emit;
using System.Reflection.Metadata;

namespace dotnes;

/// <summary>
/// Producer identities for evaluation-stack operands, including unchanged values
/// carried across control-flow edges. Differing merge inputs remain unknown.
/// Unknown control-flow inputs are never treated as constants or local values.
/// </summary>
sealed class ILValueAnalysis
{
    static readonly Dictionary<ushort, OpCode> opcodes = typeof(OpCodes).GetFields()
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => unchecked((ushort)op.Value));
    static readonly HashSet<string> nesLibMethods = new(typeof(NESLib).GetMethods()
        .Select(method => method.Name), StringComparer.Ordinal);

    public int[][] Inputs { get; }
    public List<int>[] Consumers { get; }
    public bool[] ProducesValue { get; }
    public bool[] Escapes { get; }

    public ILValueAnalysis(ILInstruction[] instructions, ReflectionCache reflection)
    {
        Inputs = Enumerable.Range(0, instructions.Length).Select(_ => Array.Empty<int>()).ToArray();
        Consumers = Enumerable.Range(0, instructions.Length).Select(_ => new List<int>()).ToArray();
        ProducesValue = new bool[instructions.Length];
        Escapes = new bool[instructions.Length];
        var offsets = instructions.Select((instruction, index) => (instruction.Offset, index))
            .ToDictionary(pair => pair.Offset, pair => pair.index);
        var states = new int[]?[instructions.Length];
        var pending = new Queue<int>();
        if (instructions.Length > 0)
        {
            states[0] = Array.Empty<int>();
            pending.Enqueue(0);
        }

        void Escape(IEnumerable<int> values)
        {
            foreach (int producer in values)
                if (producer >= 0)
                    Escapes[producer] = true;
        }

        void Merge(int successor, List<int> stack)
        {
            if (successor >= instructions.Length)
            {
                Escape(stack);
                return;
            }
            var old = states[successor];
            if (old == null)
            {
                states[successor] = stack.ToArray();
                pending.Enqueue(successor);
                return;
            }
            if (old.Length != stack.Count)
            {
                Escape(old);
                Escape(stack);
                // An unknown stack effect cannot manufacture known operands.
                if (old.Any(p => p >= 0) || old.Length > stack.Count)
                {
                    states[successor] = Enumerable.Repeat(-1, Math.Min(old.Length, stack.Count)).ToArray();
                    pending.Enqueue(successor);
                }
                return;
            }
            bool changed = false;
            for (int j = 0; j < old.Length; j++)
            {
                if (old[j] == stack[j])
                    continue;
                Escape(new[] { old[j], stack[j] });
                changed |= old[j] != -1;
                old[j] = -1;
            }
            if (changed)
                pending.Enqueue(successor);
        }

        while (pending.Count > 0)
        {
            int i = pending.Dequeue();
            var instruction = instructions[i];
            var stack = new List<int>(states[i]!);
            if (instruction.OpCode == ILOpCode.Dup)
            {
                Inputs[i] = stack.Count > 0 ? new[] { stack[stack.Count - 1] } : new[] { -1 };
                stack.Add(Inputs[i][0]);
            }
            else if (instruction.OpCode == ILOpCode.Ret)
            {
                Inputs[i] = stack.Count == 1 ? new[] { stack[0] } : Array.Empty<int>();
                if (stack.Count != 1)
                    Escape(stack);
                continue;
            }
            else if (!TryGetEffect(instruction, reflection, out int pop, out int push))
            {
                Escape(stack);
                stack.Clear();
            }
            else
            {
                var inputs = new int[pop];
                for (int j = pop - 1; j >= 0; j--)
                {
                    inputs[j] = stack.Count > 0 ? stack[stack.Count - 1] : -1;
                    if (stack.Count > 0)
                        stack.RemoveAt(stack.Count - 1);
                }
                Inputs[i] = inputs;
                if (push == 1)
                {
                    ProducesValue[i] = true;
                    stack.Add(i);
                }
            }
            if (instruction.OpCode is ILOpCode.Leave or ILOpCode.Leave_s)
            {
                Escape(stack);
                stack.Clear();
            }
            foreach (int target in GetBranchTargets(instruction))
            {
                if (offsets.TryGetValue(target, out int successor))
                    Merge(successor, stack);
                else
                    Escape(stack);
            }
            if (!opcodes.TryGetValue((ushort)instruction.OpCode, out var opcode)
                || opcode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw))
                Merge(i + 1, stack);
            else if (opcode.FlowControl is FlowControl.Return or FlowControl.Throw)
                Escape(stack);
        }
        for (int i = 0; i < Inputs.Length; i++)
            foreach (int producer in Inputs[i])
                if (producer >= 0)
                    Consumers[producer].Add(i);
    }

    internal static IEnumerable<int> GetBranchTargets(ILInstruction instruction)
    {
        if (GetBranchTarget(instruction) is int target)
            yield return target;
        else if (instruction.OpCode == ILOpCode.Switch && instruction.Bytes is { } bytes
            && instruction.Integer is int count)
        {
            int start = instruction.Offset + 5 + count * 4;
            var targets = bytes.ToArray();
            for (int i = 0; i < count; i++)
                yield return start + BitConverter.ToInt32(targets, i * 4);
        }
    }

    internal static bool IsBranch(ILOpCode code) =>
        opcodes.TryGetValue((ushort)code, out var op)
        && op.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch;

    internal static int? GetBranchTarget(ILInstruction instruction)
    {
        if (!IsBranch(instruction.OpCode) || instruction.Integer is not int operand)
            return null;
        var op = opcodes[(ushort)instruction.OpCode];
        return op.OperandType switch
        {
            OperandType.ShortInlineBrTarget => instruction.Offset + 2 + unchecked((sbyte)operand),
            OperandType.InlineBrTarget => instruction.Offset + 5 + operand,
            _ => null
        };
    }

    static bool TryGetEffect(ILInstruction instruction, ReflectionCache reflection, out int pop, out int push)
    {
        pop = push = 0;
        if (instruction.OpCode == ILOpCode.Call && instruction.String is string method)
        {
            if (method is "InitializeArray" or "RuntimeHelpers.InitializeArray")
            {
                pop = 2;
                return true;
            }
            if (method == "Array.Fill") { pop = 2; return true; }
            if (method == "Array.Copy") { pop = 3; return true; }
            if (!reflection.IsUserMethod(method) && !reflection.IsExternMethod(method)
                && !nesLibMethods.Contains(method))
                return false;
            pop = reflection.GetNumberOfArguments(method);
            push = reflection.HasReturnValue(method) ? 1 : 0;
            return true;
        }
        if (!opcodes.TryGetValue((ushort)instruction.OpCode, out var opcode))
            return false;
        pop = opcode.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi
                or StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8
                or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_pop1
                or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8
                or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8
                or StackBehaviour.Popref_popi_popref => 3,
            _ => -1
        };
        push = opcode.StackBehaviourPush switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8
                or StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
            _ => -1
        };
        return pop >= 0 && push >= 0;
    }
}
