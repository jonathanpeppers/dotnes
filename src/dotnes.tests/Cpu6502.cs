using dotnes.ObjectModel;

namespace dotnes.tests;

// Asset-free instruction execution with cycle totals, not a NES/device emulator.
// Bus accesses retain program order and stores occur on their completion cycle.
// Dummy reads and RMW dummy writes are omitted; PPU/APU/DMA and IRQ polling delays
// are not modeled. Do not use the bus callbacks as a complete hardware bus trace.
internal sealed class Cpu6502
{
    public const ushort SoftwareStackTop = 0x0800;
    public byte[] Memory { get; } = new byte[65536];
    public ushort PC { get; private set; }
    public byte A { get; private set; }
    public byte X { get; private set; }
    public byte Y { get; private set; }
    public byte SP { get; private set; } = 0xFF;
    public bool Carry { get; private set; }
    public bool Zero { get; private set; }
    public bool Negative { get; private set; }
    public bool Overflow { get; private set; }
    public bool InterruptDisable { get; private set; }
    public bool Decimal { get; private set; }
    public byte Status => (byte)(0x20 | (Carry ? 1 : 0) | (Zero ? 2 : 0) |
        (InterruptDisable ? 4 : 0) | (Decimal ? 8 : 0) | (Overflow ? 0x40 : 0) | (Negative ? 0x80 : 0));
    public List<ushort> WrittenAddresses { get; } = [];
    public int InstructionCount { get; private set; }
    public long CycleCount { get; private set; }
    public int InterruptCount { get; private set; }
    // Delegates replace default memory access. A mapper normally backs RAM with
    // Memory and keeps physical PRG ROM separately; writes must not mutate ROM.
    public Func<ushort, byte>? ReadBus { get; set; }
    public Action<ushort, byte>? WriteBus { get; set; }
    // Called before fetching each instruction (also in RunUntil). May inject an
    // NMI/IRQ; the ensuing fetch uses the interrupt's PC. Not called by Nmi/Irq.
    public Action<Cpu6502>? BeforeInstruction { get; set; }
    public int SoftwareStackWrites { get; private set; }
    public int SoftwareStackPointerWrites { get; private set; }
    // Inspection/accounting reads backing RAM, not the bus, to avoid inventing
    // CPU reads or device side effects during a store.
    public ushort SoftwareStackPointer => (ushort)(Memory[0x22] | Memory[0x23] << 8);

    public Cpu6502(byte[] code, ushort origin, ushort entry)
    {
        code.CopyTo(Memory, origin);
        PC = entry;
        Memory[0x23] = (byte)(SoftwareStackTop >> 8);
    }

    public void RunUntil(ushort stop, int instructionLimit = 100000)
    {
        for (int i = 0; i < instructionLimit; i++)
        {
            if (PC == stop)
                return;
            Step();
        }
        throw new InvalidOperationException($"Execution exceeded {instructionLimit} instructions at ${PC:X4}.");
    }

    // Inject at an instruction boundary; device timing and IRQ polling delays are not modeled.
    public void Nmi() => EnterInterrupt(0xFFFA);

    public bool Irq()
    {
        if (InterruptDisable)
            return false;
        EnterInterrupt(0xFFFE);
        return true;
    }

    void EnterInterrupt(ushort vector)
    {
        InterruptCount++;
        CycleCount += 2; // Discarded opcode/internal cycle, without dummy bus reads.
        Push((byte)(PC >> 8));
        Push((byte)PC);
        Push(Status);
        InterruptDisable = true;
        PC = ReadWord(vector);
    }

    public void Step()
    {
        BeforeInstruction?.Invoke(this);
        long startCycle = CycleCount;
        ushort start = PC;
        byte encoded = Fetch();
        if (!OpcodeTable.TryDecode(encoded, out var opcode, out var mode))
            throw new InvalidOperationException($"Unsupported opcode ${encoded:X2} at ${start:X4}.");
        InstructionCount++;
        if (opcode == Opcode.JSR)
        {
            byte low = Fetch();
            ushort returnAddress = PC;
            CycleCount++;
            Push((byte)(returnAddress >> 8));
            Push((byte)returnAddress);
            PC = (ushort)(low | Fetch() << 8);
            return;
        }
        bool pageCrossed = false;
        ushort address = mode switch
        {
            AddressMode.Implied or AddressMode.Accumulator => 0,
            AddressMode.Immediate or AddressMode.Relative => PC++,
            AddressMode.ZeroPage => Fetch(),
            AddressMode.ZeroPageX => (byte)(Fetch() + X),
            AddressMode.ZeroPageY => (byte)(Fetch() + Y),
            AddressMode.Absolute => FetchWord(),
            AddressMode.AbsoluteX => IndexedAddress(FetchWord(), X, out pageCrossed),
            AddressMode.AbsoluteY => IndexedAddress(FetchWord(), Y, out pageCrossed),
            AddressMode.IndexedIndirect => IndexedIndirectAddress(),
            AddressMode.IndirectIndexed => IndexedAddress(ReadZeroPageWord(Fetch()), Y, out pageCrossed),
            AddressMode.Indirect => IndirectAddress(),
            _ => throw new InvalidOperationException($"Unsupported address mode {mode} at ${start:X4}."),
        };
        long completionCycle = startCycle + BaseCycles(opcode, mode);
        if (pageCrossed && !IsStore(opcode) && !IsReadModifyWrite(opcode))
            completionCycle++;
        byte Operand() => ReadAt(address, completionCycle);
        byte value;
        switch (opcode)
        {
            case Opcode.LDA: A = Flags(Operand()); break;
            case Opcode.LDX: X = Flags(Operand()); break;
            case Opcode.LDY: Y = Flags(Operand()); break;
            case Opcode.STA: Store(address, A, completionCycle); break;
            case Opcode.STX: Store(address, X, completionCycle); break;
            case Opcode.STY: Store(address, Y, completionCycle); break;
            case Opcode.TAX: X = Flags(A); break;
            case Opcode.TAY: Y = Flags(A); break;
            case Opcode.TXA: A = Flags(X); break;
            case Opcode.TYA: A = Flags(Y); break;
            case Opcode.TSX: X = Flags(SP); break;
            case Opcode.TXS: SP = X; break;
            case Opcode.PHA: CycleCount = completionCycle - 1; Push(A); break;
            case Opcode.PLA: CycleCount = completionCycle - 1; A = Flags(Pop()); break;
            case Opcode.PHP: CycleCount = completionCycle - 1; Push((byte)(Status | 0x10)); break;
            case Opcode.PLP: CycleCount = completionCycle - 1; RestoreStatus(Pop()); break;
            case Opcode.ADC: Add(Operand()); break;
            case Opcode.SBC: Add((byte)~Operand()); break;
            case Opcode.AND: A = Flags((byte)(A & Operand())); break;
            case Opcode.ORA: A = Flags((byte)(A | Operand())); break;
            case Opcode.EOR: A = Flags((byte)(A ^ Operand())); break;
            case Opcode.CMP: Compare(A, Operand()); break;
            case Opcode.CPX: Compare(X, Operand()); break;
            case Opcode.CPY: Compare(Y, Operand()); break;
            case Opcode.BIT:
                value = Operand();
                Zero = (A & value) == 0;
                Negative = (value & 0x80) != 0;
                Overflow = (value & 0x40) != 0;
                break;
            case Opcode.INC: Store(address, Flags((byte)(ReadAt(address, completionCycle - 2) + 1)), completionCycle); break;
            case Opcode.DEC: Store(address, Flags((byte)(ReadAt(address, completionCycle - 2) - 1)), completionCycle); break;
            case Opcode.INX: X = Flags((byte)(X + 1)); break;
            case Opcode.INY: Y = Flags((byte)(Y + 1)); break;
            case Opcode.DEX: X = Flags((byte)(X - 1)); break;
            case Opcode.DEY: Y = Flags((byte)(Y - 1)); break;
            case Opcode.ASL:
            case Opcode.LSR:
            case Opcode.ROL:
            case Opcode.ROR:
                value = mode == AddressMode.Accumulator ? A : ReadAt(address, completionCycle - 2);
                bool carryIn = Carry;
                bool left = opcode is Opcode.ASL or Opcode.ROL;
                Carry = (value & (left ? 0x80 : 1)) != 0;
                value = Flags((byte)(left
                    ? (value << 1) | (opcode == Opcode.ROL && carryIn ? 1 : 0)
                    : (value >> 1) | (opcode == Opcode.ROR && carryIn ? 0x80 : 0)));
                if (mode == AddressMode.Accumulator) A = value;
                else Store(address, value, completionCycle);
                break;
            case Opcode.CLC: Carry = false; break;
            case Opcode.SEC: Carry = true; break;
            case Opcode.CLV: Overflow = false; break;
            case Opcode.CLI: InterruptDisable = false; break;
            case Opcode.SEI: InterruptDisable = true; break;
            case Opcode.CLD: Decimal = false; break;
            case Opcode.SED: Decimal = true; break;
            case Opcode.JMP: PC = address; break;
            case Opcode.RTS:
                CycleCount = completionCycle - 3;
                PC = (ushort)((Pop() | Pop() << 8) + 1);
                break;
            case Opcode.RTI:
                CycleCount = completionCycle - 3;
                RestoreStatus(Pop());
                PC = (ushort)(Pop() | Pop() << 8);
                break;
            case Opcode.BCC: Branch(!Carry, address); break;
            case Opcode.BCS: Branch(Carry, address); break;
            case Opcode.BEQ: Branch(Zero, address); break;
            case Opcode.BNE: Branch(!Zero, address); break;
            case Opcode.BMI: Branch(Negative, address); break;
            case Opcode.BPL: Branch(!Negative, address); break;
            case Opcode.BVC: Branch(!Overflow, address); break;
            case Opcode.BVS: Branch(Overflow, address); break;
            case Opcode.NOP: break;
            default:
                throw new InvalidOperationException($"Unsupported {opcode} (${encoded:X2}) at ${start:X4}.");
        }
        CycleCount = Math.Max(CycleCount, completionCycle);
    }

    // OpcodeTable describes encodings, not timings. These are official NMOS
    // instruction totals; variable read/branch penalties are applied separately.
    static int BaseCycles(Opcode opcode, AddressMode mode)
    {
        if (opcode is Opcode.JSR or Opcode.RTS or Opcode.RTI)
            return 6;
        if (opcode is Opcode.PHA or Opcode.PHP)
            return 3;
        if (opcode is Opcode.PLA or Opcode.PLP)
            return 4;
        if (opcode == Opcode.JMP)
            return mode == AddressMode.Indirect ? 5 : 3;
        if (IsReadModifyWrite(opcode) && mode != AddressMode.Accumulator)
            return mode switch
            {
                AddressMode.ZeroPage => 5,
                AddressMode.ZeroPageX => 6,
                AddressMode.Absolute => 6,
                AddressMode.AbsoluteX => 7,
                _ => throw new InvalidOperationException($"Unsupported RMW mode {mode}."),
            };
        return mode switch
        {
            AddressMode.Implied or AddressMode.Accumulator or AddressMode.Immediate or AddressMode.Relative => 2,
            AddressMode.ZeroPage => 3,
            AddressMode.ZeroPageX or AddressMode.ZeroPageY or AddressMode.Absolute => 4,
            AddressMode.AbsoluteX or AddressMode.AbsoluteY => IsStore(opcode) ? 5 : 4,
            AddressMode.IndexedIndirect => 6,
            AddressMode.IndirectIndexed => IsStore(opcode) ? 6 : 5,
            _ => throw new InvalidOperationException($"Unsupported cycle count for {opcode}/{mode}."),
        };
    }

    static bool IsStore(Opcode opcode) => opcode is Opcode.STA or Opcode.STX or Opcode.STY;
    static bool IsReadModifyWrite(Opcode opcode) =>
        opcode is Opcode.INC or Opcode.DEC or Opcode.ASL or Opcode.LSR or Opcode.ROL or Opcode.ROR;

    static ushort IndexedAddress(ushort address, byte index, out bool pageCrossed)
    {
        ushort result = (ushort)(address + index);
        pageCrossed = (address & 0xFF00) != (result & 0xFF00);
        return result;
    }

    byte Read(ushort address)
    {
        CycleCount++;
        return ReadBus is null ? Memory[address] : ReadBus(address);
    }

    byte ReadAt(ushort address, long cycle)
    {
        CycleCount = cycle - 1;
        return Read(address);
    }

    byte Fetch() => Read(PC++);
    ushort FetchWord() => (ushort)(Fetch() | Fetch() << 8);
    ushort ReadWord(ushort address) => (ushort)(Read(address) | Read((ushort)(address + 1)) << 8);
    ushort ReadZeroPageWord(byte address) => (ushort)(Read(address) | Read((byte)(address + 1)) << 8);

    ushort IndexedIndirectAddress()
    {
        byte pointer = (byte)(Fetch() + X);
        CycleCount++;
        return ReadZeroPageWord(pointer);
    }

    ushort IndirectAddress()
    {
        ushort pointer = FetchWord();
        // NMOS 6502 JMP ($xxFF) wraps the high-byte read within the same page.
        return (ushort)(Read(pointer) | Read((ushort)((pointer & 0xFF00) | (byte)(pointer + 1))) << 8);
    }

    byte Flags(byte value)
    {
        Zero = value == 0;
        Negative = (value & 0x80) != 0;
        return value;
    }

    void RestoreStatus(byte status)
    {
        Carry = (status & 1) != 0;
        Zero = (status & 2) != 0;
        InterruptDisable = (status & 4) != 0;
        Decimal = (status & 8) != 0;
        Overflow = (status & 0x40) != 0;
        Negative = (status & 0x80) != 0;
    }

    void Add(byte value)
    {
        int result = A + value + (Carry ? 1 : 0);
        Overflow = (~(A ^ value) & (A ^ result) & 0x80) != 0;
        Carry = result > 255;
        A = Flags((byte)result);
    }

    void Compare(byte left, byte right)
    {
        Carry = left >= right;
        Flags((byte)(left - right));
    }

    void Branch(bool taken, ushort address)
    {
        sbyte offset = (sbyte)Read(address);
        if (taken)
        {
            ushort target = (ushort)(PC + offset);
            CycleCount += (PC & 0xFF00) == (target & 0xFF00) ? 1 : 2;
            PC = target;
        }
    }

    void Push(byte value)
    {
        ushort address = (ushort)(0x100 + SP--);
        Write(address, value);
    }
    byte Pop() => Read((ushort)(0x100 + ++SP));

    void Store(ushort address, byte value, long cycle)
    {
        if (address is 0x22 or 0x23)
            SoftwareStackPointerWrites++;
        else if (address >= SoftwareStackPointer && address < SoftwareStackTop)
            SoftwareStackWrites++;
        CycleCount = cycle - 1;
        Write(address, value);
    }

    void Write(ushort address, byte value)
    {
        CycleCount++;
        WrittenAddresses.Add(address);
        if (WriteBus is null)
            Memory[address] = value;
        else
            WriteBus(address, value);
    }
}
