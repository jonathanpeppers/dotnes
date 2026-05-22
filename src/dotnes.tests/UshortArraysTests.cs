using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class UshortArraysTests : RoslynTests
{
    public UshortArraysTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void UshortArray_NewarrAndConstantStore()
    {
        // ushort[] newarr allocates count*2 bytes; constant-index stelem.i2 stores lo/hi at computed addresses
        var bytes = GetProgramBytes(
            """
            ushort[] arr = new ushort[4];
            arr[0] = 100;
            arr[1] = 300;
            arr[2] = 1000;
            arr[3] = 50000;
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortArray_NewarrAndConstantStore hex: {hex}");

        // arr[0] = 100 (0x0064): STA base+0 with 0x64, STA base+1 with 0x00
        Assert.Contains("A964", hex); // LDA #$64 (lo byte of 100)
        Assert.Contains("A900", hex); // LDA #$00 (hi byte of 100)

        // arr[1] = 300 (0x012C): lo=0x2C, hi=0x01
        Assert.Contains("A92C", hex); // LDA #$2C (lo byte of 300)
        Assert.Contains("A901", hex); // LDA #$01 (hi byte of 300)

        // arr[3] = 50000 (0xC350): lo=0x50, hi=0xC3
        Assert.Contains("A950", hex); // LDA #$50 (lo byte of 50000)
        Assert.Contains("A9C3", hex); // LDA #$C3 (hi byte of 50000)
    }

    [Fact]
    public void UshortArray_VariableIndexLoad()
    {
        // Variable-index ldelem.u2 uses ASL A, TAY, LDA base,Y / LDA base+1,Y
        var bytes = GetProgramBytes(
            """
            byte idx = rand8();
            ushort[] arr = new ushort[4];
            arr[0] = 100;
            arr[1] = 300;
            arr[2] = 1000;
            arr[3] = 50000;
            ushort loaded = arr[idx];
            pal_col(0, (byte)loaded);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortArray_VariableIndexLoad hex: {hex}");

        // Variable index load pattern: ASL A (0A), TAY (A8)
        Assert.Contains("0AA8", hex); // ASL A; TAY (double index for 16-bit elements)

        // AbsoluteY addressing: LDA abs,Y (B9) for both lo and hi bytes
        Assert.Contains("B9", hex);
    }

    [Fact]
    public void UshortArray_VariableIndexStore()
    {
        // Variable-index stelem.i2 saves value, computes Y offset, stores both bytes
        var bytes = GetProgramBytes(
            """
            ushort[] arr = new ushort[4];
            arr[0] = 100;
            byte idx = rand8();
            arr[idx] = 310;
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortArray_VariableIndexStore hex: {hex}");

        // 310 = 0x0136: lo=0x36, hi=0x01
        Assert.Contains("A936", hex); // LDA #$36 (lo byte of 310)
        Assert.Contains("A901", hex); // LDA #$01 (hi byte of 310)

        // Variable index store uses ASL A + TAY pattern
        Assert.Contains("0A", hex);   // ASL A
        Assert.Contains("A8", hex);   // TAY

        // Store pattern uses STA absolute,Y (opcode 99)
        Assert.Contains("99", hex);   // STA absolute,Y
    }

    [Fact]
    public void UshortArray_LoadStoresIn16BitLocal()
    {
        // ldelem.u2 result stored to ushort local uses STA $xxxx + STX $xxxx+1
        var bytes = GetProgramBytes(
            """
            byte i = rand8();
            ushort[] arr = new ushort[2];
            arr[0] = 500;
            arr[1] = 1000;
            ushort val = arr[i];
            pal_col(0, (byte)val);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortArray_LoadStoresIn16BitLocal hex: {hex}");

        // arr[0] = 500 (0x01F4): lo=0xF4, hi=0x01
        Assert.Contains("A9F4", hex); // LDA #$F4
        Assert.Contains("A901", hex); // LDA #$01

        // arr[1] = 1000 (0x03E8): lo=0xE8, hi=0x03
        Assert.Contains("A9E8", hex); // LDA #$E8
        Assert.Contains("A903", hex); // LDA #$03
    }
}
