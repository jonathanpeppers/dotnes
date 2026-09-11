using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace dotnes.tests;

public class NumericProducerProofTests
{
    static void Configure(IL2NESWriter writer) => writer.ConfigureNumericTypes(
        new Dictionary<string, MethodNumericTypes>
        {
            ["main"] = new([PrimitiveTypeCode.UInt16, PrimitiveTypeCode.Byte], [], PrimitiveTypeCode.Void),
        }, new Dictionary<string, PrimitiveTypeCode?> { ["Value"] = PrimitiveTypeCode.UInt16 });

    [Theory]
    [InlineData(ILOpCode.Br_s, 0, true)]
    [InlineData(ILOpCode.Br, 0, true)]
    [InlineData(ILOpCode.Switch, 0, true)]
    [InlineData(ILOpCode.Br_s, 1, false)]
    [InlineData(ILOpCode.Br, 2, false)]
    [InlineData(ILOpCode.Switch, 3, false)]
    [InlineData(ILOpCode.Br_s, 4, false)]
    public void ControlFlowCannotBypassTheWordLoad(ILOpCode branch, int target, bool expected)
    {
        using var stream = new MemoryStream();
        using var writer = new IL2NESWriter(stream);
        var control = branch == ILOpCode.Switch
            ? new ILInstruction(branch, 10, 1, Bytes: BitConverter.GetBytes(target - 19).ToImmutableArray())
            : new ILInstruction(branch, 10, target - (branch == ILOpCode.Br_s ? 12 : 15));
        writer.Instructions =
        [
            new(ILOpCode.Ldloc_0, 0),
            new(ILOpCode.Ldc_i4_1, 1),
            new(ILOpCode.Add, 2),
            new(ILOpCode.Conv_u2, 3),
            new(ILOpCode.Ldloc_1, 4),
            control,
        ];
        writer.Index = 4;
        Configure(writer);
        writer.Variables.Locals[0] = new(0, 0x325, IsWord: true);
        writer.StartBlockBuffering();
        writer.RecordBlockCount(0);
        writer.RecordBlockCount(2);
        Assert.Equal(expected, writer.HasVerifiedNumericProducer(2));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void ProvesOnlyLoweredWordLoadAndConstant(bool field, bool add)
    {
        using var stream = new MemoryStream();
        using var writer = new IL2NESWriter(stream);
        writer.Instructions =
        [
            field ? new(ILOpCode.Ldsfld, 0, String: "Value") : new(ILOpCode.Ldloc_0, 0),
            new(ILOpCode.Ldc_i4_1, 1),
            new(add ? ILOpCode.Add : ILOpCode.Sub, 2),
            new(ILOpCode.Conv_u2, 3),
        ];
        writer.Index = 3;
        Configure(writer);
        writer.Variables.Locals[0] = new(0, 0x325, IsWord: true);
        writer.StaticFieldAddresses["Value"] = 0x325;
        writer.WordStaticFields.Add("Value");
        writer.StartBlockBuffering();
        Assert.False(writer.HasVerifiedNumericProducer(2));
        writer.RecordBlockCount(0);
        writer.RecordBlockCount(2);
        Assert.True(writer.HasVerifiedNumericProducer(2));

        writer.Variables.Locals[0] = new(0, 0x325);
        writer.WordStaticFields.Clear();
        Assert.False(writer.HasVerifiedNumericProducer(2));
    }

    [Theory]
    [InlineData(ILOpCode.Add, ILOpCode.Ldc_i4_1)]
    [InlineData(ILOpCode.Sub, ILOpCode.Ldc_i4_1)]
    [InlineData(ILOpCode.Conv_u2, ILOpCode.Ldc_i4_1)]
    [InlineData(ILOpCode.Ldloc_0, ILOpCode.Ldloc_1)]
    [InlineData(ILOpCode.Ldloc_0, ILOpCode.Ldc_i4_m1)]
    public void RejectsArithmeticAncestorsAndUnprovenConstants(ILOpCode source, ILOpCode right)
    {
        using var stream = new MemoryStream();
        using var writer = new IL2NESWriter(stream);
        writer.Instructions =
        [
            new(source, 0),
            new(right, 1),
            new(ILOpCode.Add, 2),
            new(ILOpCode.Conv_u2, 3),
        ];
        writer.Index = 3;
        writer.Variables.Locals[0] = new(0, 0x325, IsWord: true);
        writer.StartBlockBuffering();
        writer.RecordBlockCount(0);
        writer.RecordBlockCount(2);
        Assert.False(writer.HasVerifiedNumericProducer(2));
    }
}
