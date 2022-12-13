using System.Reflection.Metadata;
using static NES.NESLib;

namespace dotnes.tests;

public class IL2NESWriterTests
{
    readonly byte[] data;
    readonly MemoryStream stream = new MemoryStream();

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
        using var writer = GetWriter();
        writer.WriteHeader(PRG_ROM_SIZE: 2, CHR_ROM_SIZE: 1);
        writer.WriteSegment(0);
        writer.WriteBuiltIns();

        // pal_col(0, 0x02);
        writer.Write(ILOpCode.Ldc_i4_0);
        writer.Write(ILOpCode.Ldc_i4_2);
        writer.Write(ILOpCode.Call, nameof(pal_col));

        // pal_col(1, 0x14);
        writer.Write(ILOpCode.Ldc_i4_1);
        writer.Write(ILOpCode.Ldc_i4, 0x14);
        writer.Write(ILOpCode.Call, nameof(pal_col));

        // pal_col(2, 0x20);
        writer.Write(ILOpCode.Ldc_i4_2);
        writer.Write(ILOpCode.Ldc_i4, 0x20);
        writer.Write(ILOpCode.Call, nameof(pal_col));

        // pal_col(3, 0x30);
        writer.Write(ILOpCode.Ldc_i4_3);
        writer.Write(ILOpCode.Ldc_i4, 0x30);
        writer.Write(ILOpCode.Call, nameof(pal_col));

        // vram_adr(NTADR_A(2, 2));
        writer.Write(ILOpCode.Ldc_i4_2);
        writer.Write(ILOpCode.Ldc_i4_2);
        writer.Write(ILOpCode.Call, nameof(NTADR_A));
        writer.Write(ILOpCode.Call, nameof(vram_adr));

        // vram_write("HELLO, .NET!");
        var text = "HELLO, .NET!";
        writer.Write(ILOpCode.Ldstr, text);
        writer.Write(ILOpCode.Call, nameof(vram_write));

        // ppu_on_all();
        writer.Write(ILOpCode.Call, nameof(ppu_on_all));

        // while (true) ;
        // TODO: for some reason the IL is:
        // while (true) { bool flag = true; }
        writer.Write(NESInstruction.JMP_abs, 0x8540); // Jump to self

        writer.WriteBuiltIn("donelib");
        writer.WriteBuiltIn("copydata");
        writer.WriteBuiltIn("popax");

        writer.WriteSegment(1);
        writer.WriteString("HELLO, .NET!");
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
}
