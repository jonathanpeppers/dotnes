using System.Buffers;
using System.Text;
using dotnes.ObjectModel;
using static dotnes.NESConstants;

namespace dotnes;

/// <summary>
/// Writes .nes files
/// * https://wiki.nesdev.org/w/index.php/INES
/// * https://bheisler.github.io/post/nes-rom-parser-with-nom/
/// </summary>
class NESWriter : IDisposable
{
    public static readonly Encoding Encoding = Encoding.ASCII;

    public NESWriter(Stream stream, bool leaveOpen = false, ILogger? logger = null)
    {
        _writer = new(stream, Encoding, leaveOpen);
        _logger = logger ?? new NullLogger();

        // NOTE: starting values so they exist in dictionary
        Labels[nameof(copydata)] = 0;
        Labels[nameof(popa)] = 0;
        Labels[nameof(popax)] = 0;
        Labels[nameof(pusha)] = 0;
        Labels[nameof(pushax)] = 0;
        Labels[nameof(zerobss)] = 0;
        Labels[nameof(rodata)] = 0;
        Labels[nameof(donelib)] = 0;
    }

    /// <summary>
    /// PRG_ROM is in 16 KB units
    /// </summary>
    public const int PRG_ROM_BLOCK_SIZE = 16384;
    /// <summary>
    /// CHR ROM in in 8 KB units
    /// </summary>
    public const int CHR_ROM_BLOCK_SIZE = 8192;

    // Post-main functions (these depend on code layout, keep local)
    protected const ushort copydata = 0x850C;
    protected const ushort popa = 0x854F;
    protected const ushort popax = 0x8539;
    protected const ushort pusha = 0x855F;
    protected const ushort pushax = 0x8575;
    protected const ushort zerobss = 0x858B;
    protected const ushort rodata = 0x85AE;
    protected const ushort donelib = 0x84FD;

    protected const ushort BaseAddress = 0x8000;

    protected readonly BinaryWriter _writer;
    protected readonly ILogger _logger;

    public bool LastLDA { get; protected set; }

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

    public Dictionary<string, ushort> Labels { get; private set; } = new();
    private bool _hasPresetLabels = false;

    public void SetLabels(Dictionary<string, ushort> labels)
    {
        Labels = labels;
        _hasPresetLabels = true;
    }

    /// <summary>
    /// A list of methods that were found to be used in the IL code
    /// </summary>
    public HashSet<string>? UsedMethods { get; set; }

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

    public void Write(byte[] buffer)
    {
        LastLDA = false;
        _writer.Write(buffer);
    }

    public void Write(ushort[] buffer)
    {
        LastLDA = false;
        for (int i = 0; i < buffer.Length; i++)
        {
            _writer.Write(buffer[i]);
        }
    }

    public void Write(byte[] buffer, int index, int count)
    {
        LastLDA = false;
        _writer.Write(buffer, index, count);
    }

    /// <summary>
    /// Writes a string in ASCI form, including a trailing \0
    /// </summary>
    public void WriteString(string text)
    {
        LastLDA = false;
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
    public void WriteBuiltIns(ushort sizeOfMain)
    {
        Write_exit();
        Write_initPPU();
        Write_clearPalette();
        Write_clearVRAM();
        Write_clearRAM(sizeOfMain);
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
        WriteBuiltIn(nameof(NESLib.pal_all), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_copy), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_bg), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_spr), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_col), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_clear), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_spr_bright), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_bg_bright), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_bright), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_off), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_on_all), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_onoff), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_on_bg), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_on_spr), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_mask), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_system), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.get_ppu_ctrl_var), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.set_ppu_ctrl_var), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.oam_clear), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.oam_size), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.oam_hide_rest), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_wait_frame), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_wait_nmi), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.scroll), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.bank_spr), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.bank_bg), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.vram_write), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.set_vram_update), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.flush_vram_update), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.vram_adr), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.vram_put), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.vram_fill), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.vram_inc), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.nesclock), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.delay), sizeOfMain);
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
    public void WriteFinalBuiltIns(ushort totalSize, byte locals)
    {
        Write_donelib(totalSize);
        Write_copydata(totalSize);
        Write_popax();
        Write_incsp2();
        Write_popa();
        Write_pusha();
        Write_pushax();
        Write_zerobss(locals);

        // List of optional methods at the end
        if (UsedMethods is not null)
        {
            if (UsedMethods.Contains(nameof(NESLib.oam_spr)))
            {
                WriteBuiltIn(nameof(NESLib.oam_spr), totalSize);
            }
            if (UsedMethods.Contains(nameof(NESLib.pad_poll)))
            {
                Write_pad_poll();
            }
        }
    }

    void Write_pad_poll()
    {
        SetLabel(nameof(NESLib.pad_poll), (ushort)(_writer.BaseStream.Position + BaseAddress));
        WriteBlock(BuiltInSubroutines.PadPoll());
    }

    /// <summary>
    /// Writes a built-in method from NESLib
    /// </summary>
    public void WriteBuiltIn(string name, ushort sizeOfMain)
    {
        SetLabel(name, (ushort)(_writer.BaseStream.Position + BaseAddress));

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
                WriteBlock(BuiltInSubroutines.PalCol());
                break;
            case nameof(NESLib.pal_clear):
                WriteBlock(BuiltInSubroutines.PalClear());
                break;
            case nameof(NESLib.pal_spr_bright):
                WriteBlock(BuiltInSubroutines.PalSprBright());
                break;
            case nameof(NESLib.pal_bg_bright):
                WriteBlock(BuiltInSubroutines.PalBgBright());
                break;
            case nameof(NESLib.pal_bright):
                WriteBlock(BuiltInSubroutines.PalBright());
                break;
            case nameof(NESLib.ppu_off):
                WriteBlock(BuiltInSubroutines.PpuOff());
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
                WriteBlock(BuiltInSubroutines.PpuMask());
                break;
            case nameof(NESLib.ppu_system):
                WriteBlock(BuiltInSubroutines.PpuSystem());
                break;
            case nameof(NESLib.get_ppu_ctrl_var):
                WriteBlock(BuiltInSubroutines.GetPpuCtrlVar());
                break;
            case nameof(NESLib.set_ppu_ctrl_var):
                WriteBlock(BuiltInSubroutines.SetPpuCtrlVar());
                break;
            case nameof(NESLib.oam_clear):
                WriteBlock(BuiltInSubroutines.OamClear());
                break;
            case nameof(NESLib.oam_size):
                WriteBlock(BuiltInSubroutines.OamSize());
                break;
            case nameof(NESLib.oam_hide_rest):
                WriteBlock(BuiltInSubroutines.OamHideRest());
                break;
            case nameof(NESLib.ppu_wait_frame):
                WriteBlock(BuiltInSubroutines.PpuWaitFrame());
                break;
            case nameof(NESLib.ppu_wait_nmi):
                WriteBlock(BuiltInSubroutines.PpuWaitNmi());
                break;
            case nameof(NESLib.scroll):
                WriteBlock(BuiltInSubroutines.Scroll());
                break;
            case nameof(NESLib.bank_spr):
                WriteBlock(BuiltInSubroutines.BankSpr());
                break;
            case nameof(NESLib.bank_bg):
                WriteBlock(BuiltInSubroutines.BankBg());
                break;
            case nameof(NESLib.vram_write):
                WriteBlock(BuiltInSubroutines.VramWrite());
                break;
            case nameof(NESLib.set_vram_update):
                WriteBlock(BuiltInSubroutines.SetVramUpdate());
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
                WriteBlock(BuiltInSubroutines.VramAdr());
                break;
            case nameof(NESLib.vram_put):
                WriteBlock(BuiltInSubroutines.VramPut());
                break;
            case nameof(NESLib.vram_fill):
                WriteBlock(BuiltInSubroutines.VramFill());
                break;
            case nameof(NESLib.vram_inc):
                WriteBlock(BuiltInSubroutines.VramInc());
                break;
            case nameof(NESLib.nesclock):
                WriteBlock(BuiltInSubroutines.NesClock());
                break;
            case nameof(NESLib.delay):
                WriteBlock(BuiltInSubroutines.Delay());
                break;
            case nameof(NESLib.oam_spr):
                WriteBlock(BuiltInSubroutines.OamSpr());
                break;
            default:
                throw new NotImplementedException($"{name} is not implemented!");
        }
    }

    void Write_exit()
    {
        WriteBlock(BuiltInSubroutines.Exit());
    }

    void Write_initPPU()
    {
        WriteBlock(BuiltInSubroutines.InitPPU());
    }

    void Write_clearPalette()
    {
        WriteBlock(BuiltInSubroutines.ClearPalette());
    }

    void Write_clearVRAM()
    {
        WriteBlock(BuiltInSubroutines.ClearVRAM());
    }

    void Write_clearRAM(ushort sizeOfMain)
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
        Write(NESInstruction.JSR, Labels[nameof(zerobss)]);
        Write(NESInstruction.JSR, Labels[nameof(copydata)]);
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
        WriteBlock(BuiltInSubroutines.Irq());
    }

    void Write_nmi_set_callback()
    {
        WriteBlock(BuiltInSubroutines.NmiSetCallback());
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

    void Write_donelib(ushort totalSize)
    {
        SetLabel(nameof(donelib), (ushort)(_writer.BaseStream.Position + BaseAddress));

        /*
         * 8546	A000          	LDY #$00                      ; donelib
         * 8548	F007          	BEQ $8551
         * 8547	A9FE          	LDA #$FE
         * 8549	A285          	LDX #$85
         * 854E	4C0003        	JMP condes
         * 8551	60            	RTS
         */
        Write(NESInstruction.LDY, 0x00);
        Write(NESInstruction.BEQ_rel, PAL_UPDATE);
        Write(NESInstruction.LDA, (byte)(totalSize & 0xff));
        Write(NESInstruction.LDX, (byte)(totalSize >> 8));
        Write(NESInstruction.JMP_abs, condes);
        Write(NESInstruction.RTS_impl);
    }

    void Write_copydata(ushort totalSize)
    {
        SetLabel(nameof(copydata), (ushort)(_writer.BaseStream.Position + BaseAddress));

        /*
        * 854F	A9FE          	LDA #$FE                      ; copydata
        * 8551	852A          	STA ptr1
        * 8553	A985          	LDA #$85
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
        Write(NESInstruction.LDA, (byte)(totalSize & 0xff));
        Write(NESInstruction.STA_zpg, ptr1);
        Write(NESInstruction.LDA, (byte)(totalSize >> 8));
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
        SetLabel(nameof(popax), (ushort)(_writer.BaseStream.Position + BaseAddress));
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
        SetLabel(nameof(popa), (ushort)(_writer.BaseStream.Position + BaseAddress));
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
        //
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
        //SetLabel(nameof(pusha0sp), (ushort)(_writer.BaseStream.Position + BaseAddress));
        Write(NESInstruction.LDY, 0x00);

        //SetLabel(nameof(pushaysp), (ushort)(_writer.BaseStream.Position + BaseAddress));
        Write(NESInstruction.LDA_ind_Y, sp);

        SetLabel(nameof(pusha), (ushort)(_writer.BaseStream.Position + BaseAddress));
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
        //SetLabel(nameof(push0), (ushort)(_writer.BaseStream.Position + BaseAddress));
        Write(NESInstruction.LDA, 0x00);

        //SetLabel(nameof(pusha0), (ushort)(_writer.BaseStream.Position + BaseAddress));
        Write(NESInstruction.LDX, 0x00);

        SetLabel(nameof(pushax), (ushort)(_writer.BaseStream.Position + BaseAddress));
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

    void Write_zerobss(byte locals)
    {
        SetLabel(nameof(zerobss), (ushort)(_writer.BaseStream.Position + BaseAddress));
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
        Write(NESInstruction.CPY, locals);
        Write(NESInstruction.BEQ_rel, 0x05);
        Write(NESInstruction.STA_ind_Y, ptr1);
        Write(NESInstruction.INY_impl);
        Write(NESInstruction.BNE_rel, 0xF7);
        Write(NESInstruction.RTS_impl);
    }

    /// <summary>
    /// Writes an "implied" instruction that has no argument
    /// </summary>
    public void Write(NESInstruction i)
    {
        LastLDA = i == NESInstruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X})");
        _writer.Write((byte)i);
    }

    /// <summary>
    /// Writes an instruction with a single byte argument
    /// </summary>
    public void Write (NESInstruction i, byte @byte)
    {
        LastLDA = i == NESInstruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X}) {@byte:X}");
        _writer.Write((byte)i);
        _writer.Write(@byte);
    }

    /// <summary>
    /// Writes an instruction with an address argument (2 bytes)
    /// </summary>
    public void Write(NESInstruction i, ushort address)
    {
        LastLDA = i == NESInstruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X}) {address:X}");
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

    private void SetLabel(string name, ushort address)
    {
        if (_hasPresetLabels)
            return;
        Labels[name] = address;
    }

    /// <summary>
    /// Writes a Block to the stream, resolving any label references using the Labels dictionary.
    /// </summary>
    protected void WriteBlock(Block block)
    {
        ushort currentAddress = (ushort)(_writer.BaseStream.Position + BaseAddress);
        
        // Build a local label table for intra-block labels
        var localLabels = new Dictionary<string, ushort>();
        ushort addr = currentAddress;
        
        // First pass: calculate addresses for local labels
        foreach (var (instruction, label) in block.InstructionsWithLabels)
        {
            if (label != null)
            {
                localLabels[label] = addr;
            }
            addr += (ushort)instruction.Size;
        }
        
        // Second pass: emit bytes
        addr = currentAddress;
        foreach (var (instruction, _) in block.InstructionsWithLabels)
        {
            byte opcode = OpcodeTable.Encode(instruction.Opcode, instruction.Mode);
            _writer.Write(opcode);
            
            if (instruction.Operand != null)
            {
                switch (instruction.Operand)
                {
                    case ImmediateOperand imm:
                        _writer.Write(imm.Value);
                        break;
                        
                    case AbsoluteOperand abs:
                        _writer.Write(abs.Address);
                        break;
                        
                    case LabelOperand labelOp:
                        // Try local labels first, then global Labels dictionary
                        if (localLabels.TryGetValue(labelOp.Label, out ushort labelAddr))
                        {
                            _writer.Write(labelAddr);
                        }
                        else if (Labels.TryGetValue(labelOp.Label, out labelAddr))
                        {
                            _writer.Write(labelAddr);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unresolved label: {labelOp.Label}");
                        }
                        break;
                        
                    case RelativeOperand relOp:
                        // Resolve relative branch to label
                        ushort targetAddr;
                        if (localLabels.TryGetValue(relOp.Label, out targetAddr))
                        {
                            // Calculate relative offset from instruction following this one
                            int offset = targetAddr - (addr + 2); // +2 for opcode + operand
                            if (offset < -128 || offset > 127)
                                throw new InvalidOperationException($"Branch to {relOp.Label} out of range: {offset}");
                            _writer.Write((byte)(sbyte)offset);
                        }
                        else if (Labels.TryGetValue(relOp.Label, out targetAddr))
                        {
                            int offset = targetAddr - (addr + 2);
                            if (offset < -128 || offset > 127)
                                throw new InvalidOperationException($"Branch to {relOp.Label} out of range: {offset}");
                            _writer.Write((byte)(sbyte)offset);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unresolved label: {relOp.Label}");
                        }
                        break;
                        
                    case RelativeByteOperand relByte:
                        _writer.Write((byte)(sbyte)relByte.Offset);
                        break;
                }
            }
            
            addr += (ushort)instruction.Size;
        }
        
        // Track LastLDA for optimization patterns
        if (block.Count > 0)
        {
            var lastInstr = block[block.Count - 1];
            LastLDA = lastInstr.Opcode == Opcode.LDA && lastInstr.Mode == AddressMode.Immediate;
        }
    }

    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();
}
