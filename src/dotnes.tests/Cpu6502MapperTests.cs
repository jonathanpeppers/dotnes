namespace dotnes.tests;

public class Cpu6502MapperTests
{
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x40)]
    public void MapperStoresSelectPhysicalPrgWithoutWritingRom(byte mode)
    {
        var cpu = new Cpu6502([], 0xE000, 0xE000);
        var bus = new Mmc3PrgBus(cpu);
        byte[] code =
        [
            0xA9, (byte)(mode | 6), 0x8D, 0x00, 0x80, // Select R6 and PRG mode.
            0xA9, 3, 0x8D, 0x01, 0x80,
            0xA9, (byte)(mode | 7), 0x8D, 0x00, 0x80,
            0xA9, 4, 0x8D, 0x01, 0x80,
            0xAD, 0x23, 0x81, 0x8D, 0x00, 0x03,
            0xAD, 0x23, 0xA1, 0x8D, 0x01, 0x03,
            0xAD, 0x23, 0xC1, 0x8D, 0x02, 0x03,
            0xAD, 0x23, 0xE1, 0x8D, 0x03, 0x03,
        ];
        code.CopyTo(bus.Prg, 7 * Mmc3PrgBus.BankSize);
        for (int bank = 0; bank < 8; bank++)
            bus.Prg[bank * Mmc3PrgBus.BankSize + 0x123] = (byte)(0x80 + bank);
        byte[] original = (byte[])bus.Prg.Clone();

        cpu.RunUntil((ushort)(0xE000 + code.Length));

        Assert.Equal(mode == 0 ? 0x83 : 0x86, cpu.Memory[0x300]);
        Assert.Equal(0x84, cpu.Memory[0x301]);
        Assert.Equal(mode == 0 ? 0x86 : 0x83, cpu.Memory[0x302]);
        Assert.Equal(0x87, cpu.Memory[0x303]);
        Assert.Equal(original, bus.Prg);
        Assert.Equal(0, cpu.Memory[0x8000]);
        Assert.Equal(0, cpu.Memory[0x8001]);
        Assert.Equal(new long[] { 6, 12, 18, 24 },
            bus.Accesses.Where(a => a.Write && a.Address >= 0x8000).Select(a => a.Cycle));
    }

    [Theory]
    [InlineData(0x00, 0x8000)]
    [InlineData(0x40, 0xC000)]
    public void NextOpcodeFetchUsesTheNewBank(byte mode, ushort entry)
    {
        var cpu = new Cpu6502([], entry, entry);
        var bus = new Mmc3PrgBus(cpu, mode);
        byte[] oldCode = [0xA9, (byte)(mode | 6), 0x8D, 0, 0x80, 0xA9, 3, 0x8D, 1, 0x80, 0xA9, 0x11];
        oldCode.CopyTo(bus.Prg, 0);
        byte[] newCode = [0xA9, 0x42, 0x8D, 0, 3];
        newCode.CopyTo(bus.Prg, 3 * Mmc3PrgBus.BankSize + 10);
        byte[] original = (byte[])bus.Prg.Clone();

        cpu.RunUntil((ushort)(entry + 15));

        Assert.Equal(0x42, cpu.A);
        Assert.Equal(0x42, cpu.Memory[0x300]);
        Assert.Equal(18, cpu.CycleCount);
        Assert.Equal(original, bus.Prg);
        Assert.Contains(bus.Accesses, a => !a.Write && a.Address == entry + 10 && a.Value == 0xA9 && a.Cycle == 13);
        Assert.Contains(bus.Accesses, a => !a.Write && a.Address == entry + 11 && a.Value == 0x42 && a.Cycle == 14);
    }

    [Fact]
    public void EightCycleSelectorEpilogueCompletesBeforeBoundaryInterrupt()
    {
        var cpu = new Cpu6502([], 0xE000, 0xE000);
        var bus = new Mmc3PrgBus(cpu);
        byte[] code = [0xAD, 0x00, 0x03, 0x8D, 0x00, 0x80, 0xEA];
        code.CopyTo(bus.Prg, 7 * Mmc3PrgBus.BankSize);
        cpu.Memory[0x300] = 0x46;
        bus.Prg[7 * Mmc3PrgBus.BankSize + 0x100] = 0x40; // RTI
        bus.Prg[^6] = 0x00;
        bus.Prg[^5] = 0xE1;
        var boundaries = new List<(ushort PC, long Cycle)>();
        cpu.BeforeInstruction = c =>
        {
            boundaries.Add((c.PC, c.CycleCount));
            if (c.PC == 0xE006 && c.InterruptCount == 0)
            {
                Assert.Equal(0x46, bus.Select);
                Assert.Equal(8, c.CycleCount);
                c.Nmi();
            }
        };

        cpu.RunUntil(0xE007);

        Assert.Equal(new (ushort, long)[] { (0xE000, 0), (0xE003, 4), (0xE006, 8), (0xE006, 21) }, boundaries);
        Assert.Equal(8, Assert.Single(bus.Accesses, a => a.Write && a.Address == 0x8000).Cycle);
        Assert.Equal(4, cpu.InstructionCount); // LDA, STA, RTI, NOP, not interrupt entry.
        Assert.Equal(1, cpu.InterruptCount);
        Assert.Equal(23, cpu.CycleCount);
        Assert.Equal(0xFF, cpu.SP);
        Assert.Equal(0x46, cpu.A);
        Assert.False(cpu.InterruptDisable);
    }

    [Fact]
    public void InterruptBusOrderAndCyclesPreserveRegistersFlagsAndStacks()
    {
        var cpu = new Cpu6502([], 0xE000, 0xE000);
        var bus = new Mmc3PrgBus(cpu);
        byte[] setup = [0xF8, 0x38, 0xA9, 0x80, 0x69, 0x80, 0xA2, 0x31, 0xA0, 0x42];
        setup.CopyTo(bus.Prg, 7 * Mmc3PrgBus.BankSize);
        byte[] handler = [0x48, 0x8A, 0x48, 0x98, 0x48, 0xA9, 0, 0xAA, 0xA8, 0x68, 0xA8, 0x68, 0xAA, 0x68, 0x40];
        handler.CopyTo(bus.Prg, 7 * Mmc3PrgBus.BankSize + 0x100);
        bus.Prg[^6] = 0;
        bus.Prg[^5] = 0xE1;
        cpu.RunUntil(0xE00A);
        var saved = (cpu.A, cpu.X, cpu.Y, cpu.Status, cpu.SP, cpu.SoftwareStackPointer);
        long start = cpu.CycleCount;
        int instructions = cpu.InstructionCount;
        bus.Accesses.Clear();

        cpu.Nmi();

        Assert.Equal(new Access[]
        {
            new(true, 0x1FF, 0xE0, start + 3),
            new(true, 0x1FE, 0x0A, start + 4),
            new(true, 0x1FD, saved.Status, start + 5),
            new(false, 0xFFFA, 0x00, start + 6),
            new(false, 0xFFFB, 0xE1, start + 7),
        }, bus.Accesses);
        Assert.True(cpu.InterruptDisable);
        Assert.Equal(start + 7, cpu.CycleCount);
        Assert.Equal(instructions, cpu.InstructionCount);
        Assert.False(cpu.Irq());
        Assert.Equal(1, cpu.InterruptCount);
        Assert.Equal(start + 7, cpu.CycleCount);

        cpu.RunUntil(0xE00A);

        Assert.Equal(saved, (cpu.A, cpu.X, cpu.Y, cpu.Status, cpu.SP, cpu.SoftwareStackPointer));
        Assert.Equal(start + 48, cpu.CycleCount);
        Assert.Equal(instructions + 14, cpu.InstructionCount);
        Assert.Equal(0, cpu.SoftwareStackWrites);
        Assert.Equal(0, cpu.SoftwareStackPointerWrites);
        bus.Prg[^2] = 0;
        bus.Prg[^1] = 0xE1;
        Assert.True(cpu.Irq());
        Assert.Equal(2, cpu.InterruptCount);
        Assert.Equal(start + 55, cpu.CycleCount);
        cpu.RunUntil(0xE00A);
        Assert.Equal(saved, (cpu.A, cpu.X, cpu.Y, cpu.Status, cpu.SP, cpu.SoftwareStackPointer));
    }

    [Fact]
    public void SubroutineBusOrderIncludesStackAndLateHighOperandFetch()
    {
        var cpu = new Cpu6502([], 0xE000, 0xE000);
        var bus = new Mmc3PrgBus(cpu);
        byte[] code = [0x20, 0x00, 0xE1];
        code.CopyTo(bus.Prg, 7 * Mmc3PrgBus.BankSize);
        bus.Prg[7 * Mmc3PrgBus.BankSize + 0x100] = 0x60;

        cpu.Step();

        Assert.Equal(0xE100, cpu.PC);
        Assert.Equal(6, cpu.CycleCount);
        Assert.Equal(new Access[]
        {
            new(false, 0xE000, 0x20, 1),
            new(false, 0xE001, 0x00, 2),
            new(true, 0x1FF, 0xE0, 4),
            new(true, 0x1FE, 0x02, 5),
            new(false, 0xE002, 0xE1, 6),
        }, bus.Accesses);
        bus.Accesses.Clear();
        cpu.Step();
        Assert.Equal(new Access[]
        {
            new(false, 0xE100, 0x60, 7),
            new(false, 0x1FE, 0x02, 10),
            new(false, 0x1FF, 0xE0, 11),
        }, bus.Accesses);
        Assert.Equal(12, cpu.CycleCount);
        Assert.Equal(0xE003, cpu.PC);
        Assert.Equal(0xFF, cpu.SP);
    }

    [Theory]
    [InlineData("A900", 2)]
    [InlineData("A512", 3)]
    [InlineData("B512", 4)]
    [InlineData("B612", 4)]
    [InlineData("AD0012", 4)]
    [InlineData("BD0012", 4)]
    [InlineData("BDFF12", 5)]
    [InlineData("B90012", 4)]
    [InlineData("B9FF12", 5)]
    [InlineData("BEFF12", 5)]
    [InlineData("BCFF12", 5)]
    [InlineData("A10F", 6)]
    [InlineData("B110", 6)]
    [InlineData("B112", 5)]
    [InlineData("7DFF12", 5)]
    [InlineData("F9FF12", 5)]
    [InlineData("3DFF12", 5)]
    [InlineData("19FF12", 5)]
    [InlineData("5110", 6)]
    [InlineData("DDFF12", 5)]
    [InlineData("8D0012", 4)]
    [InlineData("9D0012", 5)]
    [InlineData("9DFF12", 5)]
    [InlineData("99FF12", 5)]
    [InlineData("9110", 6)]
    [InlineData("9112", 6)]
    [InlineData("810F", 6)]
    [InlineData("9512", 4)]
    [InlineData("9612", 4)]
    [InlineData("E612", 5)]
    [InlineData("F612", 6)]
    [InlineData("CE0012", 6)]
    [InlineData("DE0012", 7)]
    [InlineData("DEFF12", 7)]
    [InlineData("0A", 2)]
    [InlineData("4612", 5)]
    [InlineData("3612", 6)]
    [InlineData("6E0012", 6)]
    [InlineData("1EFF12", 7)]
    [InlineData("48", 3)]
    [InlineData("08", 3)]
    [InlineData("68", 4)]
    [InlineData("28", 4)]
    [InlineData("EA", 2)]
    [InlineData("4C0012", 3)]
    public void InstructionCyclesIncludeIndexedReadButNotStorePagePenalties(string hex, long cycles)
    {
        var cpu = new Cpu6502(Convert.FromHexString("A201A001" + hex), 0x8000, 0x8000);
        cpu.Memory[0x10] = 0xFF;
        cpu.Memory[0x11] = 0x12;
        cpu.Memory[0x12] = 0;
        cpu.Memory[0x13] = 0x12;
        cpu.Step();
        cpu.Step();
        long start = cpu.CycleCount;

        cpu.Step();

        Assert.Equal(cycles, cpu.CycleCount - start);
    }

    [Theory]
    [InlineData("8D0013", 4, 0x5A)]
    [InlineData("9DFF12", 5, 0x5A)]
    [InlineData("99FF12", 5, 0x5A)]
    [InlineData("9110", 6, 0x5A)]
    [InlineData("810F", 6, 0x5A)]
    [InlineData("8E0013", 4, 0x01)]
    [InlineData("8C0013", 4, 0x01)]
    [InlineData("EE0013", 6, 0x02)]
    [InlineData("FEFF12", 7, 0x02)]
    public void BusStoresCompleteOnTheFinalCycle(string hex, long cycles, byte value)
    {
        var cpu = new Cpu6502(Convert.FromHexString("A95AA201A001" + hex), 0x8000, 0x8000);
        cpu.Memory[0x10] = hex == "810F" ? (byte)0 : (byte)0xFF;
        cpu.Memory[0x11] = hex == "810F" ? (byte)0x13 : (byte)0x12;
        cpu.Memory[0x1300] = 1;
        var accesses = new List<Access>();
        cpu.ReadBus = address =>
        {
            accesses.Add(new(false, address, cpu.Memory[address], cpu.CycleCount));
            return cpu.Memory[address];
        };
        cpu.WriteBus = (address, data) =>
        {
            accesses.Add(new(true, address, data, cpu.CycleCount));
            cpu.Memory[address] = data;
        };
        cpu.RunUntil(0x8006);
        long start = cpu.CycleCount;
        accesses.Clear();

        cpu.Step();

        Assert.Equal(new Access(true, 0x1300, value, start + cycles), Assert.Single(accesses, a => a.Write));
        Assert.Equal(value, cpu.Memory[0x1300]);
        Assert.Equal(start + cycles, cpu.CycleCount);
        if (hex is "EE0013" or "FEFF12")
        {
            Assert.Equal(start + cycles - 2, Assert.Single(accesses, a => !a.Write && a.Address == 0x1300).Cycle);
            Assert.Single(cpu.WrittenAddresses); // The RMW dummy write is deliberately not modeled.
        }
    }

    [Theory]
    [InlineData("A201A1FE", 6)]
    [InlineData("A001B1FF", 5)]
    public void ZeroPagePointerHighByteWrapsThroughBus(string hex, long cycles)
    {
        var cpu = new Cpu6502(Convert.FromHexString(hex), 0x8000, 0x8000);
        cpu.Memory[0xFF] = 0;
        cpu.Memory[0] = 0x12;
        cpu.Memory[0x100] = 0x13;
        cpu.Memory[0x1200] = 0x41;
        cpu.Memory[0x1201] = 0x41;
        var reads = new List<ushort>();
        cpu.ReadBus = address =>
        {
            reads.Add(address);
            return cpu.Memory[address];
        };
        cpu.Step();
        long start = cpu.CycleCount;
        reads.Clear();

        cpu.Step();

        Assert.Equal(0x41, cpu.A);
        Assert.Equal(start + cycles, cpu.CycleCount);
        Assert.Equal(new ushort[] { 0x8002, 0x8003, 0xFF, 0, hex == "A201A1FE" ? (ushort)0x1200 : (ushort)0x1201 }, reads);
    }

    [Theory]
    [InlineData(0x8000, 0xD0, 0x02, 0x8004, 3)] // Taken, same page.
    [InlineData(0x80FC, 0xD0, 0x02, 0x8100, 4)] // Taken, forward page crossing.
    [InlineData(0x8100, 0xD0, 0xFC, 0x80FE, 4)] // Taken, backward page crossing.
    [InlineData(0x80FE, 0xD0, 0x00, 0x8100, 3)] // Compare pages after reading displacement.
    [InlineData(0x80FC, 0xF0, 0x02, 0x80FE, 2)] // Not taken, would cross.
    [InlineData(0xFFFC, 0xD0, 0x02, 0x0000, 4)] // Address wrap is also a page crossing.
    public void BranchCyclesAndOperandBusReads(ushort entry, byte opcode, byte offset, ushort target, long cycles)
    {
        var cpu = new Cpu6502([opcode, offset], entry, entry);
        var reads = new List<ushort>();
        cpu.ReadBus = address =>
        {
            reads.Add(address);
            return cpu.Memory[address];
        };

        cpu.Step();

        Assert.Equal(target, cpu.PC);
        Assert.Equal(cycles, cpu.CycleCount);
        Assert.Equal(new ushort[] { entry, (ushort)(entry + 1) }, reads);
    }

    [Fact]
    public void IndirectReadsWrapThroughBusAndBitReadsItsOperandOnlyOnce()
    {
        var cpu = new Cpu6502(Convert.FromHexString("6CFF12"), 0x8000, 0x8000);
        var reads = new List<ushort>();
        cpu.Memory[0x12FF] = 0;
        cpu.Memory[0x1200] = 0x90;
        cpu.Memory[0x1300] = 0xA0;
        cpu.Memory[0x9000] = 0x24;
        cpu.Memory[0x9001] = 0x20;
        cpu.Memory[0x20] = 0xC0;
        cpu.ReadBus = address =>
        {
            reads.Add(address);
            return cpu.Memory[address];
        };

        cpu.Step();
        Assert.Equal(0x9000, cpu.PC);
        Assert.Equal(5, cpu.CycleCount);
        Assert.Equal(new ushort[] { 0x8000, 0x8001, 0x8002, 0x12FF, 0x1200 }, reads);
        reads.Clear();
        cpu.Step();
        Assert.Equal(new ushort[] { 0x9000, 0x9001, 0x20 }, reads);
        Assert.True(cpu.Zero);
        Assert.True(cpu.Overflow);
        Assert.True(cpu.Negative);
        Assert.Equal(8, cpu.CycleCount);
    }

    [Fact]
    public void BusPreservesBackingRamAndSoftwareStackAccountingWithoutSyntheticReads()
    {
        byte[] code = Convert.FromHexString("A9F08522A9078523A9AA8DFF07A9BB4868");
        var flat = new Cpu6502(code, 0x8000, 0x8000);
        var mapped = new Cpu6502(code, 0x8000, 0x8000);
        var reads = new List<ushort>();
        mapped.ReadBus = address =>
        {
            reads.Add(address);
            return mapped.Memory[address];
        };
        mapped.WriteBus = (address, value) => mapped.Memory[address] = value;
        flat.RunUntil((ushort)(0x8000 + code.Length));
        mapped.RunUntil((ushort)(0x8000 + code.Length));

        Assert.Equal(flat.Memory, mapped.Memory);
        Assert.Equal(flat.WrittenAddresses, mapped.WrittenAddresses);
        Assert.Equal(new ushort[] { 0x22, 0x23, 0x7FF, 0x1FF }, mapped.WrittenAddresses);
        Assert.Equal(2, mapped.SoftwareStackPointerWrites);
        Assert.Equal(1, mapped.SoftwareStackWrites);
        Assert.Equal(0x7F0, mapped.SoftwareStackPointer);
        Assert.DoesNotContain((ushort)0x22, reads);
        Assert.DoesNotContain((ushort)0x23, reads);
        Assert.Equal(flat.CycleCount, mapped.CycleCount);
        Assert.Equal(flat.InstructionCount, mapped.InstructionCount);
    }

    readonly record struct Access(bool Write, ushort Address, byte Value, long Cycle);

    // Only MMC3 PRG windows/registers are modeled: no CHR, IRQ counter, PPU, DMA,
    // open bus, mirroring, or device timing. CPU.Memory remains the RAM backing.
    sealed class Mmc3PrgBus
    {
        public const int BankSize = 0x2000;
        readonly Cpu6502 cpu;
        readonly byte[] registers = [0, 0, 0, 0, 0, 0, 0, 1];
        public byte[] Prg { get; } = new byte[8 * BankSize];
        public byte Select { get; private set; }
        public List<Access> Accesses { get; } = [];

        public Mmc3PrgBus(Cpu6502 cpu, byte select = 0)
        {
            this.cpu = cpu;
            Select = select;
            cpu.ReadBus = Read;
            cpu.WriteBus = Write;
        }

        byte Read(ushort address)
        {
            byte value;
            if (address < 0x8000)
                value = cpu.Memory[address];
            else
            {
                bool mode1 = (Select & 0x40) != 0;
                int bank = ((address - 0x8000) / BankSize) switch
                {
                    0 => mode1 ? 6 : registers[6],
                    1 => registers[7],
                    2 => mode1 ? registers[6] : 6,
                    _ => 7,
                };
                value = Prg[(bank % 8) * BankSize + address % BankSize];
            }
            Accesses.Add(new(false, address, value, cpu.CycleCount));
            return value;
        }

        void Write(ushort address, byte value)
        {
            Accesses.Add(new(true, address, value, cpu.CycleCount));
            if (address < 0x8000)
                cpu.Memory[address] = value;
            else if (address < 0xA000)
            {
                if ((address & 1) == 0)
                    Select = value;
                else
                    registers[Select & 7] = (byte)(value & 0x3F);
            }
        }
    }
}
