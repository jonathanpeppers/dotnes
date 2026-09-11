using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ByteCallExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void ForwardedClosurePreservesDynamicPokeAddressAcrossValueCall()
    {
        var cpu = ExecuteProgram(
            """
            byte captured = 7;
            Forward(3);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Next(byte value) => (byte)(value + 1);
            void Forward(byte offset) => Store(offset);
            void Store(byte offset)
            {
                byte value = captured;
                ushort address = (ushort)(0x6000 + offset);
                poke(address, (byte)(Next(offset) + value));
            }
            """);
        Assert.Equal(new byte[] { 0, 11, 0 }, cpu.Memory[0x6002..0x6005]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void ClosureParameterMetadataExcludesOrdinaryReferences()
    {
        using var assembly = CompileAssembly(
            """
            byte captured = 7;
            State state = default;
            byte[] array = { 1 };
            Probe(captured, ref captured, state, ref state, array, ref array);
            Forward(captured);
            while (true) ;
            void Forward(byte value) => Inner(value);
            void Inner(byte value) => poke(0x6000, (byte)(value + captured));
            static void Probe(byte value, ref byte reference, State state, ref State stateReference,
                byte[] array, ref byte[] arrayReference) { }
            struct State { public byte Value; }
            """);
        using var pe = new PEReader(assembly);
        var reader = pe.GetMetadataReader();
        var closureType = Assert.Single(reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name).Contains("DisplayClass"));
        var decoder = new ClosureParameterDecoder(new HashSet<TypeDefinitionHandle> { closureType });
        var methods = reader.MethodDefinitions.Select(reader.GetMethodDefinition).ToArray();
        var forward = Assert.Single(methods, method => reader.GetString(method.Name).Contains(">g__Forward|"));
        Assert.Equal(new[] { ClosureParameterKind.None, ClosureParameterKind.ByReference },
            forward.DecodeSignature(decoder, null).ParameterTypes);
        var probe = Assert.Single(methods, method => reader.GetString(method.Name).Contains(">g__Probe|"));
        Assert.All(probe.DecodeSignature(decoder, null).ParameterTypes,
            parameter => Assert.Equal(ClosureParameterKind.None, parameter));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ForwardingOnlyClosureMethodsKeepPhysicalArgumentOffsets(bool multipleHops, bool computedArguments)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte captured = 7;
            byte result = {{(multipleHops ? "Outer" : "Forward")}}(43, 88);
            poke(0x6000, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Next(byte value) => (byte)(value + 1);
            {{(multipleHops ? "byte Outer(byte first, byte second) => Forward(first, second);" : "")}}
            byte Forward(byte first, byte second) =>
                Inner({{(computedArguments ? "Next(first), Next(second)" : "first, second")}});
            byte Inner(byte first, byte second)
            {
                byte a = first, b = second, c = captured;
                poke(0x6001, a);
                poke(0x6002, b);
                poke(0x6003, c);
                return (byte)(a + b + c);
            }
            """);
        Assert.Equal(new byte[] { (byte)(computedArguments ? 140 : 138),
            (byte)(computedArguments ? 44 : 43), (byte)(computedArguments ? 89 : 88), 7 },
            cpu.Memory[0x6000..0x6004]);
        Assert.Equal(0xFD, cpu.SP);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void WriteOnlyClosureContextIsNotAPhysicalArgument()
    {
        var cpu = ExecuteProgram(
            """
            byte captured = 7;
            Forward(43);
            byte result = Read();
            poke(0x6000, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            void Forward(byte value) => Assign(value);
            void Assign(byte value) { captured = value; }
            byte Read() => captured;
            """);
        Assert.Equal(43, cpu.Memory[0x6000]);
        Assert.Equal(0xFD, cpu.SP);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CapturedHelperPushesEveryEarlierComputedArgument(bool forwarded, bool threeArguments)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte captured = 7;
            {{(forwarded ? "Outer(43, 88);" : $"Consume(Next(43), Next(88){(threeArguments ? ", Next(110)" : "")});")}}
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Next(byte value) => (byte)(value + 1);
            {{(forwarded ? $$"""
            void Outer(byte first, byte second)
            {
                Consume(Next(first), Next(second){{(threeArguments ? ", Next(110)" : "")}});
                byte c = captured;
                poke(0x6004, c);
            }
            """ : "")}}
            void Consume(byte a, byte b{{(threeArguments ? ", byte d" : "")}})
            {
                byte av = a, bv = b, c = captured;
                poke(0x6000, av);
                poke(0x6001, bv);
                poke(0x6002, c);
                {{(threeArguments ? "byte dv = d; poke(0x6003, dv);" : "")}}
            }
            """);
        Assert.Equal(new byte[] { 44, 89, 7, (byte)(threeArguments ? 111 : 0), (byte)(forwarded ? 7 : 0) },
            cpu.Memory[0x6000..0x6005]);
        Assert.Equal(0xFD, cpu.SP);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedCallerForwardsOnlyItsStableContext(bool earlierArgument)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte captured = 7;
            Outer(43);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Next(byte value) => value;
            void Outer(byte value)
            {
                Consume({{(earlierArgument ? "88, " : "")}}(byte)(Next(value) + 1));
                byte c = captured;
                poke(0x6002, c);
            }
            void Consume({{(earlierArgument ? "byte first, " : "")}}byte value)
            {
                byte c = captured;
                poke(0x6001, c);
                byte v = value;
                poke(0x6000, v);
                {{(earlierArgument ? "byte a = first; poke(0x6003, a);" : "")}}
            }
            """);
        Assert.Equal(new byte[] { 44, 7, 7, (byte)(earlierArgument ? 88 : 0) }, cpu.Memory[0x6000..0x6004]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComputedValueCanBePassedToCapturedScalarHelper(bool earlierArgument)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte captured = 7;
            byte frame = 0;
            while (frame < 2)
            {
                Consume({{(earlierArgument ? "88, " : "")}}(byte)(Next() + 1));
                frame++;
            }
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Next() => 43;
            void Consume({{(earlierArgument ? "byte first, " : "")}}byte value)
            {
                byte c = captured;
                poke(0x6001, c);
                byte v = value;
                poke(0x6000, v);
                {{(earlierArgument ? "byte a = first; poke(0x6002, a);" : "")}}
            }
            """);
        Assert.Equal(new byte[] { 44, 7, (byte)(earlierArgument ? 88 : 0) }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ClosureCallDoesNotLeaveAPhantomOperand()
    {
        var cpu = ExecuteProgram(
            """
            byte captured = 7;
            Outer(43);
            test_stop();
            while (true) ;
            static extern void test_stop();
            void Outer(byte value)
            {
                byte c = captured;
                poke(0x6001, c);
                Touch();
                Ignore(value, value);
                byte v = value;
                poke(0x6000, v);
            }
            void Touch()
            {
                byte c = captured;
                poke(0x6002, c);
            }
            static void Ignore(byte first, byte second) { }
            """);
        Assert.Equal(new byte[] { 43, 7, 7 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

}
