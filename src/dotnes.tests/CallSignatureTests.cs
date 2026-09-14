using Xunit.Abstractions;

namespace dotnes.tests;

public class CallSignatureTests(ITestOutputHelper output) : RoslynTests(output)
{
    [Fact]
    public void DifferentNativeOverloadsKeepPerCallStackEffects()
    {
        using var transpiler = ReadProgram(
            """
            byte[] data = new byte[2];
            byte flag = peek(0x6010);
            if (flag == 0)
                vrambuf_put(0x2080, data, 2);
            else
                vrambuf_put(0x2080, "Hi");
            while (true) ;
            """);
        var il = transpiler.ReadStaticVoidMain().ToArray();
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        var calls = il.Select((instruction, index) => (instruction, index))
            .Where(pair => pair.instruction.String == nameof(NESLib.vrambuf_put)).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Contains(calls, pair => pair.instruction.CallSignature == (2, false));
        Assert.Contains(calls, pair => pair.instruction.CallSignature == (3, false));
        foreach (var (instruction, index) in calls)
        {
            Assert.Equal(instruction.CallSignature?.ArgumentCount, analysis.Inputs[index].Length);
            Assert.Empty(analysis.Outputs[index]);
        }
    }
}
