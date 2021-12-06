using System.Buffers;
using System.Text;

namespace dotnes;

/// <summary>
/// Writes .nes files
/// * https://wiki.nesdev.org/w/index.php/INES
/// * https://bheisler.github.io/post/nes-rom-parser-with-nom/
/// </summary>
class NESWriter : IDisposable
{
    public static readonly Encoding Encoding = Encoding.ASCII;

    /// <summary>
    /// PRG_ROM is in 16 KB units
    /// </summary>
    public const int PRG_ROM_BLOCK_SIZE = 16384;
    /// <summary>
    /// CHR ROM in in 8 KB units
    /// </summary>
    public const int CHR_ROM_BLOCK_SIZE = 8192;

    protected const int TEMP = 0x17;
    protected const int sp = 0x22;
    protected const ushort pusha = 0x85A2;
    protected const ushort pushax = 0x85B8;
    protected const ushort pal_col = 0x823E;
    protected const ushort vram_adr = 0x83D4;
    protected const ushort vram_write = 0x834F;
    protected const ushort ppu_on_all = 0x8289;

    protected readonly BinaryWriter _writer;

    public NESWriter(Stream stream, bool leaveOpen = false) => _writer = new BinaryWriter(stream, Encoding, leaveOpen);

    public Stream BaseStream => _writer.BaseStream;

    /// <summary>
    /// Trainer, if present (0 or 512 bytes)
    /// </summary>
    public byte[]? Trainer { get; set; }

    /// <summary>
    /// PRG ROM data (16384 * x bytes)
    /// </summary>
    public byte[]? PRG_ROM { get; set; }

    /// <summary>
    /// CHR ROM data, if present (8192 * y bytes)
    /// </summary>
    public byte[]? CHR_ROM { get; set; }

    /// <summary>
    /// PlayChoice INST-ROM, if present (0 or 8192 bytes)
    /// </summary>
    public byte[]? INST_ROM { get; set; }

    /// <summary>
    /// Mapper, mirroring, battery, trainer
    /// </summary>
    public byte Flags6 { get; set; }

    /// <summary>
    /// Mapper, VS/Playchoice, NES 2.0
    /// </summary>
    public byte Flags7 { get; set; }

    /// <summary>
    /// PRG-RAM size (rarely used extension)
    /// </summary>
    public byte Flags8 { get; set; }

    /// <summary>
    /// TV system (rarely used extension)
    /// </summary>
    public byte Flags9 { get; set; }

    /// <summary>
    /// TV system, PRG-RAM presence (unofficial, rarely used extension)
    /// </summary>
    public byte Flags10 { get; set; }

    public long Length => _writer.BaseStream.Length;

    public void WriteHeader(byte PRG_ROM_SIZE = 0, byte CHR_ROM_SIZE = 0)
    {
        _writer.Write('N');
        _writer.Write('E');
        _writer.Write('S');
        _writer.Write('\x1A');
        // Size of PRG ROM in 16 KB units
        if (PRG_ROM != null)
            _writer.Write(checked ((byte)(PRG_ROM.Length / PRG_ROM_BLOCK_SIZE)));
        else
            _writer.Write(PRG_ROM_SIZE);
        // Size of CHR ROM in 8 KB units (Value 0 means the board uses CHR RAM)
        if (CHR_ROM != null)
            _writer.Write(checked((byte)(CHR_ROM.Length / CHR_ROM_BLOCK_SIZE)));
        else
            _writer.Write(CHR_ROM_SIZE);
        _writer.Write(Flags6);
        _writer.Write(Flags7);
        _writer.Write(Flags8);
        _writer.Write(Flags9);
        _writer.Write(Flags10);
        // 5 bytes of padding
        WriteZeroes(5);
    }

    /// <summary>
    /// Writes N zero-d bytes
    /// </summary>
    public void WriteZeroes(long length)
    {
        for (long i = 0; i < length; i++)
        {
            _writer.Write((byte)0);
        }
    }

    public void Write(byte[] buffer) => _writer.Write(buffer);

    public void Write(byte[] buffer, int index, int count) => _writer.Write(buffer, index, count);

    /// <summary>
    /// These are pre-stored segments
    /// </summary>
    public void WriteSegment(int index)
    {
        using var stream = GetType().Assembly.GetManifestResourceStream($"segment{index}.nes");
        stream.CopyTo(_writer.BaseStream);
    }

    /// <summary>
    /// Writes a string in ASCI form, including a trailing \0
    /// </summary>
    public void WriteString(string text)
    {
        int length = Encoding.GetByteCount(text);
        var bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            length = Encoding.GetBytes(text, 0, text.Length, bytes, 0);
            _writer.Write(bytes, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
        //TODO: I don't know if there is a 0 between each string, or if this denotes the end of the table
        _writer.Write((byte)0);
    }

    /// <summary>
    /// Writes a built-in method from NESLib
    /// </summary>
    public void WriteBuiltIn(string name)
    {
        switch (name)
        {
            case nameof(NESLib.pal_all):
                /*
                 * 8211	8517          	STA TEMP                      ; _pal_all
                 * 8213	8618          	STX TEMP+1                    
                 * 8215	A200          	LDX #$00                      
                 * 8217	A920          	LDA #$20                      
                 */
                Write(Instruction.STA_zpg, TEMP);
                Write(Instruction.STX_zpg, TEMP + 1);
                Write(Instruction.LDX, 0x00);
                Write(Instruction.LDA, 0x20);
                break;
            case "pal_copy":
                // NOTE: this one is internal, not in neslib.h
                /*
                 * 8219	8519          	STA $19                       ; pal_copy
                 * 821B	A000          	LDY #$00
                 */
                Write(Instruction.STA_zpg, 0x19);
                Write(Instruction.LDY, 0x00);
                break;
            case nameof(NESLib.pal_bg):
                /*
                 * 822B	8517          	STA TEMP                      ; _pal_bg
                 * 822D	8618          	STX TEMP+1                    
                 * 822F	A200          	LDX #$00                      
                 * 8231	A910          	LDA #$10                      
                 * 8233	D0E4          	BNE pal_copy
                 */
                Write(Instruction.STA_zpg, TEMP);
                Write(Instruction.STX_zpg, TEMP + 1);
                Write(Instruction.LDX, 0x00);
                Write(Instruction.LDA, 0x10);
                Write(Instruction.BNE_rel, 0xE4);
                break;
            case nameof(NESLib.pal_spr):
                /*
                 * 8235	8517          	STA TEMP                      ; _pal_spr
                 * 8237	8618          	STX TEMP+1                    
                 * 8239	A210          	LDX #$10                      
                 * 823B	8A            	TXA                           
                 * 823C	D0DB          	BNE pal_copy
                 */
                Write(Instruction.STA_zpg, TEMP);
                Write(Instruction.STX_zpg, TEMP + 1);
                Write(Instruction.LDX, 0x10);
                Write(Instruction.TXA_impl);
                Write(Instruction.BNE_rel, 0xDB);
                break;
            case nameof(NESLib.pal_col):
                /*
                 * 823E	8517          	STA TEMP                      ; _pal_col
                 * 8240	205085        	JSR popa                      
                 * 8243	291F          	AND #$1F                      
                 * 8245	AA            	TAX                           
                 * 8246	A517          	LDA TEMP                      
                 * 8248	9DC001        	STA $01C0,x                   
                 * 824B	E607          	INC PAL_UPDATE                
                 * 824D	60            	RTS
                 */
                Write(Instruction.STA_zpg, TEMP);
                Write(Instruction.JSR, 0x8550);
                Write(Instruction.AND, 0x1F);
                Write(Instruction.TAX_impl);
                Write(Instruction.LDA_zpg, TEMP);
                Write(Instruction.STA_abs_X, 0x01C0);
                Write(Instruction.INC_zpg, 0x07);
                Write(Instruction.RTS_impl);
                break;
            case nameof(NESLib.vram_adr):
                /*
                 * 83D4	8E0620        	STX $2006                     ; _vram_adr
                 * 83D7	8D0620        	STA $2006                     
                 * 83DA	60            	RTS
                 */
                Write(Instruction.STX_abs, 0x2006);
                Write(Instruction.STA_abs, 0x2006);
                Write(Instruction.RTS_impl);
                break;
            case nameof(NESLib.vram_write):
                /*
                 * 834F	8517          	STA TEMP                      ; _vram_write
                 * 8351	8618          	STX TEMP+1                    
                 * 8353	203A85        	JSR popax                     
                 * 8356	8519          	STA $19                       
                 * 8358	861A          	STX $1A                       
                 * 835A	A000          	LDY #$00                      
                 * 835C	B119          	LDA ($19),y                   
                 * 835E	8D0720        	STA $2007                     
                 * 8361	E619          	INC $19                       
                 * 8363	D002          	BNE $8367                     
                 * 8365	E61A          	INC $1A                       
                 * 8367	A517          	LDA TEMP                      
                 * 8369	D002          	BNE $836D                     
                 * 836B	C618          	DEC TEMP+1                    
                 * 836D	C617          	DEC TEMP                      
                 * 836F	A517          	LDA TEMP                      
                 * 8371	0518          	ORA TEMP+1                    
                 * 8373	D0E7          	BNE $835C                     
                 * 8375	60            	RTS
                 */
                Write(Instruction.STA_zpg, TEMP);
                Write(Instruction.STX_zpg, TEMP + 1);
                Write(Instruction.JSR, 0x853A);
                Write(Instruction.STA_zpg, 0x19);
                Write(Instruction.STX_zpg, 0x1A);
                Write(Instruction.LDY, 0x00);
                Write(Instruction.LDA_ind_Y, 0x19);
                Write(Instruction.STA_abs, 0x2007);
                Write(Instruction.INC_zpg, 0x19);
                Write(Instruction.BNE_rel, 0x02);
                Write(Instruction.INC_zpg, 0x1A);
                Write(Instruction.LDA_zpg, TEMP);
                Write(Instruction.BNE_rel, 0x02);
                Write(Instruction.DEC_zpg, TEMP + 1);
                Write(Instruction.DEC_zpg, TEMP);
                Write(Instruction.LDA_zpg, TEMP);
                Write(Instruction.ORA_zpg, TEMP + 1);
                Write(Instruction.BNE_rel, 0xE7);
                Write(Instruction.RTS_impl);
                break;
            case nameof(NESLib.ppu_on_all):
                //TODO: not sure if we should emit ppu_onoff at the same place
                /*
                 * 8289	A512          	LDA PPU_MASK_VAR              ; _ppu_on_all
                 * 828B	0918          	ORA #$18   
                 * 828D	8512          	STA PPU_MASK_VAR              ; ppu_onoff
                 * 828F	4CF082        	JMP _ppu_wait_nmi  
                 */
                Write(Instruction.LDA_zpg, 0x12);
                Write(Instruction.ORA, 0x18);
                Write(Instruction.STA_zpg, 0x12);
                Write(Instruction.JMP_abs, 0x82F0);
                break;
            case nameof(NESLib.ppu_wait_nmi):
                /*
                 * 82F0	A901          	LDA #$01                      ; _ppu_wait_nmi
                 * 82F2	8503          	STA VRAM_UPDATE               
                 * 82F4	A501          	LDA __STARTUP__               
                 * 82F6	C501          	CMP __STARTUP__               
                 * 82F8	F0FC          	BEQ $82F6                     
                 * 82FA	60            	RTS
                 */
                Write(Instruction.LDA, 0x01);
                Write(Instruction.STA_zpg, 0x03);
                Write(Instruction.LDA_zpg, 0x01);
                Write(Instruction.CMP_zpg, 0x01);
                Write(Instruction.BEQ_rel, 0xFC);
                Write(Instruction.RTS_impl);
                break;
            case "pusha":
                //NOTE: seems to be an internal subroutine
                /*
                * 85A2	A422          	LDY sp                        ; pusha
                * 85A4	F007          	BEQ $85AD                     
                * 85A6	C622          	DEC sp                        
                * 85A8	A000          	LDY #$00                      
                * 85AA	9122          	STA (sp),y                    
                * 85AC	60            	RTS                           
                */
                Write(Instruction.LDY_zpg, sp);
                Write(Instruction.BEQ_rel, 0x07);
                Write(Instruction.DEC_zpg, sp);
                Write(Instruction.LDY, 0x00);
                Write(Instruction.STA_ind_Y, sp);
                Write(Instruction.RTS_impl);
                break;
            case "popa":
                //NOTE: seems to be an internal subroutine
                /*
                 * 8592	A000          	LDY #$00                      ; popa
                 * 8594	B122          	LDA (sp),y                    
                 * 8596	E622          	INC sp                        
                 * 8598	F001          	BEQ $859B                     
                 * 859A	60            	RTS  
                 */
                Write(Instruction.LDY, 0x00);
                Write(Instruction.LDA_ind_Y, sp);
                Write(Instruction.INC_zpg, sp);
                Write(Instruction.BEQ_rel, 0x01);
                Write(Instruction.RTS_impl);
                break;
            default:
                throw new NotImplementedException($"{name} is not implemented!");
        }
    }

    /// <summary>
    /// Writes an "implied" instruction that has no argument
    /// </summary>
    public void Write(Instruction i) => _writer.Write((byte)i);

    /// <summary>
    /// Writes an instruction with a single byte argument
    /// </summary>
    public void Write (Instruction i, byte @byte)
    {
        _writer.Write((byte)i);
        _writer.Write(@byte);
    }

    /// <summary>
    /// Writes an instruction with an address argument (2 bytes)
    /// </summary>
    public void Write(Instruction i, ushort address)
    {
        _writer.Write((byte)i);
        _writer.Write(address);
    }

    public void Write()
    {
        WriteHeader();
        if (PRG_ROM != null)
            _writer.Write(PRG_ROM);
        if (CHR_ROM != null)
            _writer.Write(CHR_ROM);
        if (Trainer != null)
            _writer.Write(Trainer);
        if (INST_ROM != null)
            _writer.Write(INST_ROM);
    }

    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();
}
