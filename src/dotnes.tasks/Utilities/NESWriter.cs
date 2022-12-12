﻿using System.Buffers;
using System.Text;
using System.Xml;

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

    protected const int VRAM_UPDATE = 0x03;
    protected const int NAME_UPD_ADR = 0x04;
    protected const int SCROLL_X = 0x0C;
    protected const int SCROLL_Y = 0x0D;
    protected const int TEMP = 0x17;
    protected const int sp = 0x22;
    protected const int PRG_FILEOFFS = 0x10;
    protected const int PPU_MASK_VAR = 0x12;
    protected const ushort pusha = 0x85A2;
    protected const ushort pushax = 0x85B8;
    protected const ushort popa = 0x8592;
    protected const ushort popax = 0x857C; //TODO: might should be 0x857F?
    protected const ushort pal_col = 0x823E;
    protected const ushort vram_adr = 0x83D4;
    protected const ushort vram_write = 0x834F;
    protected const ushort ppu_on_all = 0x8289;
    protected const ushort ppu_wait_nmi = 0x82F0;

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
        var name = $"segment{index}.nes";
        using var stream = GetType().Assembly.GetManifestResourceStream(name);
        if (stream == null)
            throw new InvalidOperationException($"Cannot load {name}!");
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
    /// Writes all the built-in methods from NESLib
    /// </summary>
    public void WriteBuiltIns()
    {
        WriteBuiltIn(nameof(NESLib.nmi_set_callback));
        WriteBuiltIn(nameof(NESLib.pal_all));
        WriteBuiltIn(nameof(NESLib.pal_copy));
        WriteBuiltIn(nameof(NESLib.pal_bg));
        WriteBuiltIn(nameof(NESLib.pal_spr));
        WriteBuiltIn(nameof(NESLib.pal_col));
        WriteBuiltIn(nameof(NESLib.pal_clear));
        WriteBuiltIn(nameof(NESLib.pal_spr_bright));
        WriteBuiltIn(nameof(NESLib.pal_bg_bright));
        WriteBuiltIn(nameof(NESLib.pal_bright));
        WriteBuiltIn(nameof(NESLib.ppu_off));
        WriteBuiltIn(nameof(NESLib.ppu_on_all));
        WriteBuiltIn(nameof(NESLib.ppu_onoff));
        WriteBuiltIn(nameof(NESLib.ppu_on_bg));
        WriteBuiltIn(nameof(NESLib.ppu_on_spr));
        WriteBuiltIn(nameof(NESLib.ppu_mask));
        WriteBuiltIn(nameof(NESLib.ppu_system));
        WriteBuiltIn(nameof(NESLib.get_ppu_ctrl_var));
        WriteBuiltIn(nameof(NESLib.set_ppu_ctrl_var));
        WriteBuiltIn(nameof(NESLib.oam_clear));
        WriteBuiltIn(nameof(NESLib.oam_size));
        WriteBuiltIn(nameof(NESLib.oam_hide_rest));
        WriteBuiltIn(nameof(NESLib.ppu_wait_frame));
        WriteBuiltIn(nameof(NESLib.ppu_wait_nmi));
        WriteBuiltIn(nameof(NESLib.scroll));
        WriteBuiltIn(nameof(NESLib.bank_spr));
        WriteBuiltIn(nameof(NESLib.bank_bg));
        WriteBuiltIn(nameof(NESLib.vram_write));
        WriteBuiltIn(nameof(NESLib.set_vram_update));
        WriteBuiltIn(nameof(NESLib.flush_vram_update));
        WriteBuiltIn(nameof(NESLib.vram_adr));
        WriteBuiltIn(nameof(NESLib.vram_put));
        WriteBuiltIn(nameof(NESLib.vram_fill));
        WriteBuiltIn(nameof(NESLib.vram_inc));
        WriteBuiltIn(nameof(NESLib.nesclock));
        WriteBuiltIn(nameof(NESLib.delay));
        WriteSegment(1);
    }

    /// <summary>
    /// Writes a built-in method from NESLib
    /// </summary>
    public void WriteBuiltIn(string name)
    {
        switch (name)
        {
            case nameof(NESLib.nmi_set_callback):
                /*
                 * 820C	8515          	STA NMICallback+1             ; _nmi_set_callback
                 * 820E	8616          	STX $16                       
                 * 8210	60            	RTS                           ; HandyRTS
                 */
                Write(NESInstruction.STA_zpg, 0x15);
                Write(NESInstruction.STX_zpg, 0x16);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.pal_all):
                /*
                 * 8211	8517          	STA TEMP                      ; _pal_all
                 * 8213	8618          	STX TEMP+1                    
                 * 8215	A200          	LDX #$00                      
                 * 8217	A920          	LDA #$20                      
                 */
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.STX_zpg, TEMP + 1);
                Write(NESInstruction.LDX, 0x00);
                Write(NESInstruction.LDA, 0x20);
                break;
            case nameof (NESLib.pal_copy):
                // NOTE: this one is internal, not in neslib.h
                /*
                 * 8219	8519          	STA $19                       ; pal_copy
                 * 821B	A000          	LDY #$00                      
                 * 821D	B117          	LDA (TEMP),y                  ; @0
                 * 821F	9DC001        	STA $01C0,x                   
                 * 8222	E8            	INX                           
                 * 8223	C8            	INY                           
                 * 8224	C619          	DEC $19                       
                 * 8226	D0F5          	BNE @0                        
                 * 8228	E607          	INC PAL_UPDATE                
                 * 822A	60            	RTS
                 */
                Write(NESInstruction.STA_zpg, 0x19);
                Write(NESInstruction.LDY, 0x00);
                Write(NESInstruction.LDA_ind_Y, TEMP);
                Write(NESInstruction.STA_abs_X, 0x01C0);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.INY_impl);
                Write(NESInstruction.DEC_zpg, 0x19);
                Write(NESInstruction.BNE_rel, 0xF5);
                Write(NESInstruction.INC_zpg, 0x07);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.pal_bg):
                /*
                 * 822B	8517          	STA TEMP                      ; _pal_bg
                 * 822D	8618          	STX TEMP+1                    
                 * 822F	A200          	LDX #$00                      
                 * 8231	A910          	LDA #$10                      
                 * 8233	D0E4          	BNE pal_copy
                 */
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.STX_zpg, TEMP + 1);
                Write(NESInstruction.LDX, 0x00);
                Write(NESInstruction.LDA, 0x10);
                Write(NESInstruction.BNE_rel, 0xE4);
                break;
            case nameof(NESLib.pal_spr):
                /*
                 * 8235	8517          	STA TEMP                      ; _pal_spr
                 * 8237	8618          	STX TEMP+1                    
                 * 8239	A210          	LDX #$10                      
                 * 823B	8A            	TXA                           
                 * 823C	D0DB          	BNE pal_copy
                 */
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.STX_zpg, TEMP + 1);
                Write(NESInstruction.LDX, 0x10);
                Write(NESInstruction.TXA_impl);
                Write(NESInstruction.BNE_rel, 0xDB);
                break;
            case nameof(NESLib.pal_col):
                /*
                 * 823E	8517          	STA TEMP                      ; _pal_col
                 * 8240	209285        	JSR popa                      
                 * 8243	291F          	AND #$1F                      
                 * 8245	AA            	TAX                           
                 * 8246	A517          	LDA TEMP                      
                 * 8248	9DC001        	STA $01C0,x                   
                 * 824B	E607          	INC PAL_UPDATE                
                 * 824D	60            	RTS
                 */
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.JSR, popa);
                Write(NESInstruction.AND, 0x1F);
                Write(NESInstruction.TAX_impl);
                Write(NESInstruction.LDA_zpg, TEMP);
                Write(NESInstruction.STA_abs_X, 0x01C0);
                Write(NESInstruction.INC_zpg, 0x07);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.pal_clear):
                /*
                 * 824E	A90F          	LDA #$0F                      ; _pal_clear
                 * 8250	A200          	LDX #$00                      
                 * 8252	9DC001        	STA $01C0,x                   
                 * 8255	E8            	INX                           
                 * 8256	E020          	CPX #$20                      
                 * 8258	D0F8          	BNE $8252                     
                 * 825A	8607          	STX PAL_UPDATE                
                 * 825C	60            	RTS 
                 */
                Write(NESInstruction.LDA, 0x0F);
                Write(NESInstruction.LDX, 0x00);
                Write(NESInstruction.STA_abs_X, 0x01C0);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.CPX, 0x20);
                Write(NESInstruction.BNE_rel, 0xF8);
                Write(NESInstruction.STX_zpg, 0x07);
                Write(NESInstruction.RTS_impl);
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
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.STX_zpg, TEMP + 1);
                Write(NESInstruction.JSR, popax);
                Write(NESInstruction.STA_zpg, 0x19);
                Write(NESInstruction.STX_zpg, 0x1A);
                Write(NESInstruction.LDY, 0x00);
                Write(NESInstruction.LDA_ind_Y, 0x19);
                Write(NESInstruction.STA_abs, 0x2007);
                Write(NESInstruction.INC_zpg, 0x19);
                Write(NESInstruction.BNE_rel, 0x02);
                Write(NESInstruction.INC_zpg, 0x1A);
                Write(NESInstruction.LDA_zpg, TEMP);
                Write(NESInstruction.BNE_rel, 0x02);
                Write(NESInstruction.DEC_zpg, TEMP + 1);
                Write(NESInstruction.DEC_zpg, TEMP);
                Write(NESInstruction.LDA_zpg, TEMP);
                Write(NESInstruction.ORA_zpg, TEMP + 1);
                Write(NESInstruction.BNE_rel, 0xE7);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.ppu_on_all):
                /*
                 * 8289	A512          	LDA PPU_MASK_VAR              ; _ppu_on_all
                 * 828B	0918          	ORA #$18   
                 */
                Write(NESInstruction.LDA_zpg, 0x12);
                Write(NESInstruction.ORA, 0x18);
                break;
            case nameof(NESLib.ppu_onoff):
                //TODO: not sure if we should emit ppu_onoff at the same place
                /*
                 * 828D	8512          	STA PPU_MASK_VAR              ; ppu_onoff
                 * 828F	4CF082        	JMP _ppu_wait_nmi  
                 */
                Write(NESInstruction.STA_zpg, 0x12);
                Write(NESInstruction.JMP_abs, 0x82F0);
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
                Write(NESInstruction.LDA, 0x01);
                Write(NESInstruction.STA_zpg, 0x03);
                Write(NESInstruction.LDA_zpg, 0x01);
                Write(NESInstruction.CMP_zpg, 0x01);
                Write(NESInstruction.BEQ_rel, 0xFC);
                Write(NESInstruction.RTS_impl);
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
                Write(NESInstruction.LDY_zpg, sp);
                Write(NESInstruction.BEQ_rel, 0x07);
                Write(NESInstruction.DEC_zpg, sp);
                Write(NESInstruction.LDY, 0x00);
                Write(NESInstruction.STA_ind_Y, sp);
                Write(NESInstruction.RTS_impl);
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
                Write(NESInstruction.LDY, 0x00);
                Write(NESInstruction.LDA_ind_Y, sp);
                Write(NESInstruction.INC_zpg, sp);
                Write(NESInstruction.BEQ_rel, 0x01);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.pal_spr_bright):
                /*
                 * 825D	AA            	TAX                           ; _pal_spr_bright
                 * 825E	BD2284        	LDA palBrightTableL,x         
                 * 8261	850A          	STA PAL_SPR_PTR               
                 * 8263	BD2B84        	LDA palBrightTableH,x         
                 * 8266	850B          	STA PAL_SPR_PTR+1             
                 * 8268	8507          	STA PAL_UPDATE                
                 * 826A	60            	RTS 
                 */
                Write(NESInstruction.TAX_impl);
                Write(NESInstruction.LDA_abs_X, 0x8422);
                Write(NESInstruction.STA_zpg, 0x0A);
                Write(NESInstruction.LDA_abs_X, 0x842B);
                Write(NESInstruction.STA_zpg, 0x0B);
                Write(NESInstruction.STA_zpg, 0x07);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.pal_bg_bright):
                /*
                 * 826B	AA            	TAX                           ; _pal_bg_bright
                 * 826C	BD2284        	LDA palBrightTableL,x         
                 * 826F	8508          	STA PAL_BG_PTR                
                 * 8271	BD2B84        	LDA palBrightTableH,x         
                 * 8274	8509          	STA PAL_BG_PTR+1              
                 * 8276	8507          	STA PAL_UPDATE                
                 * 8278	60            	RTS
                 */
                Write(NESInstruction.TAX_impl);
                Write(NESInstruction.LDA_abs_X, 0x8422);
                Write(NESInstruction.STA_zpg, 0x08);
                Write(NESInstruction.LDA_abs_X, 0x842B);
                Write(NESInstruction.STA_zpg, 0x09);
                Write(NESInstruction.STA_zpg, 0x07);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.pal_bright):
                /*
                 * 8279	205D82        	JSR _pal_spr_bright           ; _pal_bright
                 * 827C	8A            	TXA                           
                 * 827D	4C6B82        	JMP _pal_bg_bright
                 */
                Write(NESInstruction.JSR, 0x825D);
                Write(NESInstruction.TXA_impl);
                Write(NESInstruction.JMP_abs, 0x826B);
                break;
            case nameof(NESLib.ppu_off):
                /*
                 * 8280	A512          	LDA PPU_MASK_VAR              ; _ppu_off
                 * 8282	29E7          	AND #$E7                      
                 * 8284	8512          	STA PPU_MASK_VAR              
                 * 8286	4CF082        	JMP _ppu_wait_nmi 
                 */
                Write(NESInstruction.LDA_zpg, PPU_MASK_VAR);
                Write(NESInstruction.AND, 0xE7);
                Write(NESInstruction.STA_zpg, PPU_MASK_VAR);
                Write(NESInstruction.JMP_abs, 0x82F0);
                break;
            case nameof(NESLib.ppu_on_bg):
                /*
                 * 8292	A512          	LDA PPU_MASK_VAR              ; _ppu_on_bg
                 * 8294	0908          	ORA #$08                      
                 * 8296	D0F5          	BNE ppu_onoff 
                 */
                Write(NESInstruction.LDA_zpg, PPU_MASK_VAR);
                Write(NESInstruction.ORA, 0x08);
                Write(NESInstruction.BNE_rel, 0xF5);
                break;
            case nameof(NESLib.ppu_on_spr):
                /*
                 * 8298	A512          	LDA PPU_MASK_VAR              ; _ppu_on_spr
                 * 829A	0910          	ORA #$10                      
                 * 829C	D0EF          	BNE ppu_onoff
                 */
                Write(NESInstruction.LDA_zpg, PPU_MASK_VAR);
                Write(NESInstruction.ORA, 0x10);
                Write(NESInstruction.BNE_rel, 0xEF);
                break;
            case nameof(NESLib.ppu_mask):
                /*
                 * 829E	8512          	STA PPU_MASK_VAR              ; _ppu_mask
                 * 82A0	60            	RTS
                 */
                Write(NESInstruction.STA_zpg, PPU_MASK_VAR);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.ppu_system):
                /*
                 * 82A1	A500          	LDA __ZP_START__              ; _ppu_system
                 * 82A3	A200          	LDX #$00                      
                 * 82A5	60            	RTS
                 */
                Write(NESInstruction.LDA_zpg, 0);
                Write(NESInstruction.LDX, 0);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.get_ppu_ctrl_var):
                /*
                 * 82A6	A510          	LDA __PRG_FILEOFFS__          ; _get_ppu_ctrl_var
                 * 82A8	A200          	LDX #$00                      
                 * 82AA	60            	RTS
                 */
                Write(NESInstruction.LDA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.LDX, 0);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.set_ppu_ctrl_var):
                /*
                 * 82AB	8510          	STA __PRG_FILEOFFS__          ; _set_ppu_ctrl_var
                 * 82AD	60            	RTS
                 */
                Write(NESInstruction.STA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.oam_clear):
                /*
                 * 82AE	A200          	LDX #$00                      ; _oam_clear
                 * 82B0	A9FF          	LDA #$FF                      
                 * 82B2	9D0002        	STA $0200,x                   
                 * 82B5	E8            	INX                           
                 * 82B6	E8            	INX                           
                 * 82B7	E8            	INX                           
                 * 82B8	E8            	INX                           
                 * 82B9	D0F7          	BNE $82B2                     
                 * 82BB	60            	RTS
                 */
                Write(NESInstruction.LDX, 0x00);
                Write(NESInstruction.LDA, 0xFF);
                Write(NESInstruction.STA_abs_X, 0x0200);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.BNE_rel, 0xF7);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.oam_size):
                /*
                 * 82BC	0A            	ASL                           ; _oam_size
                 * 82BD	0A            	ASL                           
                 * 82BE	0A            	ASL                           
                 * 82BF	0A            	ASL                           
                 * 82C0	0A            	ASL                           
                 * 82C1	2920          	AND #$20                      
                 * 82C3	8517          	STA TEMP                      
                 * 82C5	A510          	LDA __PRG_FILEOFFS__          
                 * 82C7	29DF          	AND #$DF                      
                 * 82C9	0517          	ORA TEMP                      
                 * 82CB	8510          	STA __PRG_FILEOFFS__          
                 * 82CD	60            	RTS
                 */
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.AND, 0x20);
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.LDA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.AND, 0xDF);
                Write(NESInstruction.ORA_zpg, TEMP);
                Write(NESInstruction.STA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.oam_hide_rest):
                /*
                 * 82CE	AA            	TAX                           ; _oam_hide_rest
                 * 82CF	A9F0          	LDA #$F0                      
                 * 82D1	9D0002        	STA $0200,x                   
                 * 82D4	E8            	INX                           
                 * 82D5	E8            	INX                           
                 * 82D6	E8            	INX                           
                 * 82D7	E8            	INX                           
                 * 82D8	D0F7          	BNE $82D1                     
                 * 82DA	60            	RTS
                 */
                Write(NESInstruction.TAX_impl);
                Write(NESInstruction.LDA, 0xF0);
                Write(NESInstruction.STA_abs_X, 0x0200);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.BNE_rel, 0xF7);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.ppu_wait_frame):
                /*
                 * 82DB	A901          	LDA #$01                      ; _ppu_wait_frame
                 * 82DD	8503          	STA VRAM_UPDATE               
                 * 82DF	A501          	LDA __STARTUP__               
                 * 82E1	C501          	CMP __STARTUP__               
                 * 82E3	F0FC          	BEQ $82E1                     
                 * 82E5	A500          	LDA __ZP_START__              
                 * 82E7	F006          	BEQ @3                        
                 * 82E9	A502          	LDA NES_PRG_BANKS             
                 * 82EB	C905          	CMP #$05                      
                 * 82ED	F0FA          	BEQ $82E9                     
                 * 82EF	60            	RTS                           ; @3
                 */
                Write(NESInstruction.LDA, 0x01);
                Write(NESInstruction.STA_zpg, VRAM_UPDATE);
                Write(NESInstruction.LDA_zpg, 0x01);
                Write(NESInstruction.CMP_zpg, 0x01);
                Write(NESInstruction.BEQ_rel, 0xFC);
                Write(NESInstruction.LDA_zpg, 0x00);
                Write(NESInstruction.BEQ_rel, 0x06);
                Write(NESInstruction.LDA_zpg, 0x02);
                Write(NESInstruction.CMP, 0x05);
                Write(NESInstruction.BEQ_rel, 0xFA);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.scroll):
                /*
                 * 82FB	8517          	STA TEMP                      ; _scroll
                 * 82FD	8A            	TXA                           
                 * 82FE	D00E          	BNE $830E                     
                 * 8300	A517          	LDA TEMP                      
                 * 8302	C9F0          	CMP #$F0                      
                 * 8304	B008          	BCS $830E                     
                 * 8306	850D          	STA SCROLL_Y                  
                 * 8308	A900          	LDA #$00                      
                 * 830A	8517          	STA TEMP                      
                 * 830C	F00B          	BEQ $8319                     
                 * 830E	38            	SEC                           
                 * 830F	A517          	LDA TEMP                      
                 * 8311	E9F0          	SBC #$F0                      
                 * 8313	850D          	STA SCROLL_Y                  
                 * 8315	A902          	LDA #$02                      
                 * 8317	8517          	STA TEMP                      
                 * 8319	207F85        	JSR popax                     
                 * 831C	850C          	STA SCROLL_X                  
                 * 831E	8A            	TXA                           
                 * 831F	2901          	AND #$01                      
                 * 8321	0517          	ORA TEMP                      
                 * 8323	8517          	STA TEMP                      
                 * 8325	A510          	LDA __PRG_FILEOFFS__          
                 * 8327	29FC          	AND #$FC                      
                 * 8329	0517          	ORA TEMP                      
                 * 832B	8510          	STA __PRG_FILEOFFS__          
                 * 832D	60            	RTS
                 */
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.TXA_impl);
                Write(NESInstruction.BNE_rel, 0x0E);
                Write(NESInstruction.LDA_zpg, TEMP);
                Write(NESInstruction.CMP, 0xF0);
                Write(NESInstruction.BCS, 0x08);
                Write(NESInstruction.STA_zpg, SCROLL_Y);
                Write(NESInstruction.LDA, 0x00);
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.BEQ_rel, 0x0B);
                Write(NESInstruction.SEC_impl); // 830E
                Write(NESInstruction.LDA_zpg, TEMP);
                Write(NESInstruction.SBC, 0xF0);
                Write(NESInstruction.STA_zpg, SCROLL_Y); // 8313
                Write(NESInstruction.LDA, 0x02);
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.JSR, popax);
                Write(NESInstruction.STA_zpg, SCROLL_X); // 831C
                Write(NESInstruction.TXA_impl);
                Write(NESInstruction.AND, 0x01);
                Write(NESInstruction.ORA_zpg, TEMP);
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.LDA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.AND, 0xFC);
                Write(NESInstruction.ORA_zpg, TEMP);
                Write(NESInstruction.STA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.bank_spr):
                /*
                 * 832E	2901          	AND #$01                      ; _bank_spr
                 * 8330	0A            	ASL                           
                 * 8331	0A            	ASL                           
                 * 8332	0A            	ASL                           
                 * 8333	8517          	STA TEMP                      
                 * 8335	A510          	LDA __PRG_FILEOFFS__          
                 * 8337	29F7          	AND #$F7                      
                 * 8339	0517          	ORA TEMP                      
                 * 833B	8510          	STA __PRG_FILEOFFS__          
                 * 833D	60            	RTS
                 */
                Write(NESInstruction.AND, 0x01);
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.LDA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.AND, 0xF7);
                Write(NESInstruction.ORA_zpg, TEMP);
                Write(NESInstruction.STA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.bank_bg):
                /*
                 * 833E	2901          	AND #$01                      ; _bank_bg
                 * 8340	0A            	ASL                           
                 * 8341	0A            	ASL                           
                 * 8342	0A            	ASL                           
                 * 8343	0A            	ASL                           
                 * 8344	8517          	STA TEMP                      
                 * 8346	A510          	LDA __PRG_FILEOFFS__          
                 * 8348	29EF          	AND #$EF                      
                 * 834A	0517          	ORA TEMP                      
                 * 834C	8510          	STA __PRG_FILEOFFS__          
                 * 834E	60            	RTS
                 */
                Write(NESInstruction.AND, 0x01);
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.ASL_A);
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.LDA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.AND, 0xEF);
                Write(NESInstruction.ORA_zpg, TEMP);
                Write(NESInstruction.STA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.set_vram_update):
                /*
                 * 8376	8504          	STA NAME_UPD_ADR              ; _set_vram_update
                 * 8378	8605          	STX NAME_UPD_ADR+1            
                 * 837A	0505          	ORA NAME_UPD_ADR+1            
                 * 837C	8506          	STA NAME_UPD_ENABLE           
                 * 837E	60            	RTS
                 */
                Write(NESInstruction.STA_zpg, NAME_UPD_ADR);
                Write(NESInstruction.STX_zpg, NAME_UPD_ADR + 1);
                Write(NESInstruction.ORA_zpg, NAME_UPD_ADR + 1);
                Write(NESInstruction.STA_zpg, NAME_UPD_ADR + 2);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.flush_vram_update):
                /*
                 * 837F	8504          	STA NAME_UPD_ADR              ; _flush_vram_update
                 * 8381	8605          	STX NAME_UPD_ADR+1            
                 * 8383	A000          	LDY #$00                      ; _flush_vram_update_nmi
                 */
                Write(NESInstruction.STA_zpg, NAME_UPD_ADR);
                Write(NESInstruction.STX_zpg, NAME_UPD_ADR + 1);
                Write(NESInstruction.LDY, 0);

                /*
                 * 8385	B104          	LDA (NAME_UPD_ADR),y          ; @updName
                 * 8387	C8            	INY                           
                 * 8388	C940          	CMP #$40                      
                 * 838A	B012          	BCS @updNotSeq                
                 * 838C	8D0620        	STA $2006                     
                 * 838F	B104          	LDA (NAME_UPD_ADR),y          
                 * 8391	C8            	INY                           
                 * 8392	8D0620        	STA $2006                     
                 * 8395	B104          	LDA (NAME_UPD_ADR),y          
                 * 8397	C8            	INY                           
                 * 8398	8D0720        	STA $2007                     
                 * 839B	4C8583        	JMP @updName                  
                 */
                Write(NESInstruction.LDA_ind_Y, NAME_UPD_ADR);
                Write(NESInstruction.INY_impl);
                Write(NESInstruction.CMP, 0x40);
                Write(NESInstruction.BCS, 0x12);
                Write(NESInstruction.STA_abs, NESLib.NTADR_A(6, 0));
                Write(NESInstruction.LDA_ind_Y, NAME_UPD_ADR);
                Write(NESInstruction.INY_impl);
                Write(NESInstruction.STA_abs, NESLib.NTADR_A(6, 0));
                Write(NESInstruction.LDA_ind_Y, NAME_UPD_ADR);
                Write(NESInstruction.INY_impl);
                Write(NESInstruction.STA_abs, NESLib.NTADR_A(7, 0));
                Write(NESInstruction.JMP_abs, 0x8385);

                /*
                 * 839E	AA            	TAX                           ; @updNotSeq
                 * 839F	A510          	LDA __PRG_FILEOFFS__          
                 * 83A1	E080          	CPX #$80                      
                 * 83A3	9008          	BCC @updHorzSeq               
                 * 83A5	E0FF          	CPX #$FF                      
                 * 83A7	F02A          	BEQ @updDone                  
                 */
                Write(NESInstruction.TAX_impl);
                Write(NESInstruction.LDA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.CPX, 0x80);
                Write(NESInstruction.BCC, 0x08);
                Write(NESInstruction.CPX, 0xFF);
                Write(NESInstruction.BEQ_rel, 0x2A);

                /*
                 * 83A9	0904          	ORA #$04                      ; @updVertSeq
                 * 83AB	D002          	BNE @updNameSeq               
                 * 83AD	29FB          	AND #$FB                      ; @updHorzSeq
                 * 83AF	8D0020        	STA $2000                     ; @updNameSeq
                 * 83B2	8A            	TXA                           
                 * 83B3	293F          	AND #$3F                      
                 * 83B5	8D0620        	STA $2006                     
                 * 83B8	B104          	LDA (NAME_UPD_ADR),y          
                 * 83BA	C8            	INY                           
                 * 83BB	8D0620        	STA $2006                     
                 * 83BE	B104          	LDA (NAME_UPD_ADR),y          
                 * 83C0	C8            	INY                           
                 * 83C1	AA            	TAX                           
                 */
                Write(NESInstruction.ORA, 0x04);
                Write(NESInstruction.BNE_rel, 0x02);
                Write(NESInstruction.AND, 0xFB);
                Write(NESInstruction.STA_abs, NESLib.NAMETABLE_A);
                Write(NESInstruction.TXA_impl);
                Write(NESInstruction.AND, 0x3F);
                Write(NESInstruction.STA_abs, NESLib.NTADR_A(6, 0));
                Write(NESInstruction.LDA_ind_Y, NAME_UPD_ADR);
                Write(NESInstruction.INY_impl);
                Write(NESInstruction.STA_abs, NESLib.NTADR_A(6, 0));
                Write(NESInstruction.LDA_ind_Y, NAME_UPD_ADR);
                Write(NESInstruction.INY_impl);
                Write(NESInstruction.TAX_impl);

                /*
                 * 83C2	B104          	LDA (NAME_UPD_ADR),y          ; @updNameLoop
                 * 83C4	C8            	INY                           
                 * 83C5	8D0720        	STA $2007                     
                 * 83C8	CA            	DEX                           
                 * 83C9	D0F7          	BNE @updNameLoop              
                 * 83CB	A510          	LDA __PRG_FILEOFFS__          
                 * 83CD	8D0020        	STA $2000                     
                 * 83D0	4C8583        	JMP @updName                  
                 * 83D3	60            	RTS                           ; @updDone
                 */
                Write(NESInstruction.LDA_ind_Y, NAME_UPD_ADR);
                Write(NESInstruction.INY_impl);
                Write(NESInstruction.STA_abs, NESLib.NTADR_A(7, 0));
                Write(NESInstruction.DEX_impl);
                Write(NESInstruction.BNE_rel, 0xF7);
                Write(NESInstruction.LDA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.STA_abs, NESLib.NAMETABLE_A);
                Write(NESInstruction.JMP_abs, 0x8385);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.vram_adr):
                /*
                 * 83D4	8E0620        	STX $2006                     ; _vram_adr
                 * 83D7	8D0620        	STA $2006                     
                 * 83DA	60            	RTS
                 */
                Write(NESInstruction.STX_abs, NESLib.NTADR_A(6, 0));
                Write(NESInstruction.STA_abs, NESLib.NTADR_A(6, 0));
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.vram_put):
                /*
                 * 83DB	8D0720        	STA $2007                     ; _vram_put
                 * 83DE	60            	RTS  
                 */
                Write(NESInstruction.STA_abs, NESLib.NTADR_A(7, 0));
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.vram_fill):
                /*
                 * 83DF	8519          	STA $19                       ; _vram_fill
                 * 83E1	861A          	STX $1A                       
                 * 83E3	209585        	JSR popa                      
                 * 83E6	A61A          	LDX $1A                       
                 * 83E8	F00C          	BEQ $83F6                     
                 * 83EA	A200          	LDX #$00                      
                 * 83EC	8D0720        	STA $2007                     
                 * 83EF	CA            	DEX                           
                 * 83F0	D0FA          	BNE $83EC                     
                 * 83F2	C61A          	DEC $1A                       
                 * 83F4	D0F6          	BNE $83EC                     
                 * 83F6	A619          	LDX $19                       
                 * 83F8	F006          	BEQ @4                        
                 * 83FA	8D0720        	STA $2007                     
                 * 83FD	CA            	DEX                           
                 * 83FE	D0FA          	BNE $83FA                     
                 * 8400	60            	RTS                           ; @4
                 */
                Write(NESInstruction.STA_zpg, 0x19);
                Write(NESInstruction.STX_zpg, 0x1A);
                Write(NESInstruction.JSR, popa);
                Write(NESInstruction.LDX_zpg, 0x1A);
                Write(NESInstruction.BEQ_rel, 0x0C);
                Write(NESInstruction.LDX, 0x00);
                Write(NESInstruction.STA_abs, NESLib.NTADR_A(7, 0));
                Write(NESInstruction.DEX_impl);
                Write(NESInstruction.BNE_rel, 0xFA);
                Write(NESInstruction.DEC_zpg, 0x1A);
                Write(NESInstruction.BNE_rel, 0xF6);
                Write(NESInstruction.LDX_zpg, 0x19);
                Write(NESInstruction.BEQ_rel, 0x06);
                Write(NESInstruction.STA_abs, NESLib.NTADR_A(7, 0));
                Write(NESInstruction.DEX_impl);
                Write(NESInstruction.BNE_rel, 0xFA);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.vram_inc):
                /*
                 * 8401	0900          	ORA #$00                      ; _vram_inc
                 * 8403	F002          	BEQ $8407                     
                 * 8405	A904          	LDA #$04                      
                 * 8407	8517          	STA TEMP                      
                 * 8409	A510          	LDA __PRG_FILEOFFS__          
                 * 840B	29FB          	AND #$FB                      
                 * 840D	0517          	ORA TEMP                      
                 * 840F	8510          	STA __PRG_FILEOFFS__          
                 * 8411	8D0020        	STA $2000                     
                 * 8414	60            	RTS 
                 */
                Write(NESInstruction.ORA, 0x00);
                Write(NESInstruction.BEQ_rel, 0x02);
                Write(NESInstruction.LDA, 0x04);
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.LDA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.AND, 0xFB);
                Write(NESInstruction.ORA_zpg, TEMP);
                Write(NESInstruction.STA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.STA_abs, NESLib.NAMETABLE_A);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.nesclock):
                /*
                 * 8415	A501          	LDA __STARTUP__               ; _nesclock
                 * 8417	A200          	LDX #$00                      
                 * 8419	60            	RTS
                 */
                Write(NESInstruction.LDA_zpg, 0x01);
                Write(NESInstruction.LDX, 0x00);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.delay):
                /*
                 * 841A	AA            	TAX                           ; _delay
                 * 841B	20F082        	JSR _ppu_wait_nmi             
                 * 841E	CA            	DEX                           
                 * 841F	D0FA          	BNE _delay+1                  
                 * 8421	60            	RTS
                 */
                Write(NESInstruction.TAX_impl);
                Write(NESInstruction.JSR, ppu_wait_nmi);
                Write(NESInstruction.DEX_impl);
                Write(NESInstruction.BNE_rel, 0xFA);
                Write(NESInstruction.RTS_impl);
                break;
            default:
                throw new NotImplementedException($"{name} is not implemented!");
        }
    }

    /// <summary>
    /// Writes an "implied" instruction that has no argument
    /// </summary>
    public void Write(NESInstruction i) => _writer.Write((byte)i);

    /// <summary>
    /// Writes an instruction with a single byte argument
    /// </summary>
    public void Write (NESInstruction i, byte @byte)
    {
        _writer.Write((byte)i);
        _writer.Write(@byte);
    }

    /// <summary>
    /// Writes an instruction with an address argument (2 bytes)
    /// </summary>
    public void Write(NESInstruction i, ushort address)
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
