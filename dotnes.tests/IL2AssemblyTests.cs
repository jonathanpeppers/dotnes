namespace dotnes.tests;

public class IL2AssemblyTests
{
    readonly string path;

    public IL2AssemblyTests()
    {
        var dir = Path.GetDirectoryName(GetType().Assembly.Location)!;
        path = Path.Combine(dir, "dotnes.sample.dll");
    }

    [Fact]
    public void StaticVoidMain()
    {
        using var il = new IL2Assembly(path);
        Assert.Equal(
@"Ldc_i4_0
Ldc_i4_2
Call pal_col
Call pal_col
Call pal_col
Call pal_col
Stloc_3
Nop
Nop
Stloc_0
Call vram_adr
Nop
Cpobj
Nop
Nop
Stloc_0
Nop
Call ppu_on_all
Ldc_i4_1
Stloc_0
Br_s", il.ReadStaticVoidMain());
    }
}
