using System.Collections.Immutable;
using System.Reflection.Metadata;
using static NES.NESLib;

namespace dotnes.tests;

public class IL2NESWriterTests
{
    readonly byte[] data;
    readonly MemoryStream stream = new();

    public IL2NESWriterTests()
    {
        using var s = Utilities.GetResource("hello.nes");
        data = new byte[s.Length];
        s.Read(data, 0, data.Length);
    }

    IL2NESWriter GetWriter(byte[]? PRG_ROM = null, byte[]? CHR_ROM = null)
    {
        stream.SetLength(0);

        return new IL2NESWriter(stream, leaveOpen: true)
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

        writer.WriteFinalBuiltIns(0x85FE);
        writer.WriteString(text);
        writer.WriteDestructorTable();

        // Pad 0s
        int PRG_ROM_SIZE = (int)writer.Length - 16;
        writer.WriteZeroes(NESWriter.PRG_ROM_BLOCK_SIZE - (PRG_ROM_SIZE % NESWriter.PRG_ROM_BLOCK_SIZE));
        writer.WriteZeroes(NESWriter.PRG_ROM_BLOCK_SIZE - 6);

        //TODO: no idea what these are???
        writer.Write(new byte[] { 0xBC, 0x80, 0x00, 0x80, 0x02, 0x82 });

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
            0x03,               // screen color
            0x11,0x30,0x27,0x0, // background palette 0
            0x1c,0x20,0x2c,0x0, // background palette 1
            0x00,0x10,0x20,0x0, // background palette 2
            0x06,0x16,0x26      // background palette 3
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

    [Fact]
    public void Write_Main_sprites()
    {
        const ushort sizeOfMain = 0x12D;
        using var writer = GetWriter();

        writer.Write(ILOpCode.Ldc_i4_s, 32, sizeOfMain);
        writer.Write(ILOpCode.Newarr, 16777235, sizeOfMain);
        writer.Write(ILOpCode.Dup, sizeOfMain);
        writer.Write(ILOpCode.Ldtoken, ImmutableArray.Create(new byte[] {
            0x03,               // screen color
            0x11,0x30,0x27,0x0, // background palette 0
            0x1c,0x20,0x2c,0x0, // background palette 1
            0x00,0x10,0x20,0x0, // background palette 2
            0x06,0x16,0x26,0x0, // background palette 3
            0x16,0x35,0x24,0x0, // sprite palette 0
            0x00,0x37,0x25,0x0, // sprite palette 1
            0x0d,0x2d,0x3a,0x0, // sprite palette 2
            0x0d,0x27,0x2a      // sprite palette 3
        }), sizeOfMain);
        writer.Write(ILOpCode.Stloc_0, sizeOfMain);

        // byte[] array2 = new byte[64];
        writer.Write(ILOpCode.Ldc_i4_s, 64, sizeOfMain);
        writer.Write(ILOpCode.Newarr, 16777235, sizeOfMain);
        writer.Write(ILOpCode.Stloc_1, sizeOfMain);

        // byte[] array3 = new byte[64];
        writer.Write(ILOpCode.Ldc_i4_s, 64, sizeOfMain);
        writer.Write(ILOpCode.Newarr, 16777235, sizeOfMain);
        writer.Write(ILOpCode.Stloc_2, sizeOfMain);

        // byte[] array4 = new byte[64];
        writer.Write(ILOpCode.Ldc_i4_s, 64, sizeOfMain);
        writer.Write(ILOpCode.Newarr, 16777235, sizeOfMain);
        writer.Write(ILOpCode.Stloc_3, sizeOfMain);

        // byte[] array5 = new byte[64];
        writer.Write(ILOpCode.Ldc_i4_s, 64, sizeOfMain);
        writer.Write(ILOpCode.Newarr, 16777235, sizeOfMain);
        writer.Write(ILOpCode.Stloc_s, 4, sizeOfMain);

        // for (byte b = 0; b < 64; b = (byte)(b + 1))
        writer.Write(ILOpCode.Ldc_i4_0, sizeOfMain);
        writer.Write(ILOpCode.Stloc_s, 5, sizeOfMain);
        writer.Write(ILOpCode.Br_s, 54, sizeOfMain);

        // array2[b] = NESLib.rand();
        writer.Write(ILOpCode.Ldloc_1, sizeOfMain);
        writer.Write(ILOpCode.Ldloc_s, 5, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(rand), sizeOfMain);
        writer.Write(ILOpCode.Stelem_i1, sizeOfMain);

        // array3[b] = NESLib.rand();
        writer.Write(ILOpCode.Ldloc_2, sizeOfMain);
        writer.Write(ILOpCode.Ldloc_s, 5, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(rand), sizeOfMain);
        writer.Write(ILOpCode.Stelem_i1, sizeOfMain);

        // array4[b] = (byte)((NESLib.rand() & 7) - 3);
        writer.Write(ILOpCode.Ldloc_3, sizeOfMain);
        writer.Write(ILOpCode.Ldloc_s, 5, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(rand), sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4_7, sizeOfMain);
        writer.Write(ILOpCode.And, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4_3, sizeOfMain);
        writer.Write(ILOpCode.Sub, sizeOfMain);
        writer.Write(ILOpCode.Conv_u1, sizeOfMain);
        writer.Write(ILOpCode.Stelem_i1, sizeOfMain);

        // array5[b] = (byte)((NESLib.rand() & 7) - 3);
        writer.Write(ILOpCode.Ldloc_s, 4, sizeOfMain);
        writer.Write(ILOpCode.Ldloc_s, 5, sizeOfMain);
        writer.Write(ILOpCode.Call, nameof(rand), sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4_7, sizeOfMain);
        writer.Write(ILOpCode.And, sizeOfMain);
        writer.Write(ILOpCode.Ldc_i4_3, sizeOfMain);
        writer.Write(ILOpCode.Sub, sizeOfMain);
        writer.Write(ILOpCode.Conv_u1, sizeOfMain);
        writer.Write(ILOpCode.Stelem_i1, sizeOfMain);

        var expected = Utilities.ToByteArray("A900 8D2904 AD2904 C940 B06C A929 A203 18 6D2904 9001 E8 20A586 20C786 A000 20EF86 A969 A203 18 6D2904 9001 E8 20A586 20C786 A000 20EF86 A9A9 A203 18 6D2904 9001 E8 20A586 20C786 2907 38 E903 C980 A000 20EF86 A9E9 A203 18 6D2904 9001 E8 20A586 20C786 2907 38 E903 C980 A000 20EF86 EE2904 4C0585");
        AssertEx.Equal(expected, writer);
    }
}
