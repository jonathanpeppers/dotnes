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

    protected const int ZP_START = 0x00;
    protected const int STARTUP = 0x01;
    protected const int NES_PRG_BANKS = 0x02;
    protected const int VRAM_UPDATE = 0x03;
    protected const int NAME_UPD_ADR = 0x04;
    protected const int NAME_UPD_ENABLE = 0x06;
    protected const int PAL_UPDATE = 0x07;
    protected const int PAL_BG_PTR = 0x08;
    protected const int PAL_SPR_PTR = 0x0A;
    protected const int SCROLL_X = 0x0C;
    protected const int SCROLL_Y = 0x0D;
    protected const int TEMP = 0x17;
    protected const int sp = 0x22;
    protected const int ptr1 = 0x2A;
    protected const int ptr2 = 0x2C;
    protected const int tmp1 = 0x32;
    protected const int PRG_FILEOFFS = 0x10;
    protected const int PPU_MASK_VAR = 0x12;
    protected const ushort OAM_BUF = 0x0200;
    protected const ushort PAL_BUF = 0x01C0;
    protected const ushort condes = 0x0300;
    protected const ushort PPU_CTRL = 0x2000;
    protected const ushort PPU_MASK = 0x2001;
    protected const ushort PPU_STATUS = 0x2002;
    protected const ushort PPU_OAM_ADDR = 0x2003;
    protected const ushort PPU_OAM_DATA = 0x2004;
    protected const ushort PPU_SCROLL = 0x2005;
    protected const ushort PPU_ADDR = 0x2006;
    protected const ushort PPU_DATA = 0x2007;
    protected const ushort DMC_FREQ = 0x4010;
    protected const ushort PPU_OAM_DMA = 0x4014;
    protected const ushort PPU_FRAMECNT = 0x4017;
    protected const ushort skipNtsc = 0x81F9;
    protected const ushort pal_col = 0x823E;
    protected const ushort vram_adr = 0x83D4;
    protected const ushort vram_write = 0x834F;
    protected const ushort ppu_on_all = 0x8289;
    protected const ushort ppu_wait_nmi = 0x82F0;
    protected const ushort updName = 0x8385;
    protected const ushort palBrightTableL = 0x8422;
    protected const ushort palBrightTableH = 0x842B;
    protected const ushort pusha = 0x85A2;
    protected const ushort pushax = 0x85B8;
    protected const ushort popa = 0x8592;
    protected const ushort popax = 0x857C; //TODO: might should be 0x857F?

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
        Write_exit();
        Write_initPPU();
        Write_clearPalette();
        Write_clearVRAM();
        Write_clearRAM();
        Write_waitSync3();
        Write_detectNTSC();
        Write_nmi();
        Write_doUpdate();
        Write_updPal();
        Write_updVRAM();
        Write_skipUpd();
        Write_skipAll();
        Write_skipNtsc();
        Write_irq();
        Write_nmi_set_callback();
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
        Write(NESLib.palBrightTableL);
        Write(NESLib.palBrightTable0);
        Write(NESLib.palBrightTable1);
        Write(NESLib.palBrightTable2);
        Write(NESLib.palBrightTable3);
        Write(NESLib.palBrightTable4);
        Write(NESLib.palBrightTable5);
        Write(NESLib.palBrightTable6);
        Write(NESLib.palBrightTable7);
        Write(NESLib.palBrightTable8);
        Write_initlib();
    }

    public void WriteDestructorTable()
    {
        /*
         * 8602	8D0E03        	STA $030E                     ; __DESTRUCTOR_TABLE__
         * 8605	8E0F03        	STX $030F                     
         * 8608	8D1503        	STA $0315                     
         * 860B	8E1603        	STX $0316                     
         * 860E	88            	DEY                           
         * 860F	B9FFFF        	LDA $FFFF,y                   
         * 8612	8D1F03        	STA $031F                     
         * 8615	88            	DEY                           
         * 8616	B9FFFF        	LDA $FFFF,y                   
         * 8619	8D1E03        	STA $031E                     
         * 861C	8C2103        	STY $0321                     
         * 861F	20FFFF        	JSR $FFFF                     
         * 8622	A0FF          	LDY #$FF                      
         * 8624	D0E8          	BNE $860E                     
         * 8626	60            	RTS
         */
        Write(NESInstruction.STA_abs, 0x030E);
        Write(NESInstruction.STX_abs, 0x030F);
        Write(NESInstruction.STA_abs, 0x0315);
        Write(NESInstruction.STX_abs, 0x0316);
        Write(NESInstruction.DEY_impl);
        Write(NESInstruction.LDA_abs_y, 0xFFFF);
        Write(NESInstruction.STA_abs, 0x031F);
        Write(NESInstruction.DEY_impl);
        Write(NESInstruction.LDA_abs_y, 0xFFFF);
        Write(NESInstruction.STA_abs, 0x031E);
        Write(NESInstruction.STY_abs, 0x0321);
        Write(NESInstruction.JSR, 0xFFFF);
        Write(NESInstruction.LDY, 0xFF);
        Write(NESInstruction.BNE_rel, 0xE8);
        Write(NESInstruction.RTS_impl);
    }

    /// <summary>
    /// These are any subroutines after our `static void main()` method
    /// </summary>
    public void WriteFinalBuiltIns()
    {
        Write_donelib();
        Write_copydata();
        Write_popax();
        Write_incsp2();
        Write_popa();
        Write_pusha();
        Write_pushax();
        Write_zerobss();
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
                Write(NESInstruction.STA_zpg, TEMP);
                Write(NESInstruction.STX_zpg, TEMP + 1);
                Write(NESInstruction.LDX, 0x00);
                Write(NESInstruction.LDA, 0x20);
                break;
            case nameof (NESLib.pal_copy):
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
                Write(NESInstruction.STA_abs_X, PAL_BUF);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.INY_impl);
                Write(NESInstruction.DEC_zpg, 0x19);
                Write(NESInstruction.BNE_rel, 0xF5);
                Write(NESInstruction.INC_zpg, PAL_UPDATE);
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
                Write(NESInstruction.STA_abs_X, PAL_BUF);
                Write(NESInstruction.INC_zpg, PAL_UPDATE);
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
                Write(NESInstruction.STA_abs_X, PAL_BUF);
                Write(NESInstruction.INX_impl);
                Write(NESInstruction.CPX, 0x20);
                Write(NESInstruction.BNE_rel, 0xF8);
                Write(NESInstruction.STX_zpg, PAL_UPDATE);
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
                Write(NESInstruction.LDA_abs_X, palBrightTableL);
                Write(NESInstruction.STA_zpg, PAL_SPR_PTR);
                Write(NESInstruction.LDA_abs_X, palBrightTableH);
                Write(NESInstruction.STA_zpg, PAL_SPR_PTR + 1);
                Write(NESInstruction.STA_zpg, PAL_UPDATE);
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
                Write(NESInstruction.LDA_abs_X, palBrightTableL);
                Write(NESInstruction.STA_zpg, PAL_BG_PTR);
                Write(NESInstruction.LDA_abs_X, palBrightTableH);
                Write(NESInstruction.STA_zpg, PAL_BG_PTR + 1);
                Write(NESInstruction.STA_zpg, PAL_UPDATE);
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
                Write(NESInstruction.JMP_abs, ppu_wait_nmi);
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
                Write(NESInstruction.JMP_abs, ppu_wait_nmi);
                break;
            case nameof(NESLib.ppu_on_bg):
                /*
                 * 8292	A512          	LDA PPU_MASK_VAR              ; _ppu_on_bg
                 * 8294	0908          	ORA #$08                      
                 * 8296	D0F5          	BNE ppu_onoff 
                 */
                Write(NESInstruction.LDA_zpg, PPU_MASK_VAR);
                Write(NESInstruction.ORA, PAL_BG_PTR);
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
                Write(NESInstruction.LDA_zpg, ZP_START);
                Write(NESInstruction.LDX, 0x00);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.get_ppu_ctrl_var):
                /*
                 * 82A6	A510          	LDA __PRG_FILEOFFS__          ; _get_ppu_ctrl_var
                 * 82A8	A200          	LDX #$00                      
                 * 82AA	60            	RTS
                 */
                Write(NESInstruction.LDA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.LDX, 0x00);
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
                Write(NESInstruction.LDA_zpg, STARTUP);
                Write(NESInstruction.CMP_zpg, STARTUP);
                Write(NESInstruction.BEQ_rel, 0xFC);
                Write(NESInstruction.LDA_zpg, ZP_START);
                Write(NESInstruction.BEQ_rel, 0x06);
                Write(NESInstruction.LDA_zpg, NES_PRG_BANKS);
                Write(NESInstruction.CMP, 0x05);
                Write(NESInstruction.BEQ_rel, 0xFA);
                Write(NESInstruction.RTS_impl);
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
                Write(NESInstruction.LDA_zpg, STARTUP);
                Write(NESInstruction.CMP_zpg, STARTUP);
                Write(NESInstruction.BEQ_rel, 0xFC);
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
                Write(NESInstruction.BCS, PAL_BG_PTR);
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
                Write(NESInstruction.STA_abs, PPU_DATA);
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
                Write(NESInstruction.STA_abs, PPU_ADDR);
                Write(NESInstruction.LDA_ind_Y, NAME_UPD_ADR);
                Write(NESInstruction.INY_impl);
                Write(NESInstruction.STA_abs, PPU_ADDR);
                Write(NESInstruction.LDA_ind_Y, NAME_UPD_ADR);
                Write(NESInstruction.INY_impl);
                Write(NESInstruction.STA_abs, PPU_DATA);
                Write(NESInstruction.JMP_abs, updName);

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
                Write(NESInstruction.BCC, PAL_BG_PTR);
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
                Write(NESInstruction.STA_abs, PPU_CTRL);
                Write(NESInstruction.TXA_impl);
                Write(NESInstruction.AND, 0x3F);
                Write(NESInstruction.STA_abs, PPU_ADDR);
                Write(NESInstruction.LDA_ind_Y, NAME_UPD_ADR);
                Write(NESInstruction.INY_impl);
                Write(NESInstruction.STA_abs, PPU_ADDR);
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
                Write(NESInstruction.STA_abs, PPU_DATA);
                Write(NESInstruction.DEX_impl);
                Write(NESInstruction.BNE_rel, 0xF7);
                Write(NESInstruction.LDA_zpg, PRG_FILEOFFS);
                Write(NESInstruction.STA_abs, PPU_CTRL);
                Write(NESInstruction.JMP_abs, updName);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.vram_adr):
                /*
                 * 83D4	8E0620        	STX $2006                     ; _vram_adr
                 * 83D7	8D0620        	STA $2006                     
                 * 83DA	60            	RTS
                 */
                Write(NESInstruction.STX_abs, PPU_ADDR);
                Write(NESInstruction.STA_abs, PPU_ADDR);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.vram_put):
                /*
                 * 83DB	8D0720        	STA $2007                     ; _vram_put
                 * 83DE	60            	RTS  
                 */
                Write(NESInstruction.STA_abs, PPU_DATA);
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
                Write(NESInstruction.STA_abs, PPU_DATA);
                Write(NESInstruction.DEX_impl);
                Write(NESInstruction.BNE_rel, 0xFA);
                Write(NESInstruction.DEC_zpg, 0x1A);
                Write(NESInstruction.BNE_rel, 0xF6);
                Write(NESInstruction.LDX_zpg, 0x19);
                Write(NESInstruction.BEQ_rel, 0x06);
                Write(NESInstruction.STA_abs, PPU_DATA);
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
                Write(NESInstruction.STA_abs, PPU_CTRL);
                Write(NESInstruction.RTS_impl);
                break;
            case nameof(NESLib.nesclock):
                /*
                 * 8415	A501          	LDA __STARTUP__               ; _nesclock
                 * 8417	A200          	LDX #$00                      
                 * 8419	60            	RTS
                 */
                Write(NESInstruction.LDA_zpg, STARTUP);
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

    void Write_exit()
    {
        /*
        * https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L111
        * sei
        * ldx #$ff
        * txs
        * inx
        * stx PPU_MASK
        * stx DMC_FREQ
        * stx PPU_CTRL		;no NMI
        */
        Write(NESInstruction.SEI_impl);
        Write(NESInstruction.LDX, 0xFF);
        Write(NESInstruction.TXS_impl);
        Write(NESInstruction.INX_impl);
        Write(NESInstruction.STX_abs, PPU_MASK);
        Write(NESInstruction.STX_abs, DMC_FREQ);
        Write(NESInstruction.STX_abs, PPU_CTRL);
    }

    void Write_initPPU()
    {
        /*
         * https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L121
         *     bit PPU_STATUS
         * @1:
         *     bit PPU_STATUS
         *     bpl @1
         * @2:
         *     bit PPU_STATUS
         *     bpl @2
         * 
         * ; no APU frame counter IRQs
         *     lda #$40
         *     sta PPU_FRAMECNT
         */
        Write(NESInstruction.BIT_abs, PPU_STATUS);
        Write(NESInstruction.BIT_abs, PPU_STATUS);
        Write(NESInstruction.BPL, 0xFB);
        Write(NESInstruction.BIT_abs, PPU_STATUS);
        Write(NESInstruction.BPL, 0xFB);
        Write(NESInstruction.LDA, 0x40);
        Write(NESInstruction.STA_abs, PPU_FRAMECNT);
    }

    void Write_clearPalette()
    {
        /*
         * https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L135
         *     lda #$3f
         *     sta PPU_ADDR
         *     stx PPU_ADDR
         *     lda #$0f
         *     ldx #$20
         * @1:
         *     sta PPU_DATA
         *     dex
         *     bne @1
         */
        Write(NESInstruction.LDA, 0x3F);
        Write(NESInstruction.STA_abs, PPU_ADDR);
        Write(NESInstruction.STX_abs, PPU_ADDR);
        Write(NESInstruction.LDA, 0x0F);
        Write(NESInstruction.LDX, 0x20);
        Write(NESInstruction.STA_abs, PPU_DATA);
        Write(NESInstruction.DEX_impl);
        Write(NESInstruction.BNE_rel, 0xFA);
    }

    void Write_clearVRAM()
    {
        /*
         * https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L147
         *     txa
         *     ldy #$20
         *     sty PPU_ADDR
         *     sta PPU_ADDR
         *     ldy #$10
         * @1:
         *     sta PPU_DATA
         *     inx
         *     bne @1
         *     dey
         *     bne @1
         */
        Write(NESInstruction.TXA_impl);
        Write(NESInstruction.LDY, 0x20);
        Write(NESInstruction.STY_abs, PPU_ADDR);
        Write(NESInstruction.STA_abs, PPU_ADDR);
        Write(NESInstruction.LDY, 0x10);
        Write(NESInstruction.STA_abs, PPU_DATA);
        Write(NESInstruction.INX_impl);
        Write(NESInstruction.BNE_rel, 0xFA);
        Write(NESInstruction.DEY_impl);
        Write(NESInstruction.BNE_rel, 0xF7);
    }

    void Write_clearRAM()
    {
        /*
        * https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L161
        * clearRAM:
        *     txa
        * @1:
        *     sta $000,x
        *     sta $100,x
        *     sta $200,x
        *     sta $300,x
        *     sta $400,x
        *     sta $500,x
        *     sta $600,x
        *     sta $700,x
        *     inx
        *     bne @1
        * 
        *     lda #4
        *     jsr _pal_bright
        *     jsr _pal_clear
        *     jsr _oam_clear
        * 
        *     jsr	zerobss
        *     jsr	copydata
        * 
        *     lda #<(__RAM_START__+__RAM_SIZE__)
        *     sta	sp
        *     lda	#>(__RAM_START__+__RAM_SIZE__)
        *     sta	sp+1            ; Set argument stack ptr
        * 
        *     jsr	initlib
        * 
        *     lda #%10000000
        *     sta <PPU_CTRL_VAR
        *     sta PPU_CTRL		;enable NMI
        *     lda #%00000110
        *     sta <PPU_MASK_VAR
        */
        Write(NESInstruction.TXA_impl);
        Write(NESInstruction.STA_zpg_X, 0x00);
        for (int i = 1; i <= 7; i++)
        {
            Write(NESInstruction.STA_abs_X, (ushort)(0x0100 * i));
        }
        Write(NESInstruction.INX_impl);
        Write(NESInstruction.BNE_rel, 0xE6);
        Write(NESInstruction.LDA, 0x04);
        Write(NESInstruction.JSR, 0x8279);
        Write(NESInstruction.JSR, 0x824E);
        Write(NESInstruction.JSR, 0x82AE);
        Write(NESInstruction.JSR, 0x85CE);
        Write(NESInstruction.JSR, 0x854F);
        Write(NESInstruction.LDA, 0x00);
        Write(NESInstruction.STA_zpg, sp);
        Write(NESInstruction.LDA, PAL_BG_PTR);
        Write(NESInstruction.STA_zpg, sp + 1);
        Write(NESInstruction.JSR, 0x84F4);
        Write(NESInstruction.LDA, 0x4C);
        Write(NESInstruction.STA_zpg, 0x14);
        Write(NESInstruction.LDA, 0x10);
        Write(NESInstruction.STA_zpg, 0x15);
        Write(NESInstruction.LDA, 0x82);
        Write(NESInstruction.STA_zpg, 0x16);
        Write(NESInstruction.LDA, 0x80);
        Write(NESInstruction.STA_zpg, 0x10);
        Write(NESInstruction.STA_abs, PPU_CTRL);
        Write(NESInstruction.LDA, 0x06);
        Write(NESInstruction.STA_zpg, 0x12);
    }

    void Write_waitSync3()
    {
        // https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L197
        Write(NESInstruction.LDA_zpg, STARTUP);
        Write(NESInstruction.CMP_zpg, STARTUP);
        Write(NESInstruction.BEQ_rel, 0xFC);
    }

    void Write_detectNTSC()
    {
        // https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L203
        /*
         * A2 34
         * A0 18
         * CA
         * D0 FD
         * 88
         * D0 FA
         * AD 02 20
         * 29 80
         * 85 00
         * 20 80 82
         * A9 00
         * 8D 05 20
         * 8D 05 20
         * 8D 03 20
         * 4C 00 85
         */
        Write(NESInstruction.LDX, 0x34);
        Write(NESInstruction.LDY, 0x18);
        Write(NESInstruction.DEX_impl);
        Write(NESInstruction.BNE_rel, 0xFD);
        Write(NESInstruction.DEY_impl);
        Write(NESInstruction.BNE_rel, 0xFA);
        Write(NESInstruction.LDA_abs, PPU_STATUS);
        Write(NESInstruction.AND, 0x80);
        Write(NESInstruction.STA_zpg, 0x00);
        Write(NESInstruction.JSR, 0x8280);
        Write(NESInstruction.LDA, 0x00);
        Write(NESInstruction.STA_abs, PPU_SCROLL);
        Write(NESInstruction.STA_abs, PPU_SCROLL);
        Write(NESInstruction.STA_abs, PPU_OAM_ADDR);
        Write(NESInstruction.JMP_abs, 0x8500);
    }

    void Write_nmi()
    {
        /*
         * https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/neslib.sinc#L28
         * pha
         * txa
         * pha
         * tya
         * pha
         * 
         * lda <PPU_MASK_VAR	;if rendering is disabled, do not access the VRAM at all
         * and #%00011000
         * bne @doUpdate
         * jmp	@skipAll
         */
        Write(NESInstruction.PHA_impl);
        Write(NESInstruction.TXA_impl);
        Write(NESInstruction.PHA_impl);
        Write(NESInstruction.TYA_impl);
        Write(NESInstruction.PHA_impl);
        Write(NESInstruction.LDA_zpg, 0x12);
        Write(NESInstruction.AND, 0x18);
        Write(NESInstruction.BNE_rel, 0x03);
        Write(NESInstruction.JMP_abs, 0x81E6);
    }

    void Write_doUpdate()
    {
        /*
         * https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/neslib.sinc#L40
         * lda #>OAM_BUF		;update OAM
         * sta PPU_OAM_DMA
         * 
         * lda <PAL_UPDATE		;update palette if needed
         * bne @updPal
         * jmp @updVRAM
         */
        Write(NESInstruction.LDA, 0x02);
        Write(NESInstruction.STA_abs, PPU_OAM_DMA);
        Write(NESInstruction.LDA_zpg, PAL_UPDATE);
        Write(NESInstruction.BNE_rel, 0x03);
        Write(NESInstruction.JMP_abs, 0x81C0);
    }

    void Write_updPal()
    {
        /*
         * https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/neslib.sinc#L49
         * ldx #0
         * stx <PAL_UPDATE
         * 
         * lda #$3f
         * sta PPU_ADDR
         * stx PPU_ADDR
         */
        Write(NESInstruction.LDX, 0x00);
        Write(NESInstruction.STX_zpg, PAL_UPDATE);
        Write(NESInstruction.LDA, 0x3F);
        Write(NESInstruction.STA_abs, PPU_ADDR);
        Write(NESInstruction.STX_abs, PPU_ADDR);

        /*
         * ldy PAL_BUF				;background color, remember it in X
         * lda (PAL_BG_PTR),y
         * sta PPU_DATA
         * tax
         */
        Write(NESInstruction.LDY_abs, PAL_BUF);
        Write(NESInstruction.LDA_ind_Y, PAL_BG_PTR);
        Write(NESInstruction.STA_abs, PPU_DATA);
        Write(NESInstruction.TAX_impl);

        /*
         * .repeat 3,I
         * ldy PAL_BUF+1+I
         * da (PAL_BG_PTR),y
         * sta PPU_DATA
         * .endrepeat
         */
        for (int i = 1; i <= 3; i++)
        {
            Write(NESInstruction.LDY_abs, (ushort)(PAL_BUF + i));
            Write(NESInstruction.LDA_ind_Y, PAL_BG_PTR);
            Write(NESInstruction.STA_abs, PPU_DATA);
        }

        /*
        / * .repeat 3,J
        / * stx PPU_DATA			;background color
        / * .repeat 3,I
        / * ldy PAL_BUF+5+(J*4)+I
        / * lda (PAL_BG_PTR),y
        / * sta PPU_DATA
        / * .endrepeat
        / * .endrepeat
         */
        for (int j = 1; j <= 3; j++)
        {
            Write(NESInstruction.STX_abs, PPU_DATA);
            for (int i = 1; i <= 3; i++)
            {
                Write(NESInstruction.LDY_abs, (ushort)(PAL_BUF + (j * 4) + i));
                Write(NESInstruction.LDA_ind_Y, PAL_BG_PTR);
                Write(NESInstruction.STA_abs, PPU_DATA);
            }
        }

        /*
         * .repeat 4,J
         * stx PPU_DATA			;background color
         * .repeat 3,I
         * ldy PAL_BUF+17+(J*4)+I
         * lda (PAL_SPR_PTR),y
         * sta PPU_DATA
         * .endrepeat
         * .endrepeat
         */
        for (int j = 1; j <= 4; j++)
        {
            Write(NESInstruction.STX_abs, PPU_DATA);
            for (int i = 1; i <= 3; i++)
            {
                Write(NESInstruction.LDY_abs, (ushort)(PAL_BUF + 12 + (j * 4) + i));
                Write(NESInstruction.LDA_ind_Y, PAL_SPR_PTR);
                Write(NESInstruction.STA_abs, PPU_DATA);
            }
        }
    }

    void Write_updVRAM()
    {
        /*
         * 81C0	A503          	LDA VRAM_UPDATE               ; @updVRAM
         * 81C2	F00B          	BEQ @skipUpd                  
         * 81C4	A900          	LDA #$00                      
         * 81C6	8503          	STA VRAM_UPDATE               
         * 81C8	A506          	LDA NAME_UPD_ENABLE           
         * 81CA	F003          	BEQ @skipUpd                  
         * 81CC	208383        	JSR _flush_vram_update_nmi
         */
        Write(NESInstruction.LDA_zpg, VRAM_UPDATE);
        Write(NESInstruction.BEQ_rel, 0x0B);
        Write(NESInstruction.LDA, 0x00);
        Write(NESInstruction.STA_zpg, VRAM_UPDATE);
        Write(NESInstruction.LDA_zpg, NAME_UPD_ENABLE);
        Write(NESInstruction.BEQ_rel, 0x03);
        Write(NESInstruction.JSR, 0x8383);
    }

    void Write_skipUpd()
    {
        /*
         * 81CF	A900          	LDA #$00                      ; @skipUpd
         * 81D1	8D0620        	STA $2006                     
         * 81D4	8D0620        	STA $2006                     
         * 81D7	A50C          	LDA SCROLL_X                  
         * 81D9	8D0520        	STA $2005                     
         * 81DC	A50D          	LDA SCROLL_Y                  
         * 81DE	8D0520        	STA $2005                     
         * 81E1	A510          	LDA __PRG_FILEOFFS__          
         * 81E3	8D0020        	STA $2000 
         */
        Write(NESInstruction.LDA, 0x00);
        Write(NESInstruction.STA_abs, PPU_ADDR);
        Write(NESInstruction.STA_abs, PPU_ADDR);
        Write(NESInstruction.LDA_zpg, SCROLL_X);
        Write(NESInstruction.STA_abs, PPU_SCROLL);
        Write(NESInstruction.LDA_zpg, SCROLL_Y);
        Write(NESInstruction.STA_abs, PPU_SCROLL);
        Write(NESInstruction.LDA_zpg, PRG_FILEOFFS);
        Write(NESInstruction.STA_abs, PPU_CTRL);
    }

    void Write_skipAll()
    {
        /*
         * 81E6	A512          	LDA PPU_MASK_VAR              ; @skipAll
         * 81E8	8D0120        	STA $2001                     
         * 81EB	E601          	INC __STARTUP__               
         * 81ED	E602          	INC NES_PRG_BANKS             
         * 81EF	A502          	LDA NES_PRG_BANKS             
         * 81F1	C906          	CMP #$06                      
         * 81F3	D004          	BNE skipNtsc                  
         * 81F5	A900          	LDA #$00                      
         * 81F7	8502          	STA NES_PRG_BANKS 
         */
        Write(NESInstruction.LDA_zpg, PPU_MASK_VAR);
        Write(NESInstruction.STA_abs, PPU_MASK);
        Write(NESInstruction.INC_zpg, STARTUP);
        Write(NESInstruction.INC_zpg, NES_PRG_BANKS);
        Write(NESInstruction.LDA_zpg, NES_PRG_BANKS);
        Write(NESInstruction.CMP, 0x06);
        Write(NESInstruction.BNE_rel, 0x04);
        Write(NESInstruction.LDA, 0x00);
        Write(NESInstruction.STA_zpg, NES_PRG_BANKS);
    }

    void Write_skipNtsc()
    {
        /*
         * 81F9	201400        	JSR NMICallback               ; skipNtsc
         * 81FC	68            	PLA                           
         * 81FD	A8            	TAY                           
         * 81FE	68            	PLA                           
         * 81FF	AA            	TAX                           
         * 8200	68            	PLA                           
         * 8201	40            	RTI
         */
        Write(NESInstruction.JSR, (ushort)0x0014);
        Write(NESInstruction.PLA_impl);
        Write(NESInstruction.TAY_impl);
        Write(NESInstruction.PLA_impl);
        Write(NESInstruction.TAX_impl);
        Write(NESInstruction.PLA_impl);
        Write(NESInstruction.RTI_impl);
    }

    void Write_irq()
    {
        /*
         * 8202	48            	PHA                           ; irq
         * 8203	8A            	TXA                           
         * 8204	48            	PHA                           
         * 8205	98            	TYA                           
         * 8206	48            	PHA                           
         * 8207	A9FF          	LDA #$FF                      
         * 8209	4CF981        	JMP skipNtsc
         */
        Write(NESInstruction.PHA_impl);
        Write(NESInstruction.TXA_impl);
        Write(NESInstruction.PHA_impl);
        Write(NESInstruction.TYA_impl);
        Write(NESInstruction.PHA_impl);
        Write(NESInstruction.LDA, 0xFF);
        Write(NESInstruction.JMP_abs, skipNtsc);
    }

    void Write_nmi_set_callback()
    {
        /*
         * 820C	8515          	STA NMICallback+1             ; _nmi_set_callback
         * 820E	8616          	STX $16                       
         * 8210	60            	RTS                           ; HandyRTS
         */
        Write(NESInstruction.STA_zpg, 0x15);
        Write(NESInstruction.STX_zpg, 0x16);
        Write(NESInstruction.RTS_impl);
    }

    void Write_initlib()
    {
        /*
         * 84F4	A000          	LDY #$00                      ; initlib
         * 84F6	F007          	BEQ $84FF                     
         * 84F8	A900          	LDA #$00                      
         * 84FA	A285          	LDX #$85                      
         * 84FC	4C0003        	JMP condes                    
         * 84FF	60            	RTS
         */
        Write(NESInstruction.LDY, 0x00);
        Write(NESInstruction.BEQ_rel, PAL_UPDATE);
        Write(NESInstruction.LDA, 0x00);
        Write(NESInstruction.LDX, 0x85);
        Write(NESInstruction.JMP_abs, condes);
        Write(NESInstruction.RTS_impl);
    }

    void Write_donelib()
    {
        /*
         * 8546	A000          	LDY #$00                      ; donelib
         * 8548	F007          	BEQ $8551                     
         * 854A	A902          	LDA #$02                      
         * 854C	A286          	LDX #$86                      
         * 854E	4C0003        	JMP condes                    
         * 8551	60            	RTS
         */
        Write(NESInstruction.LDY, 0x00);
        Write(NESInstruction.BEQ_rel, PAL_UPDATE);
        Write(NESInstruction.LDA, 0xFE);
        Write(NESInstruction.LDX, 0x85);
        Write(NESInstruction.JMP_abs, condes);
        Write(NESInstruction.RTS_impl);
    }

    void Write_copydata()
    {
        /*
        * 8552	A902          	LDA #$02                      ; copydata
        * 8554	852A          	STA ptr1                      
        * 8556	A986          	LDA #$86                      
        * 8558	852B          	STA ptr1+1                    
        * 855A	A900          	LDA #$00                      
        * 855C	852C          	STA ptr2                      
        * 855E	A903          	LDA #$03                      
        * 8560	852D          	STA ptr2+1                    
        * 8562	A2DA          	LDX #$DA                      
        * 8564	A9FF          	LDA #$FF                      
        * 8566	8532          	STA tmp1                      
        * 8568	A000          	LDY #$00                      
        * 856A	E8            	INX                           
        * 856B	F00D          	BEQ $857A                     
        * 856D	B12A          	LDA (ptr1),y                  
        * 856F	912C          	STA (ptr2),y                  
        * 8571	C8            	INY                           
        * 8572	D0F6          	BNE $856A                     
        * 8574	E62B          	INC ptr1+1                    
        * 8576	E62D          	INC ptr2+1                    
        * 8578	D0F0          	BNE $856A                     
        * 857A	E632          	INC tmp1                      
        * 857C	D0EF          	BNE $856D                     
        * 857E	60            	RTS
        */
        Write(NESInstruction.LDA, 0xFE);
        Write(NESInstruction.STA_zpg, ptr1);
        Write(NESInstruction.LDA, 0x85);
        Write(NESInstruction.STA_zpg, ptr1 + 1);
        Write(NESInstruction.LDA, 0x00);
        Write(NESInstruction.STA_zpg, ptr2);
        Write(NESInstruction.LDA, 0x03);
        Write(NESInstruction.STA_zpg, ptr2 + 1);
        Write(NESInstruction.LDX, 0xDA);
        Write(NESInstruction.LDA, 0xFF);
        Write(NESInstruction.STA_zpg, tmp1);
        Write(NESInstruction.LDY, 0x00);
        Write(NESInstruction.INX_impl);
        Write(NESInstruction.BEQ_rel, 0x0D);
        Write(NESInstruction.LDA_ind_Y, ptr1);
        Write(NESInstruction.STA_ind_Y, ptr2);
        Write(NESInstruction.INY_impl);
        Write(NESInstruction.BNE_rel, 0xF6);
        Write(NESInstruction.INC_zpg, ptr1 + 1);
        Write(NESInstruction.INC_zpg, ptr2 + 1);
        Write(NESInstruction.BNE_rel, 0xF0);
        Write(NESInstruction.INC_zpg, tmp1);
        Write(NESInstruction.BNE_rel, 0xEF);
        Write(NESInstruction.RTS_impl);
    }

    void Write_popax()
    {
        /*
         * 857F	A001          	LDY #$01                      ; popax
         * 8581	B122          	LDA (sp),y                    
         * 8583	AA            	TAX                           
         * 8584	88            	DEY                           
         * 8585	B122          	LDA (sp),y
         */
        Write(NESInstruction.LDY, 0x01);
        Write(NESInstruction.LDA_ind_Y, sp);
        Write(NESInstruction.TAX_impl);
        Write(NESInstruction.DEY_impl);
        Write(NESInstruction.LDA_ind_Y, sp);
    }

    void Write_incsp2()
    {
        /*
        * 8587	E622          	INC sp                        ; incsp2
        * 8589	F005          	BEQ $8590                     
        * 858B	E622          	INC sp                        
        * 858D	F003          	BEQ $8592                     
        * 858F	60            	RTS                           
        * 8590	E622          	INC sp                        
        * 8592	E623          	INC sp+1                      
        * 8594	60            	RTS
        */
        Write(NESInstruction.INC_zpg, sp);
        Write(NESInstruction.BEQ_rel, 0x05);
        Write(NESInstruction.INC_zpg, sp);
        Write(NESInstruction.BEQ_rel, 0x03);
        Write(NESInstruction.RTS_impl);
        Write(NESInstruction.INC_zpg, sp);
        Write(NESInstruction.INC_zpg, sp + 1);
        Write(NESInstruction.RTS_impl);
    }

    void Write_popa()
    {
        /*
         * 8595	A000          	LDY #$00                      ; popa
         * 8597	B122          	LDA (sp),y                    
         * 8599	E622          	INC sp                        
         * 859B	F001          	BEQ $859E                     
         * 859D	60            	RTS                           
         * 859E	E623          	INC sp+1                      
         * 85A0	60            	RTS   
         */
        Write(NESInstruction.LDY, 0x00);
        Write(NESInstruction.LDA_ind_Y, sp);
        Write(NESInstruction.INC_zpg, sp);
        Write(NESInstruction.BEQ_rel, 0x01);
        Write(NESInstruction.RTS_impl);
        Write(NESInstruction.INC_zpg, sp + 1);
        Write(NESInstruction.RTS_impl);
    }

    void Write_pusha()
    {
        /*
         * 85A1	A000          	LDY #$00                      ; pusha0sp
         * 85A3	B122          	LDA (sp),y                    ; pushaysp
         * 85A5	A422          	LDY sp                        ; pusha
         * 85A7	F007          	BEQ $85B0                     
         * 85A9	C622          	DEC sp                        
         * 85AB	A000          	LDY #$00                      
         * 85AD	9122          	STA (sp),y                    
         * 85AF	60            	RTS                           
         * 85B0	C623          	DEC sp+1                      
         * 85B2	C622          	DEC sp                        
         * 85B4	9122          	STA (sp),y                    
         * 85B6	60            	RTS
        */
        Write(NESInstruction.LDY, 0x00);
        Write(NESInstruction.LDA_ind_Y, sp);
        Write(NESInstruction.LDY_zpg, sp);
        Write(NESInstruction.BEQ_rel, PAL_UPDATE);
        Write(NESInstruction.DEC_zpg, sp);
        Write(NESInstruction.LDY, 0x00);
        Write(NESInstruction.STA_ind_Y, sp);
        Write(NESInstruction.RTS_impl);
        Write(NESInstruction.DEC_zpg, sp + 1);
        Write(NESInstruction.DEC_zpg, sp);
        Write(NESInstruction.STA_ind_Y, sp);
        Write(NESInstruction.RTS_impl);
    }

    void Write_pushax()
    {
        /*
        * 85B7	A900          	LDA #$00                      ; push0
        * 85B9	A200          	LDX #$00                      ; pusha0
        * 85BB	48            	PHA                           ; pushax
        * 85BC	A522          	LDA sp                        
        * 85BE	38            	SEC                           
        * 85BF	E902          	SBC #$02                      
        * 85C1	8522          	STA sp                        
        * 85C3	B002          	BCS $85C7                     
        * 85C5	C623          	DEC sp+1                      
        * 85C7	A001          	LDY #$01                      
        * 85C9	8A            	TXA                           
        * 85CA	9122          	STA (sp),y                    
        * 85CC	68            	PLA                           
        * 85CD	88            	DEY                           
        * 85CE	9122          	STA (sp),y                    
        * 85D0	60            	RTS
        */
        Write(NESInstruction.LDA, 0x00);
        Write(NESInstruction.LDX, 0x00);
        Write(NESInstruction.PHA_impl);
        Write(NESInstruction.LDA_zpg, sp);
        Write(NESInstruction.SEC_impl);
        Write(NESInstruction.SBC, 0x02);
        Write(NESInstruction.STA_zpg, sp);
        Write(NESInstruction.BCS, 0x02);
        Write(NESInstruction.DEC_zpg, sp + 1);
        Write(NESInstruction.LDY, 0x01);
        Write(NESInstruction.TXA_impl);
        Write(NESInstruction.STA_ind_Y, sp);
        Write(NESInstruction.PLA_impl);
        Write(NESInstruction.DEY_impl);
        Write(NESInstruction.STA_ind_Y, sp);
        Write(NESInstruction.RTS_impl);
    }

    void Write_zerobss()
    {
        /*
         * 85D1	A925          	LDA #$25                      ; zerobss
         * 85D3	852A          	STA ptr1                      
         * 85D5	A903          	LDA #$03                      
         * 85D7	852B          	STA ptr1+1                    
         * 85D9	A900          	LDA #$00                      
         * 85DB	A8            	TAY                           
         * 85DC	A200          	LDX #$00                      
         * 85DE	F00A          	BEQ $85EA                     
         * 85E0	912A          	STA (ptr1),y                  
         * 85E2	C8            	INY                           
         * 85E3	D0FB          	BNE $85E0                     
         * 85E5	E62B          	INC ptr1+1                    
         * 85E7	CA            	DEX                           
         * 85E8	D0F6          	BNE $85E0                     
         * 85EA	C000          	CPY #$00                      
         * 85EC	F005          	BEQ $85F3                     
         * 85EE	912A          	STA (ptr1),y                  
         * 85F0	C8            	INY                           
         * 85F1	D0F7          	BNE $85EA                     
         * 85F3	60            	RTS
         */
        Write(NESInstruction.LDA, 0x25);
        Write(NESInstruction.STA_zpg, ptr1);
        Write(NESInstruction.LDA, 0x03);
        Write(NESInstruction.STA_zpg, ptr1 + 1);
        Write(NESInstruction.LDA, 0x00);
        Write(NESInstruction.TAY_impl);
        Write(NESInstruction.LDX, 0x00);
        Write(NESInstruction.BEQ_rel, PAL_SPR_PTR);
        Write(NESInstruction.STA_ind_Y, ptr1);
        Write(NESInstruction.INY_impl);
        Write(NESInstruction.BNE_rel, 0xFB);
        Write(NESInstruction.INC_zpg, ptr1 + 1);
        Write(NESInstruction.DEX_impl);
        Write(NESInstruction.BNE_rel, 0xF6);
        Write(NESInstruction.CPY, 0x00);
        Write(NESInstruction.BEQ_rel, 0x05);
        Write(NESInstruction.STA_ind_Y, ptr1);
        Write(NESInstruction.INY_impl);
        Write(NESInstruction.BNE_rel, 0xF7);
        Write(NESInstruction.RTS_impl);
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
