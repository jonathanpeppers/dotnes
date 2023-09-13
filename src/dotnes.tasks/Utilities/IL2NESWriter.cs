using System.Collections.Immutable;
using System.Reflection.Metadata;
using static NES.NESLib;

namespace dotnes;

class IL2NESWriter : NESWriter
{
    public IL2NESWriter(Stream stream, bool leaveOpen = false, ILogger? logger = null)
        : base(stream, leaveOpen, logger)
    {
    }

    readonly Stack<int> A = new();
    readonly Dictionary<int, Local> Locals = new();
    readonly List<ImmutableArray<byte>> ByteArrays = new();
    ushort local = 0x324;
    ushort ByteArrayOffset = 0;
    ILOpCode previous;

    record Local(int Value, int? Address = null);

    public void Write(ILOpCode code, ushort sizeOfMain)
    {
        switch (code)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Dup:
                if (A.Count > 0)
                    A.Push(A.Peek());
                break;
            case ILOpCode.Ldc_i4_0:
                WriteLdc(0, sizeOfMain);
                break;
            case ILOpCode.Ldc_i4_1:
                WriteLdc(1, sizeOfMain);
                break;
            case ILOpCode.Ldc_i4_2:
                WriteLdc(2, sizeOfMain);
                break;
            case ILOpCode.Ldc_i4_3:
                WriteLdc(3, sizeOfMain);
                break;
            case ILOpCode.Ldc_i4_4:
                WriteLdc(4, sizeOfMain);
                break;
            case ILOpCode.Ldc_i4_5:
                WriteLdc(5, sizeOfMain);
                break;
            case ILOpCode.Ldc_i4_6:
                WriteLdc(6, sizeOfMain);
                break;
            case ILOpCode.Ldc_i4_7:
                WriteLdc(7, sizeOfMain);
                break;
            case ILOpCode.Ldc_i4_8:
                WriteLdc(8, sizeOfMain);
                break;
            case ILOpCode.Stloc_0:
                if (previous == ILOpCode.Ldtoken)
                {
                    SeekBack(4);
                    Locals[0] = new Local(A.Pop());
                }
                else
                {
                    int address = local;
                    Locals[0] = new Local(A.Pop(), address);
                    SeekBack(8);
                    Write(NESInstruction.LDX, 0x03);
                    Write(NESInstruction.LDA, 0xC0);
                    Write(NESInstruction.STA_abs, (ushort)address);
                    Write(NESInstruction.STX_abs, (ushort)(address + 1));
                    Write(NESInstruction.LDA, 0x28);
                    Write(NESInstruction.LDX, 0x86);
                }
                break;
            case ILOpCode.Stloc_1:
                if (previous == ILOpCode.Ldtoken)
                {
                    SeekBack(4);
                    Locals[1] = new Local(A.Pop());
                }
                else
                {
                    int address = local + 1;
                    Locals[1] = new Local(A.Pop(), address);
                    SeekBack(8);
                    Write(NESInstruction.LDX, 0x03);
                    Write(NESInstruction.LDA, 0xC0);
                    Write(NESInstruction.STA_abs, (ushort)address);
                    Write(NESInstruction.STX_abs, (ushort)(address + 1));
                    Write(NESInstruction.LDA, 0x28);
                    Write(NESInstruction.LDX, 0x86);
                }
                break;
            case ILOpCode.Stloc_2:
                if (previous == ILOpCode.Ldtoken)
                {
                    SeekBack(4);
                    Locals[2] = new Local(A.Pop());
                }
                else
                {
                    int address = local + 2;
                    Locals[2] = new Local(A.Pop(), address);
                    SeekBack(8);
                    Write(NESInstruction.LDX, 0x03);
                    Write(NESInstruction.LDA, 0xC0);
                    Write(NESInstruction.STA_abs, (ushort)address);
                    Write(NESInstruction.STX_abs, (ushort)(address + 1));
                    Write(NESInstruction.LDA, 0x28);
                    Write(NESInstruction.LDX, 0x86);
                }
                break;
            case ILOpCode.Stloc_3:
                if (previous == ILOpCode.Ldtoken)
                {
                    SeekBack(4);
                    Locals[3] = new Local(A.Pop());
                }
                else
                {
                    int address = local + 3;
                    Locals[3] = new Local(A.Pop(), address);
                    SeekBack(8);
                    Write(NESInstruction.LDX, 0x03);
                    Write(NESInstruction.LDA, 0xC0);
                    Write(NESInstruction.STA_abs, (ushort)address);
                    Write(NESInstruction.STX_abs, (ushort)(address + 1));
                    Write(NESInstruction.LDA, 0x28);
                    Write(NESInstruction.LDX, 0x86);
                }
                break;
            case ILOpCode.Ldloc_0:
                WriteLdloc(Locals[0], sizeOfMain);
                break;
            case ILOpCode.Ldloc_1:
                WriteLdloc(Locals[1], sizeOfMain);
                break;
            case ILOpCode.Ldloc_2:
                WriteLdloc(Locals[2], sizeOfMain);
                break;
            case ILOpCode.Ldloc_3:
                WriteLdloc(Locals[3], sizeOfMain);
                break;
            case ILOpCode.Conv_u1:
            case ILOpCode.Conv_u2:
            case ILOpCode.Conv_u4:
            case ILOpCode.Conv_u8:
                // Do nothing
                break;
            default:
                throw new NotImplementedException($"OpCode {code} with no operands is not implemented!");
        }
        previous = code;
    }

    public void Write(ILOpCode code, int operand, ushort sizeOfMain)
    {
        switch (code)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Ldc_i4:
            case ILOpCode.Ldc_i4_s:
                if (operand > ushort.MaxValue)
                {
                    throw new NotImplementedException($"{code} not implemented for value larger than ushort: {operand}");
                }
                else if (operand > byte.MaxValue)
                {
                    WriteLdc(checked((ushort)operand), sizeOfMain);
                }
                else
                {
                    WriteLdc((byte)operand, sizeOfMain);
                }
                break;
            case ILOpCode.Br_s:
                Write(NESInstruction.JMP_abs, donelib.GetAddressAfterMain(sizeOfMain));
                break;
            case ILOpCode.Newarr:
                if (previous == ILOpCode.Ldc_i4_s)
                {
                    SeekBack(2);
                }
                break;
            case ILOpCode.Stloc_s:
                if (previous == ILOpCode.Ldtoken)
                {
                    SeekBack(4);
                }
                Locals[operand] = new Local(A.Pop());
                break;
            case ILOpCode.Ldloc_s:
                WriteLdloc(Locals[operand], sizeOfMain);
                break;
            default:
                throw new NotImplementedException($"OpCode {code} with Int32 operand is not implemented!");
        }
        previous = code;
    }

    public void Write(ILOpCode code, string operand, ushort sizeOfMain)
    {
        switch (code)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Ldstr:
                //TODO: hardcoded until string table figured out
                Write(NESInstruction.LDA, 0xF1);
                Write(NESInstruction.LDX, 0x85);
                Write(NESInstruction.JSR, pushax.GetAddressAfterMain(sizeOfMain));
                Write(NESInstruction.LDX, 0x00);
                Write(ILOpCode.Ldc_i4_s, operand.Length, sizeOfMain);
                break;
            case ILOpCode.Call:
                switch (operand)
                {
                    case nameof(NTADR_A):
                        if (A.Count < 2)
                        {
                            throw new InvalidOperationException($"{operand} was called with less than 2 on the stack.");
                        }
                        ushort address = NTADR_A(checked((byte)A.Pop()), checked((byte)A.Pop()));
                        SeekBack(7);
                        //TODO: these are hardcoded until I figure this out
                        Write(NESInstruction.LDX, 0x20);
                        Write(NESInstruction.LDA, 0x42);
                        A.Push(address);
                        break;
                    default:
                        Write(NESInstruction.JSR, GetAddress(operand));
                        break;
                }
                // Pop N times
                int args = GetNumberOfArguments(operand);
                for (int i = 0; i < args; i++)
                {
                    if (A.Count > 0)
                        A.Pop();
                }
                break;
            default:
                throw new NotImplementedException($"OpCode {code} with String operand is not implemented!");
        }
        previous = code;
    }

    public void Write(ILOpCode code, ImmutableArray<byte> operand, ushort sizeOfMain)
    {
        switch (code)
        {
            case ILOpCode.Ldtoken:
                if (ByteArrayOffset == 0)
                    ByteArrayOffset = rodata.GetAddressAfterMain(sizeOfMain);
                Write(NESInstruction.LDA, (byte)(ByteArrayOffset & 0xff));
                Write(NESInstruction.LDX, (byte)(ByteArrayOffset >> 8));
                A.Push(ByteArrayOffset);
                ByteArrayOffset = (ushort)(ByteArrayOffset + operand.Length);
                ByteArrays.Add(operand);
                break;
            default:
                throw new NotImplementedException($"OpCode {code} with byte[] operand is not implemented!");
        }
        previous = code;
    }

    /// <summary>
    /// Write all the byte[] values
    /// </summary>
    public void WriteByteArrays(IL2NESWriter parent)
    {
        foreach (var bytes in parent.ByteArrays)
        {
            foreach (var b in bytes)
            {
                _writer.Write(b);
            }
        }
    }

    static ushort GetAddress(string name)
    {
        switch (name)
        {
            case nameof(pal_col):
                return 0x823E;
            case nameof(pal_bg):
                return 0x822B;
            case nameof(ppu_on_all):
                return 0x8289;
            case nameof(vram_adr):
                return 0x83D4;
            case nameof(vram_fill):
                return 0x83DF;
            case nameof(vram_write):
                return 0x834F;
            default:
                throw new NotImplementedException($"{nameof(GetAddress)} for {name} is not implemented!");
        }
    }

    static int GetNumberOfArguments(string name)
    {
        switch (name)
        {
            case nameof(ppu_on_all):
                return 0;
            case nameof(vram_adr):
            case nameof(vram_write):
            case nameof(pal_bg):
                return 1;
            case nameof(pal_col):
            case nameof(vram_fill):
            case nameof(NTADR_A):
                return 2;
            default:
                throw new NotImplementedException($"{nameof(GetNumberOfArguments)} for {name} is not implemented!");
        }
    }

    void WriteLdc(ushort operand, ushort sizeOfMain)
    {
        if (LastLDA)
        {
            Write(NESInstruction.JSR, pusha.GetAddressAfterMain(sizeOfMain));
        }
        Write(NESInstruction.LDX, checked((byte)(operand >> 8)));
        Write(NESInstruction.LDA, checked((byte)(operand & 0xff)));
        A.Push(operand);
    }

    void WriteLdc(byte operand, ushort sizeOfMain)
    {
        if (LastLDA)
        {
            Write(NESInstruction.JSR, pusha.GetAddressAfterMain(sizeOfMain));
        }
        Write(NESInstruction.LDA, operand);
        A.Push(operand);
    }

    void WriteLdloc(Local local, ushort sizeOfMain)
    {
        if (local.Address is not null)
        {
            // This is actually a local variable
            Write(NESInstruction.JSR, pusha.GetAddressAfterMain(sizeOfMain));
            Write(NESInstruction.LDA_abs, (ushort)local.Address);
            Write(NESInstruction.LDX_abs, (ushort)(local.Address + 1));
        }
        else
        {
            // This is more like an inline constant value
            Write(NESInstruction.LDA, (byte)(local.Value & 0xff));
            Write(NESInstruction.LDX, (byte)(local.Value >> 8));
            Write(NESInstruction.JSR, pushax.GetAddressAfterMain(sizeOfMain));
            Write(NESInstruction.LDX, 0x00);
            Write(NESInstruction.LDA, 0x40);
        }
    }

    void SeekBack(int length)
    {
        _logger.WriteLine($"Seek back {length} bytes");
        _writer.BaseStream.SetLength(_writer.BaseStream.Length - length);
    }
}
