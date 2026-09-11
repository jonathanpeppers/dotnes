using Xunit.Abstractions;

namespace dotnes.tests;

public class DynamicMemoryTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(false, 0x700)]
    [InlineData(true, 0x700)]
    [InlineData(false, 0x800)]
    [InlineData(true, 0x800)]
    public void ReadInHelper(bool materialize, ushort stackTop)
    {
        var cpu = ExecuteProgram($$"""
            poke(0x6000, 21);
            poke(0x6003, 75);
            poke(0x60E0, 3);
            byte index = peek(0x60E0);
            byte value = 0;
            for (byte frame = 0; frame < 3; frame++)
                value = Read(index);
            poke(0x6010, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Read(byte index)
            {
                {{(materialize
                    ? "ushort address = 0x6000; address = (ushort)(address + index); return peek(address);"
                    : "return peek((ushort)(0x6000 + index));")}}
            }
            """, cpu => cpu.Memory[0x23] = (byte)(stackTop >> 8));
        Assert.Equal(75, cpu.Memory[0x6010]);
        Assert.Equal(stackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(false, 0x700)]
    [InlineData(true, 0x700)]
    [InlineData(false, 0x800)]
    [InlineData(true, 0x800)]
    public void WriteInHelper(bool materialize, ushort stackTop)
    {
        var cpu = ExecuteProgram($$"""
            poke(0x6000, 21);
            poke(0x6003, 75);
            poke(0x60E0, 3);
            byte index = peek(0x60E0);
            byte value = 75;
            for (byte frame = 0; frame < 3; frame++)
            {
                Write(index, value);
                value++;
            }
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Write(byte index, byte value)
            {
                {{(materialize
                    ? "ushort address = 0x6000; address = (ushort)(address + index); poke(address, value);"
                    : "poke((ushort)(0x6000 + index), value);")}}
            }
            """, cpu => cpu.Memory[0x23] = (byte)(stackTop >> 8));
        Assert.Equal(21, cpu.Memory[0x6000]);
        Assert.Equal(77, cpu.Memory[0x6003]);
        Assert.Equal(stackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(16, false)]
    [InlineData(255, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(16, true)]
    [InlineData(255, true)]
    public void AddressesCarryAcrossPages(byte index, bool reversed)
    {
        var cpu = ExecuteProgram($$"""
            Write({{index}}, 53);
            byte result = Read({{index}});
            poke(0x6030, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Write(byte index, byte value)
            {
                poke((ushort)({{(reversed ? "index + 0x60F0" : "0x60F0 + index")}}), value);
            }
            static byte Read(byte index)
            {
                return peek((ushort)({{(reversed ? "index + 0x60F0" : "0x60F0 + index")}}));
            }
            """);
        Assert.Equal(53, cpu.Memory[0x60F0 + index]);
        Assert.Equal(53, cpu.Memory[0x6030]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0x40)]
    [InlineData(0xFF)]
    public void ByteAddressClearsTheHighByte(byte address)
    {
        var cpu = ExecuteProgram($$"""
            Write({{address}}, 93);
            byte value = Read({{address}});
            poke(0x6030, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Read(byte address) => peek(address);
            static void Write(byte address, byte value) => poke(address, value);
            """);
        Assert.Equal(93, cpu.Memory[address]);
        Assert.Equal(93, cpu.Memory[0x6030]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void NestedReadsPreserveTheWriteAddress()
    {
        var cpu = ExecuteProgram("""
            byte destination = peek(0x6020);
            poke((ushort)(destination + 0x60F0),
                peek((ushort)(peek(0x6021) + 0x61F0)));
            test_stop();
            while (true) ;
            static extern void test_stop();
            """, cpu =>
            {
                cpu.Memory[0x6020] = 20;
                cpu.Memory[0x6021] = 33;
                cpu.Memory[0x6211] = 87;
            });
        Assert.Equal(87, cpu.Memory[0x6104]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AddressSurvivesValueCallsAndEarlyReturns(byte branch)
    {
        var cpu = ExecuteProgram("""
            byte destination = peek(0x6020);
            poke((ushort)(destination + 0x60F0), Value());
            poke(0x6040, Value());
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Value()
            {
                poke(0x6041, 55);
                byte branch = peek(0x6021);
                if (branch == 0)
                    return peek(0x6022);
                return peek(0x6023);
            }
            """, cpu =>
            {
                cpu.Memory[0x6020] = 20;
                cpu.Memory[0x6021] = branch;
                cpu.Memory[0x6022] = 19;
                cpu.Memory[0x6023] = 71;
            });
        Assert.Equal(branch == 0 ? 19 : 71, cpu.Memory[0x6104]);
        Assert.Equal(cpu.Memory[0x6104], cpu.Memory[0x6040]);
        Assert.Equal(55, cpu.Memory[0x6041]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void CallerValueSurvivesNestedMemoryHelpers()
    {
        var cpu = ExecuteProgram("""
            byte before = peek(0x6020);
            byte result = Update(3);
            poke(0x6030, before);
            poke(0x6031, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Update(byte index)
            {
                poke((ushort)(index + 0x60F0), Read(index));
                return peek((ushort)(index + 0x60F0));
            }
            static byte Read(byte index) => peek((ushort)(index + 0x61F0));
            """, cpu =>
            {
                cpu.Memory[0x6020] = 38;
                cpu.Memory[0x61F3] = 92;
            });
        Assert.Equal(38, cpu.Memory[0x6030]);
        Assert.Equal(92, cpu.Memory[0x6031]);
        Assert.Equal(92, cpu.Memory[0x60F3]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void ReusedAddressSupportsReadModifyWrite()
    {
        const string source = """
            Update(21);
            Update(21);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Update(byte index)
            {
                ushort address = (ushort)(index + 0x60F0);
                poke(address, (byte)(peek(address) + 1));
            }
            """;
        var cpu = ExecuteProgram(source, cpu => cpu.Memory[0x6105] = 80);
        Assert.Equal(82, cpu.Memory[0x6105]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void ReusedAddressSupportsConsecutiveWrites()
    {
        var cpu = ExecuteProgram("""
            Update(21);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Update(byte index)
            {
                ushort address = (ushort)(index + 0x60F0);
                poke(address, 31);
                poke(address, 32);
            }
            """);
        Assert.Equal(32, cpu.Memory[0x6105]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(16, false)]
    [InlineData(3, true)]
    [InlineData(16, true)]
    public void NestedAddressArithmeticPreservesSavedOperands(byte index, bool dynamicDestination)
    {
        var cpu = ExecuteProgram($$"""
            Copy({{index}});
            Copy({{index}});
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Copy(byte index)
            {
                poke({{(dynamicDestination ? "(ushort)(0x6140 + index)" : "0x6040")}},
                    peek((ushort)(0x60F0 + index)));
            }
            """, cpu =>
            {
                cpu.Memory[0x6000 + index] = 21;
                cpu.Memory[0x60F0 + index] = 75;
            });
        Assert.Equal(75, cpu.Memory[dynamicDestination ? 0x6140 + index : 0x6040]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("NTADR_A", 0x2041)]
    [InlineData("NTADR_B", 0x2441)]
    [InlineData("NTADR_C", 0x2841)]
    [InlineData("NTADR_D", 0x2C41)]
    public void NametableAddressRetainsItsHighByte(string intrinsic, ushort address)
    {
        var cpu = ExecuteProgram($$"""
            byte result = peek({{intrinsic}}(1, 2));
            poke(0x6000, result);
            poke({{intrinsic}}(1, 2), 77);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """, cpu =>
            {
                cpu.Memory[address] = 99;
                cpu.Memory[0x41] = 13;
            });
        Assert.Equal(99, cpu.Memory[0x6000]);
        Assert.Equal(77, cpu.Memory[address]);
        Assert.Equal(13, cpu.Memory[0x41]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedAddressSpillsPreserveSignedOffsets(bool dynamicAddress)
    {
        var cpu = ExecuteProgram($$"""
            Copy(3, -1);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Copy(byte index, sbyte delta)
            {
                ushort address = {{(dynamicAddress ? "(ushort)(0x6000 + index)" : "0x6003")}};
                poke(address, 11);
                ushort next = (ushort)(address + delta);
                poke(next, 22);
            }
            """);
        Assert.Equal(11, cpu.Memory[0x6003]);
        Assert.Equal(22, cpu.Memory[0x6002]);
        Assert.Equal(0, cpu.Memory[0x6102]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0x7F)]
    [InlineData(0xFF)]
    public void SignedByteReadExtendsIntoWordStorage(byte input)
    {
        var cpu = ExecuteProgram("""
            short value = (sbyte)peek(0x6010);
            poke(0x6030, (byte)value);
            byte result = 11;
            if (value < 0)
                result = 22;
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """, cpu => cpu.Memory[0x6010] = input);
        Assert.Equal(input, cpu.Memory[0x6030]);
        Assert.Equal(input > 127 ? 22 : 11, cpu.Memory[0x6000]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteValuePreservesBothReadResults(bool helperCalls)
    {
        var cpu = ExecuteProgram($$"""
            poke(0x6000, (byte)({{(helperCalls ? "ReadLeft() + ReadRight()" : "peek(0x6010) + peek(0x6011)")}}));
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte ReadLeft() => peek(0x6010);
            static byte ReadRight() => peek(0x6011);
            """, cpu =>
            {
                cpu.Memory[0x6010] = 3;
                cpu.Memory[0x6011] = 7;
            });
        Assert.Equal(10, cpu.Memory[0x6000]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(255)]
    public void ConstantWriteAfterHelperReturnUsesItsLiteral(byte input)
    {
        var cpu = ExecuteProgram("""
            byte value = peek(0x6100);
            if (Identity(value) != 0) poke(0x6000, 1);
            if (Identity(value) == 0) poke(0x6001, 1);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Identity(byte value) => value;
            """, cpu => cpu.Memory[0x6100] = input);
        Assert.Equal(input != 0 ? 1 : 0, cpu.Memory[0x6000]);
        Assert.Equal(input == 0 ? 1 : 0, cpu.Memory[0x6001]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedAddressSpillsMaterializePromotedWordOperands(bool subtract)
    {
        var cpu = ExecuteProgram($$"""
            Copy(100, 200);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Copy(byte a, byte b)
            {
                ushort address = 0x6000;
                poke(address, 11);
                ushort next = (ushort)(address + (a {{(subtract ? "-" : "+")}} b));
                poke(next, 22);
            }
            """);
        Assert.Equal(11, cpu.Memory[0x6000]);
        Assert.Equal(22, cpu.Memory[subtract ? 0x5F9C : 0x612C]);
        Assert.Equal(0, cpu.Memory[subtract ? 0x609C : 0x602C]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(255)]
    public void PromotedReadMultiplicationSurvivesAValueCall(byte input)
    {
        var cpu = ExecuteProgram("""
            ushort result = (ushort)(peek(0x6012) * 8 + Next());
            poke(0x6000, (byte)result);
            poke(0x6001, (byte)(result >> 8));
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Next() => 7;
            """, cpu => cpu.Memory[0x6012] = input);
        int expected = input * 8 + 7;
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
        Assert.Equal(0x800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
