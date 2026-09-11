using Xunit.Abstractions;

namespace dotnes.tests;

public class ArrayAliasStateTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedRomAliasLoadDoesNotReplaceAnUnrelatedAddress(bool staticAlias)
    {
        string source =
            $$"""
            ushort address = 0x0123;
            byte[] table = new byte[] { 11, 22, 33 };
            byte frame = 0;
            {{(staticAlias ? "State.Table = table;" : "byte[] alias = table;")}}
            set_vram_update(address);
            byte low = peek(4);
            byte high = peek(5);
            while (frame < 2)
            {
                set_vram_update({{(staticAlias ? "table" : "alias")}});
                set_vram_update(table);
                frame++;
            }
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State { public static byte[] Table; }
            """;
        var cpu = ExecuteProgram(source);
        Assert.Equal(new byte[] { 0x23, 0x01 }, cpu.Memory[0x6000..0x6002]);
        int table = cpu.Memory[NESConstants.NAME_UPD_ADR] | cpu.Memory[NESConstants.NAME_UPD_ADR + 1] << 8;
        Assert.True(table >= NESConstants.PrgRomStart);
        Assert.Equal(new byte[] { 11, 22, 33 }, cpu.Memory[table..(table + 3)]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
