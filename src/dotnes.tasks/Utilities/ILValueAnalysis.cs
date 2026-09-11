using System.Reflection.Emit;
using System.Reflection.Metadata;

namespace dotnes;

/// <summary>
/// Producer identities for evaluation-stack operands within straight-line IL.
/// Unknown control-flow inputs are never treated as constants or local values.
/// </summary>
sealed class ILValueAnalysis
{
    static readonly Dictionary<ushort, OpCode> opcodes = typeof(OpCodes).GetFields()
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => unchecked((ushort)op.Value));

    public int[][] Inputs { get; }
    public List<int>[] Consumers { get; }
    public bool[] ProducesValue { get; }
    public bool[] Escapes { get; }

    public ILValueAnalysis(ILInstruction[] instructions, ReflectionCache reflection)
    {
        Inputs = new int[instructions.Length][];
        Consumers = Enumerable.Range(0, instructions.Length).Select(_ => new List<int>()).ToArray();
        ProducesValue = new bool[instructions.Length];
        Escapes = new bool[instructions.Length];
        var stack = new List<int>();
        var targets = new HashSet<int>(instructions.Where(i => IsBranch(i.OpCode) && i.Integer.HasValue)
            .Select(i => i.Integer!.Value));

        void EndRegion()
        {
            foreach (int producer in stack)
                Escapes[producer] = true;
            stack.Clear();
        }

        for (int i = 0; i < instructions.Length; i++)
        {
            var instruction = instructions[i];
            if (targets.Contains(instruction.Offset))
                EndRegion();
            if (instruction.OpCode == ILOpCode.Dup)
            {
                Inputs[i] = stack.Count > 0 ? new[] { stack[stack.Count - 1] } : new[] { -1 };
                if (stack.Count > 0)
                {
                    Consumers[stack[stack.Count - 1]].Add(i);
                    stack.Add(stack[stack.Count - 1]);
                }
                continue;
            }
            if (!TryGetEffect(instruction, reflection, out int pop, out int push))
            {
                EndRegion();
                Inputs[i] = Array.Empty<int>();
                continue;
            }
            var inputs = new int[pop];
            for (int j = pop - 1; j >= 0; j--)
            {
                inputs[j] = stack.Count > 0 ? stack[stack.Count - 1] : -1;
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                    Consumers[inputs[j]].Add(i);
                }
            }
            Inputs[i] = inputs;
            if (push == 1)
            {
                ProducesValue[i] = true;
                stack.Add(i);
            }
            if (IsBranch(instruction.OpCode) || instruction.OpCode is ILOpCode.Ret or ILOpCode.Throw)
                EndRegion();
        }
        EndRegion();
    }

    internal static bool IsBranch(ILOpCode code) =>
        opcodes.TryGetValue((ushort)code, out var op)
        && op.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch;

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
            if (method.Contains('.') && !reflection.IsUserMethod(method) && !reflection.IsExternMethod(method))
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
