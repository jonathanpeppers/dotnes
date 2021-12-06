using System.Reflection.Metadata;

namespace dotnes.tests;

public class IL2NESWriterTests
{
    readonly byte[] data;
    readonly MemoryStream stream = new MemoryStream();

    public IL2NESWriterTests()
    {
        using var s = GetType().Assembly.GetManifestResourceStream("dotnes.tests.Data.hello.nes");
        if (s == null)
            throw new Exception("Cannot load hello.nes!");
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
        using var r = GetWriter();
        r.WriteHeader(PRG_ROM_SIZE: 2, CHR_ROM_SIZE: 1);
        r.WriteSegment(0);

        // pal_col(0, 0x02);
        r.Write(ILOpCode.Ldc_i4_0);
        r.Write(ILOpCode.Ldc_i4_2);
        r.Write(ILOpCode.Call, nameof(NESLib.pal_col));

        // pal_col(1, 0x14);
        r.Write(ILOpCode.Ldc_i4_1);
        r.Write(ILOpCode.Ldc_i4, 0x14);
        r.Write(ILOpCode.Call, nameof(NESLib.pal_col));

        // pal_col(2, 0x20);
        r.Write(ILOpCode.Ldc_i4_2);
        r.Write(ILOpCode.Ldc_i4, 0x20);
        r.Write(ILOpCode.Call, nameof(NESLib.pal_col));

        // pal_col(3, 0x30);
        r.Write(ILOpCode.Ldc_i4_3);
        r.Write(ILOpCode.Ldc_i4, 0x30);
        r.Write(ILOpCode.Call, nameof(NESLib.pal_col));

        // vram_adr(NTADR_A(2, 2));
        //r.Write(Instruction.LDX, 0x20);
        //r.Write(Instruction.LDA, 0x42);
        //r.Write(Instruction.JSR, vram_adr);

        // vram_write("HELLO, WORLD!", 13);
        //r.Write(Instruction.LDA, 0xF1);
        //r.Write(Instruction.LDX, 0x85);
        //r.Write(Instruction.JSR, pushax);
        //r.Write(Instruction.LDX, 0x00);
        //r.Write(Instruction.LDA, 0x0D);
        //r.Write(Instruction.JSR, vram_write);

        // ppu_on_all();
        //r.Write(Instruction.JSR, ppu_on_all);

        // while (true) ;
        //r.Write(Instruction.JMP_abs, 0x8540); // Jump to self

        //r.WriteSegment(1);
        //r.WriteString("HELLO, WORLD!");
        //r.WriteSegment(2);

        // Pad 0s
        //int PRG_ROM_SIZE = (int)r.Length - 16;
        //r.WriteZeroes(NESWriter.PRG_ROM_BLOCK_SIZE - (PRG_ROM_SIZE % NESWriter.PRG_ROM_BLOCK_SIZE));
        //r.WriteZeroes(NESWriter.PRG_ROM_BLOCK_SIZE - 6);

        //TODO: no idea what these are???
        //r.Write(new byte[] { 0xBC, 0x80, 0x00, 0x80, 0x02, 0x82 });

        // Use CHR_ROM from hello.nes
        //r.Write(data, (int)r.Length, NESWriter.CHR_ROM_BLOCK_SIZE);

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
