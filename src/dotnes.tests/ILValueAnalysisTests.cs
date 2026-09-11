using System.Reflection.Metadata;

namespace dotnes.tests;

public class ILValueAnalysisTests
{
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
    public void BranchEscapesAreNotInvented()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloc_0, 0), new(ILOpCode.Br_s, 1, 4),
            new(ILOpCode.Nop, 3), new(ILOpCode.Stloc_1, 4)
        ];
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        Assert.True(analysis.Escapes[0]);
        Assert.Equal(new[] { -1 }, analysis.Inputs[3]);
        Assert.Throws<InvalidOperationException>(() =>
            ILExpressionSpiller.Rewrite(il, analysis, new HashSet<int> { 0 }));
    }
}
