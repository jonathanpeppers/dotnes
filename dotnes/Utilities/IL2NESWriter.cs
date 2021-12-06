using System.Reflection.Metadata;

namespace dotnes;

class IL2NESWriter : NESWriter
{
    public IL2NESWriter(Stream stream, bool leaveOpen = false) : base(stream, leaveOpen)
    {
    }

    /// <summary>
    /// State if the A register is filled
    /// </summary>
    bool A = false;

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
                WriteLdc(checked((byte)operand));
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
            case ILOpCode.Call:
                Write(Instruction.JSR, GetAddress(operand));
                A = false;
                break;
            default:
                throw new NotImplementedException($"OpCode {code} with String operand is not implemented!");
        }
    }

    static ushort GetAddress(string name)
    {
        switch (name)
        {
            case nameof (NESLib.pal_col):
                return 0x823E;
            default:
                throw new NotImplementedException($"Address for {name} is not implemented!");
        }
    }

    void WriteLdc(byte operand)
    {
        if (A)
        {
            Write(Instruction.JSR, pusha);
        }
        Write(Instruction.LDA, checked((byte)operand));
        A = true;
    }
}
