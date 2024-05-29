using System.Collections.Immutable;
using System.Reflection.Metadata;
using Xunit.Abstractions;
using static NES.NESLib;

namespace dotnes.tests;

public class IL2NESWriterTests
{
    readonly byte[] data;
    readonly MemoryStream stream = new();
    readonly ILogger _logger;

    public IL2NESWriterTests(ITestOutputHelper output)
    {
        _logger = new XUnitLogger(output);
        using var s = Utilities.GetResource("hello.nes");
        data = new byte[s.Length];
        s.Read(data, 0, data.Length);
    }

    IL2NESWriter GetWriter(byte[]? PRG_ROM = null, byte[]? CHR_ROM = null)
    {
        stream.SetLength(0);

        return new IL2NESWriter(stream, leaveOpen: true, logger: _logger)
        {
            PRG_ROM = PRG_ROM,
            CHR_ROM = CHR_ROM,
        };
    }

    [Fact]
    public void Write_static_void_Main()
    {
        const ushort sizeOfMain = 0x43;
        using var writer = GetWriter();
        writer.WriteHeader(PRG_ROM_SIZE: 2, CHR_ROM_SIZE: 1);
        writer.WriteBuiltIns(sizeOfMain);

        // pal_col(0, 0x02);
        writer.Write(ILOpCode.Ldc_i4_0, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4_2, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(pal_col), sizeOfMain);

        // pal_col(1, 0x14);
        writer.Write(ILOpCode.Ldc_i4_1, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4, 0x14, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(pal_col), sizeOfMain);

        // pal_col(2, 0x20);
        writer.Write(ILOpCode.Ldc_i4_2, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4, 0x20, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(pal_col), sizeOfMain);

        // pal_col(3, 0x30);
        writer.Write(ILOpCode.Ldc_i4_3, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4, 0x30, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(pal_col), sizeOfMain);

        // vram_adr(NTADR_A(2, 2));
        writer.Write(ILOpCode.Ldc_i4_2, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4_2, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(NTADR_A), sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(vram_adr), sizeOfMain);

        // vram_write("HELLO, .NET!");
        var text = "HELLO, .NET!";
        writer.Write(ILOpCode.Ldstr, text, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(vram_write), sizeOfMain);

        // ppu_on_all();
        writer.Write(ILOpCode.Call, nameof(ppu_on_all), sizeOfMain);

        // while (true) ;
        writer.Write(ILOpCode.Br_s, 254, sizeOfMain);

        writer.WriteFinalBuiltIns(0x85FE, locals: 0);
        writer.WriteString(text);
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

        // Use CHR_ROM from hello.nes
        writer.Write(data, (int)writer.Length, NESWriter.CHR_ROM_BLOCK_SIZE);

        AssertEx.Equal(data, writer);
    }

    [Fact]
    public void Write_Main_hello()
    {
        const ushort sizeOfMain = 0x43;
        using var writer = GetWriter();

        // pal_col(0, 0x02);
        writer.Write(ILOpCode.Ldc_i4_0, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4_2, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(pal_col), sizeOfMain);

        // pal_col(1, 0x14);
        writer.Write(ILOpCode.Ldc_i4_1, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4, 0x14, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(pal_col), sizeOfMain);

        // pal_col(2, 0x20);
        writer.Write(ILOpCode.Ldc_i4_2, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4, 0x20, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(pal_col), sizeOfMain);

        // pal_col(3, 0x30);
        writer.Write(ILOpCode.Ldc_i4_3, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4, 0x30, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(pal_col), sizeOfMain);

        // vram_adr(NTADR_A(2, 2));
        writer.Write(ILOpCode.Ldc_i4_2, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4_2, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(NTADR_A), sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(vram_adr), sizeOfMain);

        // vram_write("HELLO, .NET!");
        var text = "HELLO, .NET!";
        writer.Write(ILOpCode.Ldstr, text, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(vram_write), sizeOfMain);

        // ppu_on_all();
        writer.Write(ILOpCode.Call, nameof(ppu_on_all), sizeOfMain);

        // while (true) ;
        writer.Write(ILOpCode.Br_s, 254, sizeOfMain);

        var expected = Utilities.ToByteArray("A900 20A285 A902 203E82 A901 20A285 A914 203E82 A902 20A285 A920 203E82 A903 20A285 A930 203E82 A220 A942 20D483 A9F1 A285 20B885 A200 A90C 204F83 208982 4C4085");
        AssertEx.Equal(expected, writer);
    }

    [Fact]
    public void Write_Main_attributetable()
    {
        const ushort sizeOfMain = 0x2E;
        using var writer = GetWriter();
        writer.Write(ILOpCode.Ldc_i4_s, 64, sizeOfMain);
        writer.Write(ILOpCode.Newarr, 16777235, sizeOfMain);
        writer.Write(ILOpCode.Dup, sizeOfMain);
        writer.Write(ILOpCode.Ldtoken, ImmutableArray.Create(new byte[] {
          0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // rows 0-3
          0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, // rows 4-7
          0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, // rows 8-11
          0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, // rows 12-15
          0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, // rows 16-19
          0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, // rows 20-23
          0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, // rows 24-27
          0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f  // rows 28-29
        }), sizeOfMain);
        writer.Write(ILOpCode.Stloc_0, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4_s, 16, sizeOfMain);
        writer.Write(ILOpCode.Newarr, 16777235, sizeOfMain);
        writer.Write(ILOpCode.Dup, sizeOfMain);
        writer.Write(ILOpCode.Ldtoken, ImmutableArray.Create(new byte[] {
          0x03,			// screen color

          0x11,0x30,0x27,0x0,	// background palette 0
          0x1c,0x20,0x2c,0x0,	// background palette 1
          0x00,0x10,0x20,0x0,	// background palette 2
          0x06,0x16,0x26        // background palette 3
        }), sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(NESLib.pal_bg), sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4, 0x2000, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(NESLib.vram_adr), sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4_s, 22, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4, 960, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(NESLib.vram_fill), sizeOfMain);
        writer.Write(ILOpCode.Ldloc_0, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(NESLib.vram_write), sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(NESLib.ppu_on_all), sizeOfMain);
        writer.Write(ILOpCode.Br_s, 254, sizeOfMain);

        var expected = Utilities.ToByteArray("A91C A286 202B82 A220 A900 20D483 A916 208D85 A203 A9C0 20DF83 A9DC A285 20A385 A200 A940 204F83 208982 4C2B85");
        AssertEx.Equal(expected, writer);
    }
}
