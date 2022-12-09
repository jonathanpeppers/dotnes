using System.Reflection.Metadata;
using static NES.NESLib;

namespace dotnes;

class IL2NESWriter : NESWriter
{
    public IL2NESWriter(Stream stream, bool leaveOpen = false) : base(stream, leaveOpen)
    {
    }

    readonly Stack<int> A = new();
    readonly Dictionary<string, ushort> addresses = new()
    {
        { "NES.NESLib::pal_col",    0x823E },
        { "NES.NESLib::vram_adr",   0x83D4 },
        { "NES.NESLib::vram_write", 0x834F },
        { "NES.NESLib::ppu_on_all", 0x8289 },
    };

    public void Write(ILOpCode code)
    {
        switch (code)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Ldc_i4_0:
                WriteLdc(0);
                break;
            case ILOpCode.Ldc_i4_1:
                WriteLdc(1);
                break;
            case ILOpCode.Ldc_i4_2:
                WriteLdc(2);
                break;
            case ILOpCode.Ldc_i4_3:
                WriteLdc(3);
                break;
            case ILOpCode.Ldc_i4_4:
                WriteLdc(4);
                break;
            case ILOpCode.Ldc_i4_5:
                WriteLdc(5);
                break;
            case ILOpCode.Ldc_i4_6:
                WriteLdc(6);
                break;
            case ILOpCode.Ldc_i4_7:
                WriteLdc(7);
                break;
            case ILOpCode.Ldc_i4_8:
                WriteLdc(8);
                break;
            default:
                throw new NotImplementedException($"OpCode {code} with no operands is not implemented!");
        }
    }

    public void Write(ILOpCode code, int operand)
    {
        switch (code)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Ldc_i4:
            case ILOpCode.Ldc_i4_s:
                WriteLdc(checked((byte)operand));
                break;
            case ILOpCode.Br_s:
                Write(NESInstruction.JMP_abs, checked((ushort)(byte.MaxValue - operand + 0x8540 - 1)));
                break;
            default:
                throw new NotImplementedException($"OpCode {code} with Int32 operand is not implemented!");
        }
    }

    public void Write(ILOpCode code, string operand)
    {
        switch (code)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Ldstr:
                //TODO: hardcoded until string table figured out
                Write(NESInstruction.LDA, 0xF1);
                Write(NESInstruction.LDX, 0x85);
                Write(NESInstruction.JSR, pushax);
                Write(NESInstruction.LDX, 0x00);
                break;
            case ILOpCode.Call:
                switch (operand)
                {
                    case "NES.NESLib::NTADR_A":
                        if (A.Count < 2)
                        {
                            throw new InvalidOperationException($"{operand} was called with less than 2 on the stack.");
                        }
                        ushort address = NTADR_A(checked((byte)A.Pop()), checked((byte)A.Pop()));
                        _writer.BaseStream.SetLength(_writer.BaseStream.Length - 7);
                        //TODO: these are hardcoded until I figure this out
                        Write(NESInstruction.LDX, 0x20);
                        Write(NESInstruction.LDA, 0x42);
                        A.Push(address);
                        break;
                    default:
                        Write(NESInstruction.JSR, GetAddress(operand));
                        break;
                }
                A.Clear();
                break;
            default:
                throw new NotImplementedException($"OpCode {code} with String operand is not implemented!");
        }
    }

    ushort GetAddress(string name)
    {
        if (addresses.TryGetValue(name, out var address))
            return address;
        throw new NotImplementedException($"Address for {name} is not implemented!");
    }

    public void AddAddress(string name, ushort address)
    {
        // NOTE: needs to skip built-in subs
        if (!addresses.ContainsKey(name))
        {
            addresses.Add(name, address);
        }
    }

    void WriteLdc(byte operand)
    {
        if (A.Count > 0)
        {
            Write(NESInstruction.JSR, pusha);
        }
        Write(NESInstruction.LDA, checked((byte)operand));
        A.Push(operand);
    }
}
