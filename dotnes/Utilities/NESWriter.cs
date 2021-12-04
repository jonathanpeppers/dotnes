using System.Text;

namespace dotnes;

/// <summary>
/// Writes .nes files
/// * https://wiki.nesdev.org/w/index.php/INES
/// * https://bheisler.github.io/post/nes-rom-parser-with-nom/
/// </summary>
class NESWriter : IDisposable
{
    const int TEMP = 0x17;

    readonly BinaryWriter _writer;

    public NESWriter(Stream stream, bool leaveOpen = false) => _writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen);

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

    public void WriteHeader()
    {
        _writer.Write('N');
        _writer.Write('E');
        _writer.Write('S');
        _writer.Write('\x1A');
        // Size of PRG ROM in 16 KB units
        _writer.Write(checked ((byte)(PRG_ROM.Length / 16384)));
        // Size of CHR ROM in 8 KB units (Value 0 means the board uses CHR RAM)
        if (CHR_ROM != null)
            _writer.Write(checked((byte)(CHR_ROM.Length / 8192)));
        else
            _writer.Write((byte)0);
        _writer.Write(Flags6);
        _writer.Write(Flags7);
        _writer.Write(Flags8);
        _writer.Write(Flags9);
        _writer.Write(Flags10);
        // 5 bytes of padding
        for (int i = 0; i < 5; i++)
        {
            _writer.Write((byte)0);
        }
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
            // NOTE: this one is internal, not in neslib.h
            case "pal_copy":
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
