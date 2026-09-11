using System.Reflection.Metadata;
using Xunit.Abstractions;

namespace dotnes.tests;

public class IntegerSemanticsTests(ITestOutputHelper output) : RoslynTests(output)
{
    [Fact]
    public void DeclaredNumericTypesSurviveMetadataParsing()
    {
        using var transpiler = BuildProgram("""
            ushort word = 0;
            short signedWord = 0;
            sbyte signedByte = 0;
            int counter = 0;
            while (true)
            {
                word = rand16();
                signedWord = (short)word;
                signedByte = (sbyte)signedWord;
                counter++;
                poke(0x6000, (byte)(word + signedWord + signedByte + counter));
            }
            static ushort Identity(byte value) => value;
            static extern short ReadSignedWord();
            """, out _);
        var main = transpiler.NumericTypes["main"];
        Assert.Contains(PrimitiveTypeCode.UInt16, main.Locals);
        Assert.Contains(PrimitiveTypeCode.Int16, main.Locals);
        Assert.Contains(PrimitiveTypeCode.SByte, main.Locals);
        Assert.Contains(PrimitiveTypeCode.Int32, main.Locals);
        Assert.Equal(PrimitiveTypeCode.UInt16, transpiler.NumericTypes["Identity"].ReturnType);
        Assert.Equal(PrimitiveTypeCode.Byte, Assert.Single(transpiler.NumericTypes["Identity"].Parameters));
        Assert.Equal(PrimitiveTypeCode.Int16, transpiler.NumericTypes["ReadSignedWord"].ReturnType);
    }
}
