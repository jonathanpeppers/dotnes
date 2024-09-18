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

    /// <summary>
    /// The local evaluation stack
    /// </summary>
    readonly Stack<int> Stack = new();
    /// <summary>
    /// Dictionary of local varaiables
    /// </summary>
    readonly Dictionary<int, Local> Locals = new();
    /// <summary>
    /// List of byte[] data
    /// </summary>
    readonly List<ImmutableArray<byte>> ByteArrays = new();
    readonly ushort local = 0x324;
    ushort ByteArrayOffset = 0;
    ILOpCode previous;

    /// <summary>
    /// NOTE: may not be exactly correct, this is the instructions inside zerobss:
    /// A925           LDA #$25                      ; zerobss
    /// 852A STA ptr1                      
    /// A903            LDA #$03                      
    /// 852B STA ptr1+1                    
    /// A900            LDA #$00                      
    /// A8              TAY                           
    /// A200 LDX #$00                      
    /// F00A BEQ $85DE                     
    /// 912A STA(ptr1),y                  
    /// C8 INY                           
    /// D0FB BNE $85D4                     
    /// E62B INC ptr1+1                    
    /// CA              DEX                           
    /// D0F6            BNE $85D4                     
    /// C002            CPY #$02
    /// ...
    /// A program with 0 locals has C000
    /// </summary>
    public int LocalCount { get; private set; }

    record Local(int Value, int? Address = null);

    public void Write(ILOpCode code, ushort sizeOfMain)
    {
        switch (code)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Dup:
                if (Stack.Count > 0)
                    Stack.Push(Stack.Peek());
                break;
            case ILOpCode.Pop:
                if (Stack.Count > 0)
                    Stack.Pop();
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
                    Locals[0] = new Local(Stack.Pop());
                }
                else
                {
                    WriteStloc(Locals[0] = new Local(Stack.Pop(), local));
                }
                break;
            case ILOpCode.Stloc_1:
                if (previous == ILOpCode.Ldtoken)
                {
                    SeekBack(4);
                    Locals[1] = new Local(Stack.Pop());
                }
                else
                {
                    WriteStloc(Locals[1] = new Local(Stack.Pop(), local + 1));
                }
                break;
            case ILOpCode.Stloc_2:
                if (previous == ILOpCode.Ldtoken)
                {
                    SeekBack(4);
                    Locals[2] = new Local(Stack.Pop());
                }
                else
                {
                    WriteStloc(Locals[2] = new Local(Stack.Pop(), local + 2));
                }
                break;
            case ILOpCode.Stloc_3:
                if (previous == ILOpCode.Ldtoken)
                {
                    SeekBack(4);
                    Locals[3] = new Local(Stack.Pop());
                }
                else
                {
                    WriteStloc(Locals[3] = new Local(Stack.Pop(), local + 3));
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
            case ILOpCode.Add:
                Stack.Push(Stack.Pop() + Stack.Pop());
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
                // NOTE: This is (-3) because we want to go to the instruction right before donelib, that is "jump to self"
                Write(NESInstruction.JMP_abs, (ushort)(Labels[nameof(donelib)] - 3));
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
                Locals[operand] = new Local(Stack.Pop());
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
                Write(NESInstruction.JSR, Labels[nameof(pushax)]);
                Write(NESInstruction.LDX, 0x00);
                Write(ILOpCode.Ldc_i4_s, operand.Length, sizeOfMain);
                break;
            case ILOpCode.Call:
                switch (operand)
                {
                    case nameof(NTADR_A):
                    case nameof(NTADR_B):
                    case nameof(NTADR_C):
                    case nameof(NTADR_D):
                        if (Stack.Count < 2)
                        {
                            throw new InvalidOperationException($"{operand} was called with less than 2 on the stack.");
                        }
                        var address = operand switch
                        {
                            nameof(NTADR_A) => NTADR_A(checked((byte)Stack.Pop()), checked((byte)Stack.Pop())),
                            nameof(NTADR_B) => NTADR_B(checked((byte)Stack.Pop()), checked((byte)Stack.Pop())),
                            nameof(NTADR_C) => NTADR_C(checked((byte)Stack.Pop()), checked((byte)Stack.Pop())),
                            nameof(NTADR_D) => NTADR_D(checked((byte)Stack.Pop()), checked((byte)Stack.Pop())),
                            _ => throw new InvalidOperationException($"Address lookup of {operand} not implemented!"),
                        };
                        SeekBack(7);
                        //TODO: these are hardcoded until I figure this out
                        Write(NESInstruction.LDX, 0x20);
                        Write(NESInstruction.LDA, 0x42);
                        Stack.Push(address);
                        break;
                    default:
                        Write(NESInstruction.JSR, GetAddress(operand));
                        break;
                }
                // Pop N times
                int args = GetNumberOfArguments(operand);
                for (int i = 0; i < args; i++)
                {
                    if (Stack.Count > 0)
                        Stack.Pop();
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
                Stack.Push(ByteArrayOffset);
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

    ushort GetAddress(string name)
    {
        //if (Labels.TryGetValue(name, out var address))
        //{
        //    return address;
        //}

        switch (name)
        {
            case nameof(pal_col):
                return 0x823E;
            case nameof(pal_bg):
                return 0x822B;
            case nameof(pal_clear):
                return 0x824E;
            case nameof(pal_all):
                return 0x8211;
            case nameof(pal_spr):
                return 0x8235;
            case nameof(pal_spr_bright):
                return 0x825D;
            case nameof(ppu_on_all):
                return 0x8289;
            case nameof(vram_adr):
                return 0x83D4;
            case nameof(ppu_wait_frame):
                return 0x82DB;
            case nameof(ppu_on_bg):
                return 0x8292;
            case nameof(ppu_on_spr):
                return 0x8298;
            case nameof(rand):
                return 0x860C;
            case nameof(rand8):
                return 0x860C;
            case nameof(rand16):
                return 0x8617;
            case nameof(set_rand):
                return 0x8623;
            case nameof(delay):
                return 0x841A;
            case nameof(nesclock):
                return 0x8415;
            case nameof(oam_clear):
                return 0x82AE;
            case nameof(oam_hide_rest):
                return 0x82CE;
            case nameof(oam_size):
                return 0x82BC;
            case nameof(vram_fill):
                return 0x83DF;
            case nameof(vram_write):
                return 0x834F;
            case nameof(vram_put):
                return 0x83DB;
            case nameof(vram_inc):
                return 0x8401;
            case nameof(set_vram_update):
                return 0x8376;
            case nameof(set_ppu_ctrl_var):
                return 0x82AB;
            case nameof(scroll):
                return 0x82FB;
            case nameof(oam_spr):
                if (Labels.TryGetValue(nameof(oam_spr), out var address))
                {
                    return address;
                }
                else
                {
                    return 0x0000;
                }
            default:
                throw new NotImplementedException($"{nameof(GetAddress)} for {name} is not implemented!");
        }
    }

    static int GetNumberOfArguments(string name)
    {
        switch (name)
        {
            case nameof(ppu_on_bg):
            case nameof(ppu_on_spr):
            case nameof(pal_clear):
            case nameof(ppu_on_all):
            case nameof(ppu_wait_frame):
            case nameof(rand):
            case nameof(rand8): 
            case nameof(rand16):
            case nameof(nesclock):
            case nameof(oam_clear):
                return 0;
            case nameof(vram_adr):
            case nameof(vram_write):
            case nameof(vram_put):
            case nameof(set_vram_update):
            case nameof(set_ppu_ctrl_var):
            case nameof(pal_bg):
            case nameof(pal_spr_bright):
            case nameof(pal_spr):
            case nameof(pal_all):
            case nameof(set_rand):    
            case nameof(oam_hide_rest):
            case nameof(oam_size):
                return 1;
            case nameof(pal_col):
            case nameof(vram_fill):
            case nameof(NTADR_A):
            case nameof(NTADR_B):
            case nameof(NTADR_C):
            case nameof(NTADR_D):
            case nameof(scroll):
                return 2;
            case nameof(oam_spr):
                return 5;
            default:
                throw new NotImplementedException($"{nameof(GetNumberOfArguments)} for {name} is not implemented!");
        }
    }

    void WriteStloc(Local local)
    {
        if (local.Address is null)
            throw new ArgumentNullException(nameof(local.Address));

        if (local.Value < byte.MaxValue)
        {
            LocalCount += 1;
            SeekBack(6);
            Write(NESInstruction.LDA, (byte)local.Value);
            Write(NESInstruction.STA_abs, (ushort)local.Address);
            Write(NESInstruction.LDA, 0x22);
            Write(NESInstruction.LDX, 0x86);
        }
        else if (local.Value < ushort.MaxValue)
        {
            LocalCount += 2;
            SeekBack(8);
            Write(NESInstruction.LDX, 0x03);
            Write(NESInstruction.LDA, 0xC0);
            Write(NESInstruction.STA_abs, (ushort)local.Address);
            Write(NESInstruction.STX_abs, (ushort)(local.Address + 1));
            Write(NESInstruction.LDA, 0x28);
            Write(NESInstruction.LDX, 0x86);
        }
        else
        {
            throw new NotImplementedException($"{nameof(WriteStloc)} not implemented for value larger than ushort: {local.Value}");
        }
    }

    void WriteLdc(ushort operand, ushort sizeOfMain)
    {
        if (LastLDA)
        {
            Write(NESInstruction.JSR, Labels[nameof(pusha)]);
        }
        Write(NESInstruction.LDX, checked((byte)(operand >> 8)));
        Write(NESInstruction.LDA, checked((byte)(operand & 0xff)));
        Stack.Push(operand);
    }

    void WriteLdc(byte operand, ushort sizeOfMain)
    {
        if (LastLDA)
        {
            Write(NESInstruction.JSR, Labels[nameof(pusha)]);
        }
        Write(NESInstruction.LDA, operand);
        Stack.Push(operand);
    }

    void WriteLdloc(Local local, ushort sizeOfMain)
    {
        if (local.Address is not null)
        {
            // This is actually a local variable
            if (local.Value < byte.MaxValue)
            {
                Write(NESInstruction.LDA_abs, (ushort)local.Address);
                Write(NESInstruction.JSR, Labels[nameof(pusha)]);
            }
            else if (local.Value < ushort.MaxValue)
            {
                Write(NESInstruction.JSR, Labels[nameof(pusha)]);
                Write(NESInstruction.LDA_abs, (ushort)local.Address);
                Write(NESInstruction.LDX_abs, (ushort)(local.Address + 1));
            }
            else
            {
                throw new NotImplementedException($"{nameof(WriteLdloc)} not implemented for value larger than ushort: {local.Value}");
            }
        }
        else
        {
            // This is more like an inline constant value
            Write(NESInstruction.LDA, (byte)(local.Value & 0xff));
            Write(NESInstruction.LDX, (byte)(local.Value >> 8));
            Write(NESInstruction.JSR, Labels[nameof(pushax)]);
            Write(NESInstruction.LDX, 0x00);
            Write(NESInstruction.LDA, 0x40);
        }
        Stack.Push(local.Value);
    }

    void SeekBack(int length)
    {
        _logger.WriteLine($"Seek back {length} bytes");
        if (_writer.BaseStream.Length < length)
        {
            _writer.BaseStream.SetLength(0);
        }
        else
        {
            _writer.BaseStream.SetLength(_writer.BaseStream.Length - length);
        }
    }
}
