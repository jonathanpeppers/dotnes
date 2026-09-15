using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ByteCallExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 3)]
    [InlineData(127, 126)]
    [InlineData(255, 254)]
    public void ByteParameterBoundsLoop(byte end, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte value = Operations.Count({{end}});
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class Operations
            {
                public static byte Count(byte end)
                {
                    byte count = 0;
                    for (byte i = 1; i < end; i++) count++;
                    return count;
                }
            }
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(255)]
    public void MutatingByteParameterPreservesCaller(byte input)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte value = {{input}};
            byte result = Increment(value);
            poke(0x6000, result);
            poke(0x6001, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Increment(byte value) { value++; return value; }
            """);
        Assert.Equal(unchecked((byte)(input + 1)), cpu.Memory[0x6000]);
        Assert.Equal(input, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    public static TheoryData<byte, byte, string, byte> ParameterComparisons()
    {
        var data = new TheoryData<byte, byte, string, byte>();
        foreach (byte left in new byte[] { 0, 127, 255 })
        foreach (byte right in new byte[] { 0, 127, 255 })
        {
            data.Add(left, right, "<", (byte)(left < right ? 1 : 0));
            data.Add(left, right, "<=", (byte)(left <= right ? 1 : 0));
            data.Add(left, right, ">", (byte)(left > right ? 1 : 0));
            data.Add(left, right, ">=", (byte)(left >= right ? 1 : 0));
            data.Add(left, right, "==", (byte)(left == right ? 1 : 0));
            data.Add(left, right, "!=", (byte)(left != right ? 1 : 0));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ParameterComparisons))]
    public void ByteParameterComparisons(byte left, byte right, string comparison, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte result = Compare({{left}}, {{right}});
            poke(0x6000, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Compare(byte left, byte right)
            {
                if (left {{comparison}} right) return 1;
                return 0;
            }
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("value--;", 0, 255)]
    [InlineData("value += 128;", 127, 255)]
    [InlineData("value += 1;", 255, 0)]
    [InlineData("value -= 128;", 127, 255)]
    [InlineData("value *= 2;", 255, 254)]
    [InlineData("value /= 2;", 255, 127)]
    [InlineData("value %= 8;", 255, 7)]
    [InlineData("value &= 127;", 255, 127)]
    [InlineData("value |= 128;", 127, 255)]
    [InlineData("value ^= 255;", 255, 0)]
    [InlineData("value <<= 1;", 127, 254)]
    [InlineData("value >>= 1;", 255, 127)]
    public void ByteParameterCompoundAssignment(string statement, byte input, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte value = {{input}};
            byte result = Change(value);
            poke(0x6000, result);
            poke(0x6001, value);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Change(byte value) { {{statement}} return value; }
            """);
        Assert.Equal(new[] { expected, input }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(255)]
    public void ParameterMutationKeepsNestedCallEvaluationOrder(byte input)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte result = Outer({{input}});
            poke(0x6004, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Outer(byte value)
            {
                Store(value++, Change(value), ++value);
                return value;
            }
            static byte Change(byte value) { value += 8; return value; }
            static void Store(byte first, byte second, byte third)
            {
                poke(0x6000, first);
                poke(0x6001, second);
                poke(0x6002, third);
            }
            """);
        Assert.Equal(new byte[] { input, unchecked((byte)(input + 9)), unchecked((byte)(input + 2)), 0,
            unchecked((byte)(input + 2)) }, cpu.Memory[0x6000..0x6005]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(255)]
    public void PostIncrementSnapshotsTheOldParameter(byte input)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte old = Increment({{input}});
            poke(0x6000, old);
            Pair({{input}}, 8);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Increment(byte value) => value++;
            static void Pair(byte value, byte increment)
            {
                Store(value++, value);
                value += increment;
                poke(0x6003, value);
            }
            static void Store(byte first, byte second)
            {
                poke(0x6001, first);
                poke(0x6002, second);
            }
            """);
        Assert.Equal(new byte[] { input, input, unchecked((byte)(input + 1)), unchecked((byte)(input + 9)) },
            cpu.Memory[0x6000..0x6004]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(-128, -127)]
    [InlineData(-1, 0)]
    [InlineData(127, -128)]
    public void SignedArgumentStoreRetainsByteWidth(sbyte input, sbyte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            short value = Increment({{input}});
            poke(0x6000, (byte)value);
            poke(0x6001, (byte)(value >> 8));
            test_stop(); while (true) ;
            static extern void test_stop();
            static short Increment(sbyte value) { value++; return value; }
            """);
        Assert.Equal(unchecked((ushort)expected), cpu.Memory[0x6000] | cpu.Memory[0x6001] << 8);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ArgumentMutationWorksWithArrayParameterOffsets()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[8];
            byte result = Change(255, data, 127);
            byte stored = data[0];
            poke(0x6000, result);
            poke(0x6001, stored);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Change(byte first, byte[] data, byte last)
            {
                first++;
                last++;
                data[first] = last;
                return first;
            }
            """);
        Assert.Equal(new byte[] { 0, 128 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ReemittedByteArgumentsRegisterPushaDependency()
    {
        var reflection = new ReflectionCache();
        reflection.RegisterUserMethod("Choose", 2, true);
        ILInstruction[] il =
        [
            new(ILOpCode.Ldc_i4_s, 0, Integer: 21),
            new(ILOpCode.Ldc_i4_s, 1, Integer: 43),
            new(ILOpCode.Call, 2, String: "Choose"),
        ];
        using var writer = new IL2NESWriter(new MemoryStream(), reflectionCache: reflection)
        {
            Instructions = il,
            UsedMethods = new HashSet<string>(),
            UserMethodNames = new HashSet<string> { "Choose" },
            ByteParameterCalls = new Dictionary<string, int> { ["Choose"] = -1 },
        };
        writer.ConfigureNumericTypes(new Dictionary<string, MethodNumericTypes>
        {
            ["main"] = new([], [], PrimitiveTypeCode.Void),
            ["Choose"] = new([], [PrimitiveTypeCode.Byte, PrimitiveTypeCode.Byte], PrimitiveTypeCode.Byte),
        });
        writer.StartBlockBuffering();
        for (int i = 0; i < il.Length; i++)
        {
            writer.Index = i;
            writer.RecordBlockCount(il[i].Offset);
            if (il[i].Integer is int value)
                writer.Write(il[i], value);
            else
                writer.Write(il[i], il[i].String!);
        }
        Assert.Contains("pusha", writer.UsedMethods);
    }

    [Theory]
    [InlineData(0, 23)]
    [InlineData(1, 24)]
    public void ConditionalLeftOperandIsEvaluatedInsteadOfReplacedByAdjacentLoad(byte flag, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte result = Select({{flag}});
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Select(byte flag)
            {
                byte a = 7;
                byte b = 8;
                byte c = 16;
                return (byte)((flag == 0 ? a : b) | c);
            }
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(0x0800, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ByteArgumentsWorkAlongsideDecsp4Runtime()
    {
        var cpu = ExecuteProgram(
            """
            oam_spr(1, 2, 3, 0, 0);
            byte value = Choose(21, 43);
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Choose(byte first, byte second) => second;
            """);
        Assert.Equal(43, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

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

    [Fact]
    public void NestedByteCallKeepsTheEnclosingArgument()
    {
        var cpu = ExecuteProgram(
            """
            Outer(11, Inner(22, 33));
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Inner(byte first, byte second) => second;
            static void Outer(byte first, byte second)
            {
                byte a = first;
                byte b = second;
                poke(0x6000, a);
                poke(0x6001, b);
            }
            """);
        Assert.Equal(new byte[] { 11, 33 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ByteReturnReplacesPadPollProvenance()
    {
        var cpu = ExecuteProgram(
            """
            byte buttons = (byte)pad_poll(0);
            byte value = Choose(21, 43);
            byte result = (byte)(value & 15);
            poke(0x6000, result);
            poke(0x6001, buttons);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Choose(byte first, byte second) => second;
            """);
        Assert.Equal(11, cpu.Memory[0x6000]);
        Assert.Equal(0, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ForwardedByteArgumentsKeepCallerFrame()
    {
        var cpu = ExecuteProgram(
            """
            byte result = Forward(43);
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Forward(byte value)
            {
                Ignore(value, value);
                return value;
            }
            static void Ignore(byte first, byte second) { }
            """);
        Assert.Equal(43, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
