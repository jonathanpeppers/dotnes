namespace dotnes.tests;

public class NESWriterTests
{
    readonly byte[] data;
    readonly MemoryStream stream = new MemoryStream();

    public NESWriterTests()
    {
        using var s = GetType().Assembly.GetManifestResourceStream("dotnes.tests.Data.hello.nes");
        if (s == null)
            throw new Exception("Cannot load hello.nes!");
        data = new byte[s.Length];
        s.Read(data, 0, data.Length);
    }

    NESWriter GetWriter(byte[]? PRG_ROM = null, byte[]? CHR_ROM = null)
    {
        stream.SetLength(0);

        return new NESWriter(stream, leaveOpen: true)
        {
            PRG_ROM = PRG_ROM,
            CHR_ROM = CHR_ROM,
        };
    }

    void AssertInstructions(string assembly)
    {
        var expected = toByteArray(assembly.Replace(" ", ""));
        var actual = stream.ToArray();
        Assert.Equal(expected, actual);

        static byte[] toByteArray(string text)
        {
            int length = text.Length >> 1;
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                bytes[i] = (byte)((toHex(text[i << 1]) << 4) + (toHex(text[(i << 1) + 1])));
            }
            return bytes;
        }

        static int toHex(char ch)
        {
            int value = (int)ch;
            return value - (value < 58 ? 48 : (value < 97 ? 55 : 87));
        }
    }

    /// <summary>
    /// Just used to slice apart 'hello.nes' for use
    /// </summary>
    //[Fact]
    public void Slice()
    {
        using var segment0 = File.Create("segment0.nes");
        segment0.Write(data, 16, 0x510 - 16);
        using var segment1 = File.Create("segment1.nes");
        segment1.Write(data, 0x553, 0x600 - 0x553);
        using var segment2 = File.Create("segment2.nes");
        segment2.Write(data, 0x60F, 0x634 - 0x60F);
    }

    [Fact]
    public void WriteHeader()
    {
        using var writer = GetWriter(new byte[2 * 16384], new byte[1 * 8192]);
        writer.WriteHeader();
        writer.Flush();

        var actual = stream.ToArray();
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(data[i], actual[i]);
        }
    }

    [Fact]
    public void WriteFullROM()
    {
        using var writer = GetWriter(new byte[2 * 16384], new byte[1 * 8192]);

        Array.Copy(data, 16, writer.PRG_ROM!, 0, 2 * 16384);
        Array.Copy(data, 16 + 2 * 16384, writer.CHR_ROM!, 0, 8192);

        writer.Write();
        writer.Flush();

        Assert.Equal(data, stream.ToArray());
    }

    [Fact]
    public void WriteLDA()
    {
        using var r = GetWriter();
        r.Write(Instruction.LDA, 0x08);
        r.Flush();

        // 8076	A908          	LDA #$08  
        AssertInstructions("A908");
    }

    [Fact]
    public void WriteJSR()
    {
        using var r = GetWriter();
        r.Write(Instruction.JSR, 0x84F4);
        r.Flush();

        // 807A	20F484        	JSR initlib
        AssertInstructions("20F484");
    }

    [Fact]
    public void Write_pal_all()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.pal_all));
        r.Flush();
        AssertInstructions("8517 8618 A200 A920");
    }

    [Fact]
    public void Write_pal_copy()
    {
        using var r = GetWriter();
        r.WriteBuiltIn("pal_copy");
        r.Flush();
        AssertInstructions("8519 A000");
    }

    [Fact]
    public void Write_pal_bg()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.pal_bg));
        r.Flush();
        AssertInstructions("8517 8618 A200 A910 D0E4");
    }

    [Fact]
    public void Write_pal_spr()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.pal_spr));
        r.Flush();
        AssertInstructions("8517 8618 A210 8A D0DB");
    }

    [Fact]
    public void Write_pal_col()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.pal_col));
        r.Flush();
        AssertInstructions("8517 205085 291F AA A517 9DC001 E607 60");
    }

    [Fact]
    public void Write_vram_adr()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.vram_adr));
        r.Flush();
        AssertInstructions("8E0620 8D0620 60");
    }

    [Fact]
    public void Write_vram_write()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.vram_write));
        r.Flush();
        AssertInstructions("8517 8618 203A85 8519 861A A000 B119 8D0720 E619 D002 E61A A517 D002 C618 C617 A517 0518 D0E7 60");
    }

    [Fact]
    public void Write_ppu_on_all()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.ppu_on_all));
        r.Flush();
        AssertInstructions("A512 0918 8512 4CF082");
    }

    [Fact]
    public void Write_pusha()
    {
        using var r = GetWriter();
        r.WriteBuiltIn("pusha");
        r.Flush();
        AssertInstructions("A422 F007 C622 A000 9122 60");
    }

    [Fact]
    public void Write_popa()
    {
        using var r = GetWriter();
        r.WriteBuiltIn("popa");
        r.Flush();
        AssertInstructions("A000 B122 E622 F001 60");
    }

    [Fact]
    public void Write_ppu_wait_nmi()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.ppu_wait_nmi));
        r.Flush();
        AssertInstructions("A901 8503 A501 C501 F0FC 60");
    }

    [Fact]
    public void Write_Main()
    {
        using var r = GetWriter();
        r.WriteHeader(PRG_ROM_SIZE: 2, CHR_ROM_SIZE: 1);
        r.WriteSegment(0);

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
        * 8543	A000          	LDY #$00                      ; donelib
        */

        ushort pusha = 0x85A2;
        ushort pushax = 0x85B8;
        ushort pal_col = 0x823E;
        ushort vram_adr = 0x83D4;
        ushort vram_write = 0x834F;
        ushort ppu_on_all = 0x8289;

        // pal_col(0, 0x02);
        r.Write(Instruction.LDA, 0x00);
        r.Write(Instruction.JSR, pusha);
        r.Write(Instruction.LDA, 0x02);
        r.Write(Instruction.JSR, pal_col);

        // pal_col(1, 0x14);
        r.Write(Instruction.LDA, 0x01);
        r.Write(Instruction.JSR, pusha);
        r.Write(Instruction.LDA, 0x14);
        r.Write(Instruction.JSR, pal_col);

        // pal_col(2, 0x20);
        r.Write(Instruction.LDA, 0x02);
        r.Write(Instruction.JSR, pusha);
        r.Write(Instruction.LDA, 0x20);
        r.Write(Instruction.JSR, pal_col);

        // pal_col(3, 0x30);
        r.Write(Instruction.LDA, 0x03);
        r.Write(Instruction.JSR, pusha);
        r.Write(Instruction.LDA, 0x30);
        r.Write(Instruction.JSR, pal_col);

        // vram_adr(NTADR_A(2, 2));
        r.Write(Instruction.LDX, 0x20);
        r.Write(Instruction.LDA, 0x42);
        r.Write(Instruction.JSR, vram_adr);

        // vram_write("HELLO, WORLD!", 13);
        r.Write(Instruction.LDA, 0xF1);
        r.Write(Instruction.LDX, 0x85);
        r.Write(Instruction.JSR, pushax);
        r.Write(Instruction.LDX, 0x00);
        r.Write(Instruction.LDA, 0x0D);
        r.Write(Instruction.JSR, vram_write);

        // ppu_on_all();
        r.Write(Instruction.JSR, ppu_on_all);

        // while (true) ;
        r.Write(Instruction.JMP_abs, 0x8540); // Jump to self

        // ;donelib
        r.Write(Instruction.LDY, 0x00);

        //r.WriteSegment(1);
        //r.WriteString("HELLO, WORLD!");
        //r.WriteSegment(2);
        r.Flush();

        var actual = stream.ToArray();
        //Assert.Equal(data.Length, actual.Length);
        for (int i = 0; i < actual.Length; i++)
        {
            if (data[i] != actual[i])
                throw new Exception($"Index: {i}, Expected: {data[i]}, Actual: {actual[i]}");
        }
    }
}