using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace dotnes.tests;

public class ArrayStorageAnalysisTests
{
    [Fact]
    public void LocalAliasChainsResolveTheirReachingBinding()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldtoken, 0, Bytes: ImmutableArray.Create<byte>(1, 2, 3, 4)),
            new(ILOpCode.Stloc_0, 1),
            new(ILOpCode.Ldloc_0, 2),
            new(ILOpCode.Stloc_1, 3),
            new(ILOpCode.Ldloc_1, 4),
            new(ILOpCode.Stloc_2, 5),
            new(ILOpCode.Ldloc_2, 6),
            new(ILOpCode.Pop, 7),
            new(ILOpCode.Ldc_i4_4, 8),
            new(ILOpCode.Newarr, 9, String: "Byte"),
            new(ILOpCode.Stloc_2, 10),
            new(ILOpCode.Ldloc_2, 11),
        ];
        var storage = new ArrayStorageAnalysis([il], new ReflectionCache());
        Assert.Equal(ArrayStorage.Rom, storage.GetStorage(il, 6));
        Assert.Equal(ArrayStorage.Ram, storage.GetStorage(il, 11));
    }

    [Fact]
    public void StaticAliasesResolveAcrossMethods()
    {
        ILInstruction[] initializer =
        [
            new(ILOpCode.Ldtoken, 0, Bytes: ImmutableArray.Create<byte>(1, 2, 3, 4)),
            new(ILOpCode.Stsfld, 1, String: "Original"),
            new(ILOpCode.Ldsfld, 2, String: "Original"),
            new(ILOpCode.Stsfld, 3, String: "Alias"),
        ];
        ILInstruction[] consumer =
        [
            new(ILOpCode.Ldsfld, 0, String: "Alias"),
            new(ILOpCode.Stloc_0, 1),
            new(ILOpCode.Ldloc_0, 2),
        ];
        var storage = new ArrayStorageAnalysis([initializer, consumer], new ReflectionCache());
        Assert.Equal(ArrayStorage.Rom, storage.GetStorage(consumer, 2));
    }
}
