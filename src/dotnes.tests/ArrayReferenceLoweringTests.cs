using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes.tests;

public class ArrayReferenceLoweringTests
{
    [Fact]
    public void RemovesOnlyCapturedReferencesAndPreservesOriginalLabels()
    {
        var reflection = Reflection();
        var source = Instructions();
        var rewritten = ArrayReferenceLowering.Rewrite(source, reflection);
        Assert.All(source, instruction => Assert.Single(rewritten, i => i.Offset == instruction.Offset));
        Assert.DoesNotContain(rewritten, i => i.OpCode is ILOpCode.Ldelema or ILOpCode.Ldind_u1 or ILOpCode.Stind_i1);
        Assert.Single(rewritten, i => i.OpCode == ILOpCode.Ldelem_u1);
        Assert.Single(rewritten, i => i.OpCode == ILOpCode.Stelem_i1);
        int call = Array.FindIndex(rewritten, i => i.OpCode == ILOpCode.Call);
        var analysis = new ILValueAnalysis(rewritten, reflection);
        Assert.Empty(analysis.Outputs[call - 1]);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void ReassignedOrAddressTakenCaptureIsDiagnosed(int local, bool addressTaken)
    {
        var source = Instructions().ToList();
        source.InsertRange(8, addressTaken
            ? new ILInstruction[] { new(ILOpCode.Ldloca_s, 100, local), new(ILOpCode.Pop, 101) }
            : new ILInstruction[] { new(ILOpCode.Ldloc_s, 100, 5), new(ILOpCode.Stloc_s, 101, local) });
        var exception = Assert.Throws<TranspileException>(() =>
            ArrayReferenceLowering.Rewrite(source.ToArray(), Reflection()));
        Assert.Contains("unchanged captured operands", exception.Message);
    }

    static ReflectionCache Reflection()
    {
        var reflection = new ReflectionCache();
        reflection.RegisterUserMethod("Value", 0, true);
        return reflection;
    }

    static ILInstruction[] Instructions() =>
    [
        new(ILOpCode.Ldloc_0, 0),
        new(ILOpCode.Ldloc_1, 1),
        new(ILOpCode.Ldelema, 2, String: "Byte"),
        new(ILOpCode.Dup, 3),
        new(ILOpCode.Ldind_u1, 4),
        new(ILOpCode.Stloc_2, 5),
        new(ILOpCode.Call, 6, String: "Value"),
        new(ILOpCode.Stloc_3, 7),
        new(ILOpCode.Ldloc_2, 8),
        new(ILOpCode.Ldloc_3, 9),
        new(ILOpCode.Add, 10),
        new(ILOpCode.Stloc_s, 11, 4),
        new(ILOpCode.Ldloc_s, 12, 4),
        new(ILOpCode.Stind_i1, 13),
        new(ILOpCode.Ret, 14),
    ];
}
