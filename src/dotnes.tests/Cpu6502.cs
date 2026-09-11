using dotnes.ObjectModel;

namespace dotnes.tests;

// Asset-free execution of emitted code, not a NES/device or cycle-accurate emulator.
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
    public int SoftwareStackWrites { get; private set; }
    public int SoftwareStackPointerWrites { get; private set; }
    public ushort SoftwareStackPointer => ReadWord(0x22);

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
        Push((byte)(PC >> 8));
        Push((byte)PC);
        Push(Status);
        InterruptDisable = true;
        PC = ReadWord(vector);
    }

    public void Step()
    {
        ushort start = PC;
        byte encoded = Fetch();
        if (!OpcodeTable.TryDecode(encoded, out var opcode, out var mode))
            throw new InvalidOperationException($"Unsupported opcode ${encoded:X2} at ${start:X4}.");
        InstructionCount++;
        ushort address = mode switch
        {
            AddressMode.Implied or AddressMode.Accumulator => 0,
            AddressMode.Immediate or AddressMode.Relative => PC++,
            AddressMode.ZeroPage => Fetch(),
            AddressMode.ZeroPageX => (byte)(Fetch() + X),
            AddressMode.ZeroPageY => (byte)(Fetch() + Y),
            AddressMode.Absolute => FetchWord(),
            AddressMode.AbsoluteX => (ushort)(FetchWord() + X),
            AddressMode.AbsoluteY => (ushort)(FetchWord() + Y),
            AddressMode.IndexedIndirect => ReadZeroPageWord((byte)(Fetch() + X)),
            AddressMode.IndirectIndexed => (ushort)(ReadZeroPageWord(Fetch()) + Y),
            AddressMode.Indirect => IndirectAddress(),
            _ => throw new InvalidOperationException($"Unsupported address mode {mode} at ${start:X4}."),
        };
        byte value;
        switch (opcode)
        {
            case Opcode.LDA: A = Flags(Memory[address]); break;
            case Opcode.LDX: X = Flags(Memory[address]); break;
            case Opcode.LDY: Y = Flags(Memory[address]); break;
            case Opcode.STA: Store(address, A); break;
            case Opcode.STX: Store(address, X); break;
            case Opcode.STY: Store(address, Y); break;
            case Opcode.TAX: X = Flags(A); break;
            case Opcode.TAY: Y = Flags(A); break;
            case Opcode.TXA: A = Flags(X); break;
            case Opcode.TYA: A = Flags(Y); break;
            case Opcode.TSX: X = Flags(SP); break;
            case Opcode.TXS: SP = X; break;
            case Opcode.PHA: Push(A); break;
            case Opcode.PLA: A = Flags(Pop()); break;
            case Opcode.PHP: Push((byte)(Status | 0x10)); break;
            case Opcode.PLP: RestoreStatus(Pop()); break;
            case Opcode.ADC: Add(Memory[address]); break;
            case Opcode.SBC: Add((byte)~Memory[address]); break;
            case Opcode.AND: A = Flags((byte)(A & Memory[address])); break;
            case Opcode.ORA: A = Flags((byte)(A | Memory[address])); break;
            case Opcode.EOR: A = Flags((byte)(A ^ Memory[address])); break;
            case Opcode.CMP: Compare(A, Memory[address]); break;
            case Opcode.CPX: Compare(X, Memory[address]); break;
            case Opcode.CPY: Compare(Y, Memory[address]); break;
            case Opcode.BIT:
                Zero = (A & Memory[address]) == 0;
                Negative = (Memory[address] & 0x80) != 0;
                Overflow = (Memory[address] & 0x40) != 0;
                break;
            case Opcode.INC: Store(address, Flags((byte)(Memory[address] + 1))); break;
            case Opcode.DEC: Store(address, Flags((byte)(Memory[address] - 1))); break;
            case Opcode.INX: X = Flags((byte)(X + 1)); break;
            case Opcode.INY: Y = Flags((byte)(Y + 1)); break;
            case Opcode.DEX: X = Flags((byte)(X - 1)); break;
            case Opcode.DEY: Y = Flags((byte)(Y - 1)); break;
            case Opcode.ASL:
            case Opcode.LSR:
            case Opcode.ROL:
            case Opcode.ROR:
                value = mode == AddressMode.Accumulator ? A : Memory[address];
                bool carryIn = Carry;
                bool left = opcode is Opcode.ASL or Opcode.ROL;
                Carry = (value & (left ? 0x80 : 1)) != 0;
                value = Flags((byte)(left
                    ? (value << 1) | (opcode == Opcode.ROL && carryIn ? 1 : 0)
                    : (value >> 1) | (opcode == Opcode.ROR && carryIn ? 0x80 : 0)));
                if (mode == AddressMode.Accumulator) A = value;
                else Store(address, value);
                break;
            case Opcode.CLC: Carry = false; break;
            case Opcode.SEC: Carry = true; break;
            case Opcode.CLV: Overflow = false; break;
            case Opcode.CLI: InterruptDisable = false; break;
            case Opcode.SEI: InterruptDisable = true; break;
            case Opcode.CLD: Decimal = false; break;
            case Opcode.SED: Decimal = true; break;
            case Opcode.JMP: PC = address; break;
            case Opcode.JSR:
                ushort returnAddress = (ushort)(PC - 1);
                Push((byte)(returnAddress >> 8));
                Push((byte)returnAddress);
                PC = address;
                break;
            case Opcode.RTS:
                PC = (ushort)((Pop() | Pop() << 8) + 1);
                break;
            case Opcode.RTI:
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
    }

    byte Fetch() => Memory[PC++];
    ushort FetchWord() => (ushort)(Fetch() | Fetch() << 8);
    ushort ReadWord(ushort address) => (ushort)(Memory[address] | Memory[(ushort)(address + 1)] << 8);
    ushort ReadZeroPageWord(byte address) => (ushort)(Memory[address] | Memory[(byte)(address + 1)] << 8);

    ushort IndirectAddress()
    {
        ushort pointer = FetchWord();
        // NMOS 6502 JMP ($xxFF) wraps the high-byte read within the same page.
        return (ushort)(Memory[pointer] | Memory[(pointer & 0xFF00) | (byte)(pointer + 1)] << 8);
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
        if (taken) PC = (ushort)(PC + (sbyte)Memory[address]);
    }

    void Push(byte value)
    {
        ushort address = (ushort)(0x100 + SP--);
        WrittenAddresses.Add(address);
        Memory[address] = value;
    }
    byte Pop() => Memory[0x100 + ++SP];

    void Store(ushort address, byte value)
    {
        WrittenAddresses.Add(address);
        if (address is 0x22 or 0x23)
            SoftwareStackPointerWrites++;
        else if (address >= SoftwareStackPointer && address < SoftwareStackTop)
            SoftwareStackWrites++;
        Memory[address] = value;
    }
}
