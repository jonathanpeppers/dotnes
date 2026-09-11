using System.Reflection.Metadata;

namespace dotnes.tests;

public class ILValueAnalysisTests
{
    [Fact]
    public void SourceAritySurvivesClosureAbiAdjustment()
    {
        var reflection = new ReflectionCache();
        reflection.RegisterUserMethod("Touch", 1, false);
        reflection.RegisterUserMethod("Touch", 0, false);
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloca_s, 0, 0),
            new(ILOpCode.Call, 2, String: "Touch"),
            new(ILOpCode.Ldc_i4_1, 7),
            new(ILOpCode.Stloc_1, 8),
        ];
        var analysis = new ILValueAnalysis(il, reflection);
        Assert.Equal(0, reflection.GetNumberOfArguments("Touch"));
        Assert.Equal(1, reflection.GetILNumberOfArguments("Touch"));
        Assert.Equal(new[] { 0 }, analysis.Inputs[1]);
        Assert.Empty(analysis.Outputs[1]);
    }

    [Fact]
    public void ConstantMemoryArgumentAfterLoopHasKnownProducer()
    {
        using var dll = Utilities.GetResource("shoot2.release.dll");
        using var transpiler = new Transpiler(dll, Array.Empty<AssemblyReader>());
        var il = transpiler.ReadStaticVoidMain().ToArray();
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        int call = Array.FindIndex(il, i => i.Offset == 0x028A);
        Assert.True(analysis.Inputs[call].SequenceEqual(new[] { call - 1 }),
            $"Inputs: {string.Join(",", analysis.Inputs[call])}; predecessor: {il[call - 1]}; edges: "
            + string.Join("; ", il.Where(i => ILValueAnalysis.GetBranchTargets(i).Contains(0x028A))));
    }

    [Fact]
    public void NestedOperandsHaveDistinctProducers()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloc_0, 0), new(ILOpCode.Ldloc_1, 1),
            new(ILOpCode.Ldc_i4_1, 2), new(ILOpCode.Shl, 3),
            new(ILOpCode.Add, 4), new(ILOpCode.Stloc_2, 5)
        ];
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        Assert.Equal(new[] { 1, 2 }, analysis.Inputs[3]);
        Assert.Equal(new[] { 0, 3 }, analysis.Inputs[4]);
        var rewritten = ILExpressionSpiller.Rewrite(il, analysis, new HashSet<int> { 0, 1, 2, 3 });
        Assert.Equal(3, rewritten.Count(i => i.OpCode == ILOpCode.Ldloc_s));
        Assert.Equal(3, rewritten.Count(i => i.OpCode == ILOpCode.Stloc_s));
        Assert.Equal(rewritten.Length, rewritten.Select(i => i.Offset).Distinct().Count());
    }

    [Fact]
    public void CallsConsumeOnlyTheirOwnInputs()
    {
        var reflection = new ReflectionCache();
        reflection.RegisterUserMethod("Next", 0, true);
        ILInstruction[] il =
        [
            new(ILOpCode.Call, 0, String: "Next"), new(ILOpCode.Call, 1, String: "Next"),
            new(ILOpCode.Sub, 2), new(ILOpCode.Stloc_0, 3)
        ];
        var analysis = new ILValueAnalysis(il, reflection);
        Assert.Empty(analysis.Inputs[1]);
        Assert.Equal(new[] { 0, 1 }, analysis.Inputs[2]);
        var rewritten = ILExpressionSpiller.Rewrite(il, analysis, new HashSet<int> { 0, 1 });
        Assert.Equal(2, rewritten.Count(i => i.OpCode == ILOpCode.Call));
        Assert.Equal(ILOpCode.Stloc_s, rewritten[1].OpCode);
        Assert.Equal(ILOpCode.Call, rewritten[2].OpCode);
    }

    [Fact]
    public void BranchRetainsUnchangedProducer()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloc_0, 0), new(ILOpCode.Br_s, 1, 1),
            new(ILOpCode.Nop, 3), new(ILOpCode.Stloc_1, 4)
        ];
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        Assert.False(analysis.Escapes[0]);
        Assert.Equal(new[] { 0 }, analysis.Inputs[3]);
        var rewritten = ILExpressionSpiller.Rewrite(il, analysis, new HashSet<int> { 0 });
        Assert.Equal(ILOpCode.Ldloc_s, Assert.Single(rewritten, i => i.Offset == 4).OpCode);
    }

    [Fact]
    public void DifferentMergeInputsRemainUnknown()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloc_0, 0), new(ILOpCode.Brtrue_s, 1, 3),
            new(ILOpCode.Ldloc_1, 3), new(ILOpCode.Br_s, 4, 1),
            new(ILOpCode.Ldloc_2, 6), new(ILOpCode.Stloc_3, 7), new(ILOpCode.Ret, 8)
        ];
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        Assert.Equal(new[] { -1 }, analysis.Inputs[5]);
        Assert.True(analysis.Escapes[2]);
        Assert.True(analysis.Escapes[4]);
        Assert.Throws<InvalidOperationException>(() =>
            ILExpressionSpiller.Rewrite(il, analysis, new HashSet<int> { 2 }));
    }

    [Fact]
    public void SpilledSwitchRetainsAllTargets()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloc_0, 0), new(ILOpCode.Switch, 1, 2, Bytes: [1, 0, 0, 0, 2, 0, 0, 0]),
            new(ILOpCode.Ret, 14), new(ILOpCode.Ret, 15), new(ILOpCode.Ret, 16)
        ];
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        var rewritten = ILExpressionSpiller.Rewrite(il, analysis, new HashSet<int> { 0 });
        Assert.Equal(new[] { 15, 16 }, ILValueAnalysis.GetBranchTargets(
            Assert.Single(rewritten, i => i.OpCode == ILOpCode.Switch)));
    }

    [Fact]
    public void SpilledBranchRetainsOriginalTarget()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloc_0, 0), new(ILOpCode.Ldc_i4_1, 1),
            new(ILOpCode.Beq_s, 2, 1), new(ILOpCode.Nop, 4), new(ILOpCode.Ret, 5)
        ];
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        var rewritten = ILExpressionSpiller.Rewrite(il, analysis, new HashSet<int> { 0, 1 });
        var branch = Assert.Single(rewritten, i => i.OpCode == ILOpCode.Beq);
        Assert.Equal(5, ILValueAnalysis.GetBranchTarget(branch));
        Assert.Equal(ILOpCode.Ldloc_s, Assert.Single(rewritten, i => i.Offset == 2).OpCode);
        Assert.Equal(0, ILValueAnalysis.GetBranchTarget(new(ILOpCode.Br_s, 4, 250)));
    }

    [Fact]
    public void ReturnConsumesAValueRatherThanEscapingIt()
    {
        ILInstruction[] il = [new(ILOpCode.Ldc_i4_1, 0), new(ILOpCode.Ret, 1)];
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        Assert.Equal(new[] { 0 }, analysis.Inputs[1]);
        Assert.False(analysis.Escapes[0]);
        var rewritten = ILExpressionSpiller.Rewrite(il, analysis, new HashSet<int> { 0 });
        Assert.Equal(ILOpCode.Ldc_i4_1, rewritten[1].OpCode);
        Assert.Equal(ILOpCode.Ret, rewritten[2].OpCode);
    }

    [Fact]
    public void SpillSlotsDoNotAliasAddressTakenLocals()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloca_s, 0, 8), new(ILOpCode.Initobj, 2, String: "Point"),
            new(ILOpCode.Ldloc_0, 7), new(ILOpCode.Ret, 8)
        ];
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        var rewritten = ILExpressionSpiller.Rewrite(il, analysis, new HashSet<int> { 2 });
        Assert.Equal(9, Assert.Single(rewritten, i => i.OpCode == ILOpCode.Stloc_s).Integer);
    }

    [Theory]
    [InlineData("InlineArrayAsSpan")]
    [InlineData("InlineArrayFirstElementRef")]
    [InlineData("Unsupported.Method")]
    public void UnknownCallDoesNotInventStackEffects(string name)
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloc_0, 0), new(ILOpCode.Call, 1, String: name),
            new(ILOpCode.Stloc_1, 6), new(ILOpCode.Ret, 7)
        ];
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        Assert.True(analysis.Escapes[0]);
        Assert.Equal(new[] { -1 }, analysis.Inputs[2]);
    }
}
