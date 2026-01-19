using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class NESWriterTests
{
    const ushort SizeOfMain = 67;
    readonly MemoryStream _stream = new();
    readonly ILogger _logger;

    public NESWriterTests(ITestOutputHelper output)
    {
        _logger = new XUnitLogger(output);
    }

    NESWriter GetWriter(byte[]? PRG_ROM = null, byte[]? CHR_ROM = null)
    {
        _stream.SetLength(0);

        return new NESWriter(_stream, leaveOpen: true, logger: _logger)
        {
            PRG_ROM = PRG_ROM,
            CHR_ROM = CHR_ROM,
        };
    }

    void AssertInstructions(string assembly)
    {
        var expected = Utilities.ToByteArray(assembly);
        var actual = _stream.ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public Task WriteHeader()
    {
        using var writer = GetWriter(new byte[2 * NESWriter.PRG_ROM_BLOCK_SIZE], new byte[NESWriter.CHR_ROM_BLOCK_SIZE]);
        writer.WriteHeader();
        writer.Flush();

        return Verify(_stream.ToArray());
    }

    [Fact]
    public void WriteLDA()
    {
        using var writer = GetWriter();
        writer.Write(NESInstruction.LDA, 0x08);
        writer.Flush();

        // 8076	A908          	LDA #$08  
        AssertInstructions("A908");
    }

    [Fact]
    public void WriteJSR()
    {
        using var writer = GetWriter();
        writer.Write(NESInstruction.JSR, 0x84F4);
        writer.Flush();

        // 807A	20F484        	JSR initlib
        AssertInstructions("20F484");
    }

    [Fact]
    public void Write_pal_all()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PalAll());
        writer.Flush();
        AssertInstructions("8517 8618 A200 A920");
    }

    [Fact]
    public void Write_pal_copy()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PalCopy());
        writer.Flush();
        AssertInstructions("8519 A000 B117 9DC001 E8 C8 C619 D0F5 E607 60");
    }

    [Fact]
    public void Write_pal_bg()
    {
        using var writer = GetWriter();
        // pal_copy label must be set for the BNE branch to resolve
        // BNE at offset 8 needs relative offset 0xE4 (-28) -> target = 0x800A - 28 = 0x7FEE
        writer.Labels["pal_copy"] = 0x7FEE;
        writer.WriteBlock(BuiltInSubroutines.PalBg());
        writer.Flush();
        AssertInstructions("8517 8618 A200 A910 D0E4");
    }

    [Fact]
    public void Write_pal_spr()
    {
        using var writer = GetWriter();
        // pal_copy label must be set for the BNE branch to resolve
        // BNE at offset 7 needs relative offset 0xDB (-37) -> target = 0x8009 - 37 = 0x7FE4
        writer.Labels["pal_copy"] = 0x7FE4;
        writer.WriteBlock(BuiltInSubroutines.PalSpr());
        writer.Flush();
        AssertInstructions("8517 8618 A210 8A D0DB");
    }

    [Fact]
    public void Write_pal_col()
    {
        using var writer = GetWriter();
        writer.Labels["popa"] = 0x854F + 0x43;
        writer.WriteBlock(BuiltInSubroutines.PalCol());
        writer.Flush();
        AssertInstructions("8517 209285 291F AA A517 9DC001 E607 60");
    }

    [Fact]
    public void Write_pal_clear()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PalClear());
        writer.Flush();
        AssertInstructions("A90F A200 9DC001 E8 E020 D0F8 8607 60");
    }

    [Fact]
    public void Write_vram_adr()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.VramAdr());
        writer.Flush();
        AssertInstructions("8E0620 8D0620 60");
    }

    [Fact]
    public void Write_vram_write()
    {
        using var writer = GetWriter();
        // vram_write needs popax label - address derived from expected output "207C85" = JSR $857C
        writer.Labels["popax"] = 0x857C;
        writer.WriteBlock(BuiltInSubroutines.VramWrite());
        writer.Flush();
        AssertInstructions("8517 8618 207C85 8519 861A A000 B119 8D0720 E619 D002 E61A A517 D002 C618 C617 A517 0518 D0E7 60");
    }

    [Fact]
    public void Write_ppu_on_all()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PpuOnAll());
        writer.Flush();
        AssertInstructions("A512 0918");
    }

    [Fact]
    public void Write_ppu_onoff()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PpuOnOff());
        writer.Flush();
        AssertInstructions("8512 4CF082");
    }

    [Fact]
    public void Write_ppu_on_bg()
    {
        using var writer = GetWriter();
        // ppu_on_bg branches backward to ppu_onoff with offset -11 (0xF5)
        // At stream position 0 (address 0x8000), BNE is at address 0x8004
        // Target = 0x8004 + 2 - 11 = 0x7FFB
        writer.Labels["ppu_onoff"] = 0x7FFB;
        writer.WriteBlock(BuiltInSubroutines.PpuOnBg());
        writer.Flush();
        AssertInstructions("A512 0908 D0F5");
    }

    [Fact]
    public void Write_ppu_wait_nmi()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PpuWaitNmi());
        writer.Flush();
        AssertInstructions("A901 8503 A501 C501 F0FC 60");
    }

    [Fact]
    public Task Write_Main()
    {
        using var writer = GetWriter();
        // Set up Labels needed by WriteBuiltIns - these are calculated based on expected addresses
        // popa is used by pal_col, pad_trigger, delay
        // popax is used by scroll, vram_write  
        // zerobss/copydata are used by Write_initlib
        writer.Labels["popa"] = 0x8592;
        writer.Labels["popax"] = 0x857C;
        writer.Labels["zerobss"] = 0x85C7;
        writer.Labels["copydata"] = 0x85EA;
        
        writer.WriteHeader(PRG_ROM_SIZE: 2, CHR_ROM_SIZE: 1);
        writer.WriteBuiltIns();

        /*
        * 8500	A900          	LDA #$00                      ; _main
        * 8502	20A285        	JSR pusha                     
        * 8505	A902          	LDA #$02                      
        * 8507	203E82        	JSR _pal_col                  
        * 850A	A901          	LDA #$01                      
        * 850C	20A285        	JSR pusha                     
        * 850F	A914          	LDA #$14                      
        * 8511	203E82        	JSR _pal_col                  
        * 8514	A902          	LDA #$02                      
        * 8516	20A285        	JSR pusha                     
        * 8519	A920          	LDA #$20                      
        * 851B	203E82        	JSR _pal_col                  
        * 851E	A903          	LDA #$03                      
        * 8520	20A285        	JSR pusha                     
        * 8523	A930          	LDA #$30                      
        * 8525	203E82        	JSR _pal_col                  
        * 8528	A220          	LDX #$20                      
        * 852A	A942          	LDA #$42                      
        * 852C	20D483        	JSR _vram_adr                 
        * 852F	A9F1          	LDA #$F1                      
        * 8531	A285          	LDX #$85                      
        * 8533	20B885        	JSR pushax                    
        * 8536	A200          	LDX #$00                      
        * 8538	A90D          	LDA #$0D                      
        * 853A	204F83        	JSR _vram_write               
        * 853D	208982        	JSR _ppu_on_all               
        * 8540	4C4085        	JMP $8540                     
        */

        ushort pusha = 0x85A2;
        ushort pushax = 0x85B8;
        ushort pal_col = 0x823E;
        ushort vram_adr = 0x83D4;
        ushort vram_write = 0x834F;
        ushort ppu_on_all = 0x8289;

        // pal_col(0, 0x02);
        writer.Write(NESInstruction.LDA, 0x00);
        writer.Write(NESInstruction.JSR, pusha);
        writer.Write(NESInstruction.LDA, 0x02);
        writer.Write(NESInstruction.JSR, pal_col);

        // pal_col(1, 0x14);
        writer.Write(NESInstruction.LDA, 0x01);
        writer.Write(NESInstruction.JSR, pusha);
        writer.Write(NESInstruction.LDA, 0x14);
        writer.Write(NESInstruction.JSR, pal_col);

        // pal_col(2, 0x20);
        writer.Write(NESInstruction.LDA, 0x02);
        writer.Write(NESInstruction.JSR, pusha);
        writer.Write(NESInstruction.LDA, 0x20);
        writer.Write(NESInstruction.JSR, pal_col);

        // pal_col(3, 0x30);
        writer.Write(NESInstruction.LDA, 0x03);
        writer.Write(NESInstruction.JSR, pusha);
        writer.Write(NESInstruction.LDA, 0x30);
        writer.Write(NESInstruction.JSR, pal_col);

        // vram_adr(NTADR_A(2, 2));
        writer.Write(NESInstruction.LDX, 0x20);
        writer.Write(NESInstruction.LDA, 0x42);
        writer.Write(NESInstruction.JSR, vram_adr);

        // vram_write("HELLO, .NET!", 12);
        writer.Write(NESInstruction.LDA, 0xF1);
        writer.Write(NESInstruction.LDX, 0x85);
        writer.Write(NESInstruction.JSR, pushax);
        writer.Write(NESInstruction.LDX, 0x00);
        writer.Write(NESInstruction.LDA, 0x0C);
        writer.Write(NESInstruction.JSR, vram_write);

        // ppu_on_all();
        writer.Write(NESInstruction.JSR, ppu_on_all);

        // while (true) ;
        writer.Write(NESInstruction.JMP_abs, 0x8540); // Jump to self

        writer.WriteFinalBuiltIns(0x85FE, locals: 0);
        writer.WriteString("HELLO, .NET!");
        writer.WriteDestructorTable();

        // Pad 0s
        int PRG_ROM_SIZE = (int)writer.Length - 16;
        writer.WriteZeroes(NESWriter.PRG_ROM_BLOCK_SIZE - (PRG_ROM_SIZE % NESWriter.PRG_ROM_BLOCK_SIZE));

        // Write interrupt vectors
        const int VECTOR_ADDRESSES_SIZE = 6;
        writer.WriteZeroes(NESWriter.PRG_ROM_BLOCK_SIZE - VECTOR_ADDRESSES_SIZE);
        ushort nmi_data = 0x80BC;
        ushort reset_data = 0x8000;
        ushort irq_data = 0x8202;
        writer.Write(new ushort[] { nmi_data, reset_data, irq_data });

        return Verify(_stream.ToArray());
    }

    [Fact]
    public Task WriteUsedMethods()
    {
        using var writer = GetWriter();
        writer.UsedMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(NESLib.oam_spr),
        };
        writer.WriteHeader(PRG_ROM_SIZE: 2, CHR_ROM_SIZE: 1);
        writer.WriteFinalBuiltIns(SizeOfMain, locals: 4);
        return Verify(_stream.ToArray());
    }
}