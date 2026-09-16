using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ManagedBankExecutionTests(ITestOutputHelper output) : RoslynTests(output)
{
    static CompilationOptions Options() => new()
    {
        Mapper = 4, PrgBanks = 3, Mmc3BankedLayout = true, Mmc3ManagedHomeBank = 0,
        Mmc3ManagedInterruptContract = Mmc3ManagedInterruptContract.NonNestingChrCallbacks,
        ManagedCodeBanks = { new() { Name = "audio", Bank = 2, Size = 0x1000 } },
    };

    BankedCompilation Compile(byte mode, bool optimize = false, bool compact = false, bool bankedSelectorWrites = false)
    {
        string padding = "";
        if (compact)
        {
            var fast = Compile(mode, optimize, bankedSelectorWrites: bankedSelectorWrites);
            int capacity = Mmc3BankLayout.ResetStubAddress - Mmc3BankLayout.FixedProgramAddress;
            padding = $".segment \"RODATA\"\npadding:\n.res {capacity - fast.FixedProgram.TotalSize + 26}, 0";
        }
        string source = $$"""
            ppu_use_native_renderer();
            unsafe { nmi_set_callback(&NativeNmi); irq_set_callback(&NativeIrq); }
            mmc3_set_chr_bank({{mode | 3}}, 7);
            Audio.Reset();
            byte input = peek(0x6200);
            byte result = Audio.Tick(input, 240);
            poke(0x6000, result);
            poke(0x6001, input);
            ushort word = Audio.Word();
            poke(0x6002, (byte)word);
            poke(0x6003, (byte)(word >> 8));
            poke(0x6004, peek(0x9000));
            poke(0x600F, 0xA5);
            while (true) ;
            static extern void NativeNmi();
            static extern void NativeIrq();
            [NESCodeBank("audio")]
            static class Audio
            {
                static byte count;
                public static void Reset() { count = 7; }
                public static byte Tick(byte input, byte next)
                {
                    {{(bankedSelectorWrites ? $"mmc3_set_chr_bank({mode | 2}, 8);" : "")}}
                    count = (byte)(count + input);
                    byte note = peek(0x9003);
                    return (byte)(Add(next, note) + count);
                }
                static byte Add(byte left, byte right) => (byte)(left + right);
                public static ushort Word()
                {
                    byte low = peek(0x9004);
                    byte high = peek(0x9005);
                    return (ushort)(low | (high << 8));
                }
            }
            """;
        var assembly = CompileAssembly(source, allowUnsafe: true);
        using var native = new AssemblyReader(new StringReader($"""
            _NativeNmi:
                lda #{mode | 5}
                sta $8000
                lda #$33
                sta $8001
                ldx #$AA
                ldy #$BB
                sec
                rts
            _NativeIrq:
                lda #{mode | 4}
                sta $8000
                lda #$44
                sta $8001
                ldx #$CC
                ldy #$DD
                sec
                rts
            {padding}
            """));
        var options = Options();
        options.OptimizeByteHelpers = optimize;
        return NesCompiler.CompileBanked(assembly, options, [native], _logger);
    }

    public static TheoryData<bool, byte, bool, bool, bool> InterruptGateCases
    {
        get
        {
            var cases = new TheoryData<bool, byte, bool, bool, bool>();
            foreach (bool irq in new[] { false, true })
            foreach (byte mode in new byte[] { 0, 0x80 })
            foreach (bool optimize in new[] { false, true })
            foreach (bool compact in new[] { false, true })
            foreach (bool writes in new[] { false, true })
                cases.Add(irq, mode, optimize, compact, writes);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(InterruptGateCases))]
    public void EveryGateAndForegroundPublicationBoundarySurvivesStockInterrupt(
        bool irq, byte mode, bool optimize, bool compact, bool bankedSelectorWrites)
    {
        var result = Compile(mode, optimize, compact, bankedSelectorWrites);
        Assert.Equal(compact, result.FixedProgram.GetBlock("__nesbank_enter") != null);
        var fixedProgram = result.FixedProgram;
        var targets = new HashSet<ushort>();
        foreach (var block in fixedProgram.Blocks.Where(block => block.Label is not null &&
            (block.Label.StartsWith("__nesbank_gate_", StringComparison.Ordinal) ||
                block.Label is "__nesbank_enter" or "__nesbank_leave")))
            for (int index = 0; index < block.Count; index++)
                targets.Add(fixedProgram.GetInstructionAddress(block, index));

        var main = fixedProgram.GetBlock("main")!;
        ushort shadow = fixedProgram.GetLabels()["__nesbank_selector"];
        for (int index = 0; index < main.Count; index++)
            if (main[index].Operand is AbsoluteOperand address &&
                address.Address is NESLib.MMC3_BANK_SELECT or NESLib.MMC3_BANK_DATA ||
                main[index].Operand is AbsoluteOperand { Address: var value } && value == shadow)
                targets.Add(fixedProgram.GetInstructionAddress(main, index));

        var baseline = new BankMachine(result, mode);
        int boundaries = 0;
        baseline.Cpu.BeforeInstruction = cpu => { if (targets.Contains(cpu.PC)) boundaries++; };
        baseline.Run();
        Assert.True(boundaries > 50);
        AssertResult(baseline, mode);

        for (int boundary = 0; boundary < boundaries; boundary++)
        {
            var machine = new BankMachine(result, mode);
            int visited = 0;
            bool injected = false, restored = false;
            (ushort PC, byte A, byte X, byte Y, byte SP, byte Status, ushort SoftwareSP) saved = default;
            machine.Cpu.BeforeInstruction = cpu =>
            {
                if (injected)
                {
                    if (!restored && cpu.PC == saved.PC && cpu.SP == saved.SP)
                    {
                        Assert.Equal(saved, State(cpu));
                        restored = true;
                    }
                    return;
                }
                if (!targets.Contains(cpu.PC) || visited++ != boundary)
                    return;
                saved = State(cpu);
                injected = true;
                if (irq)
                    Assert.True(cpu.Irq());
                else
                    cpu.Nmi();
            };
            machine.Run();
            Assert.True(injected && restored, $"Interrupt did not resume boundary {boundary}.");
            Assert.Equal(1, machine.Cpu.InterruptCount);
            Assert.Equal(irq ? (byte)0x44 : (byte)0x33, machine.Chr[irq ? 4 : 5]);
            if (bankedSelectorWrites)
                Assert.Equal(8, machine.Chr[2]);
            AssertResult(machine, mode);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(127, false)]
    [InlineData(128, false)]
    [InlineData(255, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(127, true)]
    [InlineData(128, true)]
    [InlineData(255, true)]
    public void OrdinaryByteArgumentsAndWordReturnsKeepTheirAbi(byte input, bool optimize)
    {
        var machine = new BankMachine(Compile(0, optimize), 0);
        machine.Cpu.Memory[0x6200] = input;
        machine.Run();
        Assert.Equal(unchecked((byte)(240 + 0x21 + 7 + input)), machine.Cpu.Memory[0x6000]);
        Assert.Equal(input, machine.Cpu.Memory[0x6001]);
        Assert.Equal(new byte[] { 0xAB, 3, 0x6B }, machine.Cpu.Memory[0x6002..0x6005]);
        Assert.Equal(0x0800, machine.Cpu.SoftwareStackPointer);
        Assert.Equal(0xFF, machine.Cpu.SP);
    }

    [Fact]
    public void MapperBookkeepingPreservesEntryAndReturnRegistersAndFlags()
    {
        var result = Compile(0);
        var program = result.FixedProgram;
        var entries = new Dictionary<ushort, ushort>();
        var returns = new HashSet<ushort>();
        var exits = new HashSet<ushort>();
        foreach (var block in program.Blocks.Where(block =>
            block.Label?.StartsWith("__nesbank_gate_", StringComparison.Ordinal) == true))
        {
            int call = Enumerable.Range(0, block.Count).Single(index =>
                block[index].Opcode == Opcode.JSR && block[index].Operand is LabelOperand label &&
                label.Label.StartsWith("__nesbank_method_", StringComparison.Ordinal));
            var target = (LabelOperand)block[call].Operand!;
            entries.Add(program.GetInstructionAddress(block, 0), program.GetLabels()[target.Label]);
            returns.Add(program.GetInstructionAddress(block, call + 1));
        }
        var leave = program.GetBlock("__nesbank_leave")!;
        exits.Add(program.GetInstructionAddress(leave, leave.Count - 1));
        var machine = new BankMachine(result, 0);
        (byte A, byte X, byte Y, byte Status, ushort SoftwareSP) entry = default, returned = default;
        ushort targetPc = 0;
        int entered = 0, returnedCount = 0;
        machine.Cpu.BeforeInstruction = cpu =>
        {
            var registers = (cpu.A, cpu.X, cpu.Y, cpu.Status, cpu.SoftwareStackPointer);
            if (entries.TryGetValue(cpu.PC, out ushort target))
            {
                entry = registers;
                targetPc = target;
            }
            else if (cpu.PC == targetPc)
            {
                Assert.Equal(entry, registers);
                targetPc = 0;
                entered++;
            }
            else if (returns.Contains(cpu.PC))
                returned = registers;
            else if (exits.Contains(cpu.PC))
            {
                Assert.Equal(returned, registers);
                returnedCount++;
            }
        };
        machine.Run();
        Assert.Equal(3, entered);
        Assert.Equal(entered, returnedCount);
        AssertResult(machine, 0);
    }

    [Theory]
    [InlineData(false, false, 87)]
    [InlineData(true, false, 106)]
    [InlineData(false, true, 103)]
    [InlineData(true, true, 122)]
    public void InlineEntryAndSharedReturnBoundGateOverhead(bool compact, bool bankedSelectorWrites, int expectedCycles)
    {
        var result = Compile(0, compact: compact, bankedSelectorWrites: bankedSelectorWrites);
        var program = result.FixedProgram;
        Assert.Equal(compact, program.GetBlock("__nesbank_enter") != null);
        var labels = program.GetLabels();
        var gates = new Dictionary<ushort, (ushort Method, ushort Return)>();
        foreach (var block in program.Blocks.Where(block =>
            block.Label?.StartsWith("__nesbank_gate_", StringComparison.Ordinal) == true))
        {
            int call = Enumerable.Range(0, block.Count).Single(index =>
                block[index].Opcode == Opcode.JSR && block[index].Operand is LabelOperand label &&
                label.Label.StartsWith("__nesbank_method_", StringComparison.Ordinal));
            gates.Add(labels[block.Label!], (labels[((LabelOperand)block[call].Operand!).Label],
                program.GetInstructionAddress(block, call + 1)));
        }
        var leave = program.GetBlock("__nesbank_leave")!;
        ushort exit = program.GetInstructionAddress(leave, leave.Count - 1);
        var machine = new BankMachine(result, 0);
        (ushort Method, ushort Return) current = default;
        long started = 0, bodyStarted = 0, bodyCycles = 0;
        int measured = 0;
        machine.Cpu.BeforeInstruction = cpu =>
        {
            if (gates.TryGetValue(cpu.PC, out var gate))
            {
                current = gate;
                started = cpu.CycleCount;
            }
            else if (cpu.PC == current.Method)
                bodyStarted = cpu.CycleCount;
            else if (cpu.PC == current.Return)
                bodyCycles = cpu.CycleCount - bodyStarted;
            else if (cpu.PC == exit)
            {
                Assert.Equal(expectedCycles, cpu.CycleCount - started - bodyCycles + 6);
                measured++;
            }
        };
        machine.Run();
        Assert.Equal(3, measured);
        AssertResult(machine, 0);
    }

    [Fact]
    public void StockDispatchersOnlyAddEightCyclesAfterCallbacks()
    {
        var result = Compile(0);
        foreach (string name in new[] { "skipNtsc", "irq_with_callback" })
        {
            var block = result.FixedProgram.GetBlock(name)!;
            var original = name == "skipNtsc" ? BuiltInSubroutines.SkipNtsc() : BuiltInSubroutines.IrqWithCallback();
            int jsr = Enumerable.Range(0, original.Count).Single(i => original[i].Opcode == Opcode.JSR);
            Assert.Equal(original.InstructionsWithLabels.Take(jsr + 1), block.InstructionsWithLabels.Take(jsr + 1));
            Assert.Equal(Opcode.LDA, block[jsr + 1].Opcode);
            Assert.Equal(AddressMode.Absolute, block[jsr + 1].Mode);
            Assert.Equal(Opcode.STA, block[jsr + 2].Opcode);
            var epilogue = new Program6502();
            epilogue.AddBlock(new Block().Emit(block[jsr + 1]).Emit(block[jsr + 2]));
            var cpu = new Cpu6502(epilogue.ToBytes(), epilogue.BaseAddress, epilogue.BaseAddress);
            cpu.Step();
            cpu.Step();
            Assert.Equal(8, cpu.CycleCount);
            Assert.Equal(original.InstructionsWithLabels.Skip(jsr + 1), block.InstructionsWithLabels.Skip(jsr + 3));
        }
    }

    [Theory]
    [InlineData(0, 0x81, 0, 0xFF, 0xFF, 6)]
    [InlineData(1, 0x7F, 1, 0x34, 0x12, 1)]
    [InlineData(0x80, 0xFF, 1, 0x34, 0x12, 3)]
    public void SignedBooleanAndEarlyWordReturnsCrossGates(
        byte condition, byte signed, byte expectedBool, byte low, byte high, byte bits)
    {
        string source = """
            byte flag = peek(0x6200);
            sbyte signed = (sbyte)peek(0x6201);
            poke(0x6000, Audio.Check(flag != 0) ? (byte)1 : (byte)0);
            poke(0x6001, (byte)Audio.Signed(signed));
            short word = Audio.Word(flag != 0);
            poke(0x6002, (byte)word);
            poke(0x6003, (byte)(word >> 8));
            poke(0x6004, Audio.Bits(flag != 0, signed < 0, flag == 0));
            ushort widened = Audio.Widen(signed, -1);
            poke(0x6005, (byte)widened);
            poke(0x6006, (byte)(widened >> 8));
            poke(0x6007, Audio.Match(flag != 0, signed < 0) ? (byte)1 : (byte)0);
            poke(0x600F, 0xA5);
            while (true) ;
            [NESCodeBank("audio")]
            static class Audio
            {
                public static bool Check(bool value) { return value; }
                public static sbyte Signed(sbyte value) { return value; }
                public static short Word(bool value)
                {
                    if (!value) return -1;
                    return 0x1234;
                }
                public static byte Bits(bool first, bool second, bool third) => Pack(first, second, third);
                static byte Pack(bool first, bool second, bool third)
                {
                    byte result = 0;
                    if (first) result |= 1;
                    if (second) result |= 2;
                    if (third) result |= 4;
                    return result;
                }
                public static ushort Widen(sbyte first, sbyte second) => Combine(first, second);
                static ushort Combine(sbyte first, sbyte second) => (ushort)((byte)first | ((byte)second << 8));
                public static bool Match(bool first, bool second) => Both(first, second);
                static bool Both(bool first, bool second) => first && second;
            }
            """;
        using var assembly = CompileAssembly(source);
        var result = NesCompiler.CompileBanked(assembly, Options(), logger: _logger);
        var program = result.FixedProgram;
        var labels = program.GetLabels();
        var bank = result.Regions[0].Program.ToBytes();
        byte mapping = 0, selector = 6;
        var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, labels["main"]);
        cpu.Memory[labels["__nesbank_selector"]] = 6;
        cpu.Memory[0x6200] = condition;
        cpu.Memory[0x6201] = signed;
        cpu.ReadBus = address => address is >= 0x8000 and < 0xA000
            ? mapping == 2 && address - 0x8000 < bank.Length ? bank[address - 0x8000] : (byte)0
            : cpu.Memory[address];
        cpu.WriteBus = (address, value) =>
        {
            if (address == 0x8000) selector = value;
            else if (address == 0x8001)
            {
                Assert.Equal(6, selector);
                mapping = value;
            }
            else if (address < 0x8000) cpu.Memory[address] = value;
        };
        for (int step = 0; step < 10000 && cpu.Memory[0x600F] != 0xA5; step++)
            cpu.Step();
        Assert.Equal(0xA5, cpu.Memory[0x600F]);
        Assert.Equal(new byte[] { expectedBool, signed, low, high, bits, signed, 0xFF,
            (byte)(expectedBool != 0 && signed >= 0x80 ? 1 : 0) }, cpu.Memory[0x6000..0x6008]);
        Assert.Equal(0, mapping);
        Assert.Equal(6, selector);
        Assert.Equal(0x0800, cpu.SoftwareStackPointer);
        Assert.Equal(0xFF, cpu.SP);
    }

    static (ushort PC, byte A, byte X, byte Y, byte SP, byte Status, ushort SoftwareSP) State(Cpu6502 cpu) =>
        (cpu.PC, cpu.A, cpu.X, cpu.Y, cpu.SP, cpu.Status, cpu.SoftwareStackPointer);

    static void AssertResult(BankMachine machine, byte mode)
    {
        Assert.Equal(new byte[] { 0x1B, 3, 0xAB, 3, 0x6B }, machine.Cpu.Memory[0x6000..0x6005]);
        Assert.Equal(mode | 3, machine.Selector);
        Assert.Equal(0, machine.Bank);
        Assert.Equal(7, machine.Chr[3]);
        Assert.Equal(0x0800, machine.Cpu.SoftwareStackPointer);
        Assert.Equal(0xFF, machine.Cpu.SP);
    }

    internal sealed class BankMachine
    {
        readonly byte[] home = Enumerable.Repeat((byte)0x6B, 0x2000).ToArray();
        readonly byte[] banked = new byte[0x2000];
        public Cpu6502 Cpu { get; }
        public byte Selector { get; private set; }
        public byte Bank { get; private set; }
        public byte R7Bank { get; private set; }
        public byte[] Chr { get; } = new byte[6];

        public BankMachine(BankedCompilation result, byte mode)
        {
            var program = result.FixedProgram;
            var labels = program.GetLabels();
            result.Regions[0].Program.ToBytes().CopyTo(banked, result.Regions[0].Placement.Offset);
            banked[0x1003] = 0x21;
            banked[0x1004] = 0xAB;
            banked[0x1005] = 3;
            var r7Images = new Dictionary<int, byte[]>();
            foreach (var asset in result.PrgAssets)
            {
                Assert.Equal(0xA000, asset.Placement.CpuAddress);
                if (!r7Images.TryGetValue(asset.Placement.Bank, out var image))
                    r7Images.Add(asset.Placement.Bank, image = new byte[0x2000]);
                (asset.Program?.ToBytes() ?? asset.Data!).CopyTo(image, asset.Placement.Offset);
            }
            Cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, labels["main"]);
            Cpu.Memory[labels["__nesbank_selector"]] = Selector = (byte)(mode | 6);
            Cpu.Memory[0x6200] = 3;
            Cpu.Memory[0x14] = 0x4C;
            Word(0xFFFA, labels["_nmi"]);
            Word(0xFFFE, labels["irq_with_callback"]);
            Cpu.ReadBus = address => address is >= 0x8000 and < 0xA000
                ? (Bank == 0 ? home : banked)[address - 0x8000]
                : address is >= 0xA000 and < 0xC000 && r7Images.TryGetValue(R7Bank, out var image)
                    ? image[address - 0xA000] : Cpu.Memory[address];
            Cpu.WriteBus = (address, value) =>
            {
                if (address == NESLib.MMC3_BANK_SELECT)
                {
                    Assert.Equal(0, value & 0x40);
                    Selector = value;
                }
                else if (address == NESLib.MMC3_BANK_DATA)
                {
                    if ((Selector & 7) == 6)
                    {
                        Assert.True(value is 0 or 2);
                        Bank = value;
                    }
                    else if ((Selector & 7) == 7)
                        R7Bank = value;
                    else
                    {
                        Assert.InRange(Selector & 7, 0, 5);
                        Chr[Selector & 7] = value;
                    }
                }
                else if (address < 0x8000)
                    Cpu.Memory[address] = value;
            };
        }

        void Word(ushort address, ushort value)
        {
            Cpu.Memory[address] = (byte)value;
            Cpu.Memory[address + 1] = (byte)(value >> 8);
        }

        public void Run()
        {
            for (int steps = 0; steps < 10000; steps++)
            {
                if (Cpu.Memory[0x600F] == 0xA5)
                    return;
                Cpu.Step();
            }
            Assert.Fail($"Banked program did not finish at ${Cpu.PC:X4}.");
        }
    }
}
