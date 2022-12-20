using System.Collections.Immutable;
using System.Reflection.Metadata;
using static NES.NESLib;

namespace dotnes;

class IL2NESWriter : NESWriter
{
    public IL2NESWriter(Stream stream, bool leaveOpen = false) : base(stream, leaveOpen)
    {
    }

    readonly Stack<int> A = new Stack<int> ();
    readonly List<ImmutableArray<byte>> ByteArrays = new List<ImmutableArray<byte>>();
    ushort ByteArrayOffset = 0;

    public void Write(ILOpCode code, ushort sizeOfMain)
    {
        switch (code)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Dup:
                // Do nothing
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
                // Remove the previous Ldtoken
                _writer.BaseStream.SetLength(_writer.BaseStream.Length - 4);
                break;
            case ILOpCode.Ldloc_0:
                {
                    var offset = A.Peek();
                    Write(NESInstruction.LDA, (byte)(offset & 0xff));
                    Write(NESInstruction.LDX, (byte)(offset >> 8));
                    Write(NESInstruction.JSR, popax.GetAddressAfterMain(sizeOfMain));
                    Write(NESInstruction.LDX, 0x00);
                    Write(NESInstruction.LDA, 0x40);
                }
                break;
            default:
                throw new NotImplementedException($"OpCode {code} with no operands is not implemented!");
        }
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
                    //TODO: and if larger than ushort?
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
                Write(NESInstruction.JMP_abs, checked((ushort)(byte.MaxValue - operand + 0x8540 - 1)));
                break;
            case ILOpCode.Newarr:
                // Remove the previous Ldc_i4_s
                _writer.BaseStream.SetLength(_writer.BaseStream.Length - 2);
                A.Pop();
                break;
            default:
                throw new NotImplementedException($"OpCode {code} with Int32 operand is not implemented!");
        }
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
                A.Push(operand.Length); //HACK: to use operand for vram_write?
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
                        _writer.BaseStream.SetLength(_writer.BaseStream.Length - 7);
                        //TODO: these are hardcoded until I figure this out
                        Write(NESInstruction.LDX, 0x20);
                        Write(NESInstruction.LDA, 0x42);
                        A.Push(address);
                        break;
                    case nameof(NESLib.vram_write):
                        Write(ILOpCode.Ldc_i4_s, A.Pop(), sizeOfMain);
                        goto default;
                    default:
                        Write(NESInstruction.JSR, GetAddress(operand));
                        break;
                }
                // Pop twice
                if (A.Count > 0)
                    A.Pop();
                if (A.Count > 0)
                    A.Pop();
                break;
            default:
                throw new NotImplementedException($"OpCode {code} with String operand is not implemented!");
        }
    }

    public void Write(ILOpCode code, ImmutableArray<byte> operand, ushort sizeOfMain)
    {
        if (operand == null)
            throw new ArgumentNullException(nameof(operand));
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
    }

    static ushort GetAddress(string name)
    {
        switch (name)
        {
            case nameof(pal_col):
                return 0x823E;
            case nameof(vram_adr):
                return 0x83D4;
            case nameof(vram_write):
                return 0x834F;
            case nameof(ppu_on_all):
                return 0x8289;
            case nameof(pal_bg):
                return 0x822B;
            case nameof(vram_fill):
                return 0x83DF;
            default:
                throw new NotImplementedException($"Address for {name} is not implemented!");
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
        Write(NESInstruction.LDA, checked((byte)operand));
        A.Push(operand);
    }
}
