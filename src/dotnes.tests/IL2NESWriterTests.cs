using System.Collections.Immutable;
using System.Reflection.Metadata;
using Xunit.Abstractions;
using static NES.NESLib;

namespace dotnes.tests;

public class IL2NESWriterTests
{
    readonly MemoryStream _stream = new();
    readonly ILogger _logger;

    public IL2NESWriterTests(ITestOutputHelper output)
    {
        _logger = new XUnitLogger(output);
    }

    IL2NESWriter GetWriter(byte[]? PRG_ROM = null, byte[]? CHR_ROM = null)
    {
        _stream.SetLength(0);

        var writer = new IL2NESWriter(_stream, leaveOpen: true, logger: _logger)
        {
            PRG_ROM = PRG_ROM,
            CHR_ROM = CHR_ROM,
        };
        writer.StartBlockBuffering();
        return writer;
    }

    [Fact]
    public Task Write_static_void_Main()
    {
        const ushort sizeOfMain = 0x43;
        using var writer = GetWriter();
        // Set up Labels needed by WriteBuiltIns
        writer.Labels["popa"] = 0x8592;
        writer.Labels["popax"] = 0x857C;
        writer.Labels["zerobss"] = 0x85C7;
        writer.Labels["copydata"] = 0x85EA;
        writer.Labels["pusha"] = 0x85A2;
        writer.Labels["pushax"] = 0x85B8;
        writer.WriteHeader(PRG_ROM_SIZE: 2, CHR_ROM_SIZE: 1);
        writer.WriteBuiltIns();

        // pal_col(0, 0x02);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_0), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col), sizeOfMain);

        // pal_col(1, 0x14);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_1), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x14, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col), sizeOfMain);

        // pal_col(2, 0x20);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x20, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col), sizeOfMain);

        // pal_col(3, 0x30);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_3), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x30, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col), sizeOfMain);

        // vram_adr(NTADR_A(2, 2));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NTADR_A), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(vram_adr), sizeOfMain);

        // vram_write("HELLO, .NET!");
        var text = "HELLO, .NET!";
        writer.Write(new ILInstruction(ILOpCode.Ldstr), text, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(vram_write), sizeOfMain);

        // ppu_on_all();
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(ppu_on_all), sizeOfMain);

        // while (true) ;
        writer.Write(new ILInstruction(ILOpCode.Br_s), 254, sizeOfMain);

        // Flush main block before writing final built-ins
        writer.FlushMainBlock();

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

        return Verify(_stream.ToArray());
    }

    [Fact]
    public Task Write_Main_hello()
    {
        const ushort sizeOfMain = 0x43;
        using var writer = GetWriter();
        // Set up Labels needed for IL->NES translation
        writer.Labels["pusha"] = 0x85A2;
        writer.Labels["pushax"] = 0x85B8;
        writer.Labels["pal_col"] = 0x823E;
        writer.Labels["vram_adr"] = 0x83D4;
        writer.Labels["vram_write"] = 0x834F;
        writer.Labels["ppu_on_all"] = 0x8289;

        // pal_col(0, 0x02);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_0), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col), sizeOfMain);

        // pal_col(1, 0x14);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_1), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x14, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col), sizeOfMain);

        // pal_col(2, 0x20);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x20, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col), sizeOfMain);

        // pal_col(3, 0x30);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_3), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x30, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col), sizeOfMain);

        // vram_adr(NTADR_A(2, 2));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NTADR_A), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(vram_adr), sizeOfMain);

        // vram_write("HELLO, .NET!");
        var text = "HELLO, .NET!";
        writer.Write(new ILInstruction(ILOpCode.Ldstr), text, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(vram_write), sizeOfMain);

        // ppu_on_all();
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(ppu_on_all), sizeOfMain);

        // while (true) ;
        writer.Write(new ILInstruction(ILOpCode.Br_s), 254, sizeOfMain);

        writer.FlushMainBlock();
        return Verify(_stream.ToArray());
    }

    [Fact]
    public Task Write_Main_attributetable()
    {
        const ushort sizeOfMain = 0x2E;
        using var writer = GetWriter();
        // Enable single-pass label-based mode
        writer.UseLabelReferences = true;
        // Set up Labels needed for IL->NES translation
        writer.Labels["pusha"] = 0x85A2;
        writer.Labels["pushax"] = 0x85B8;
        writer.Labels["pal_bg"] = 0x822B;
        writer.Labels["vram_adr"] = 0x83D4;
        writer.Labels["vram_fill"] = 0x83DF;
        writer.Labels["vram_write"] = 0x834F;
        writer.Labels["ppu_on_all"] = 0x8289;
        writer.Labels["pushax"] = 0x8D85;
        // Set up byte array labels for single-pass mode
        writer.Labels["bytearray_0"] = 0x85DC; // ATTRIBUTE_TABLE (64 bytes)
        writer.Labels["bytearray_1"] = 0x861C; // PALETTE (16 bytes)
        // Set up instruction label for Br_s target (while(true) infinite loop)
        writer.Labels["instruction_00"] = 0x0000;
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_s), 64, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Newarr), 16777235, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Dup), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldtoken), [
          0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // rows 0-3
          0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, // rows 4-7
          0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, // rows 8-11
          0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, // rows 12-15
          0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, // rows 16-19
          0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, // rows 20-23
          0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, // rows 24-27
          0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f  // rows 28-29
        ], sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Stloc_0), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_s), 16, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Newarr), 16777235, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Dup), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldtoken), [
          0x03,			// screen color

          0x11,0x30,0x27,0x0,	// background palette 0
          0x1c,0x20,0x2c,0x0,	// background palette 1
          0x00,0x10,0x20,0x0,	// background palette 2
          0x06,0x16,0x26        // background palette 3
        ], sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NESLib.pal_bg), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x2000, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NESLib.vram_adr), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_s), 22, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 960, sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NESLib.vram_fill), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Ldloc_0), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NESLib.vram_write), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NESLib.ppu_on_all), sizeOfMain);
        writer.Write(new ILInstruction(ILOpCode.Br_s), 254, sizeOfMain);

        writer.FlushMainBlock();
        return Verify(_stream.ToArray());
    }
}
