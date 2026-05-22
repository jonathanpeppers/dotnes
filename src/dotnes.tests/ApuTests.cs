using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ApuTests : RoslynTests
{
    public ApuTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void PlayTone_Pulse1()
    {
        // apu_play_tone(PulseChannel.Pulse1, 0x0180, APUDuty.Duty25, 10) should emit inline register writes:
        //   ctrl = (1 << 6) | 0x30 | 10 = 0x7A -> STA $4000
        //   sweep = 0x00 -> STA $4001
        //   timer_lo = 0x80 -> STA $4002
        //   timer_hi = 0x01 -> STA $4003
        var bytes = GetProgramBytes(
            """
            poke(APU_STATUS, 0x0F);
            apu_play_tone(PulseChannel.Pulse1, 0x0180, APUDuty.Duty25, 10);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Verify poke(APU_STATUS, 0x0F) emits correctly (period > 255 uses ushort path)
        Assert.Contains("A90F" + "8D1540", hex);    // LDA #$0F, STA $4015 (APU_STATUS)
        // Assert full LDA+STA pairs as contiguous sequences
        Assert.Contains("A97A" + "8D0040", hex);   // LDA #$7A, STA $4000 (ctrl)
        Assert.Contains("A900" + "8D0140", hex);   // LDA #$00, STA $4001 (sweep)
        Assert.Contains("A980" + "8D0240", hex);   // LDA #$80, STA $4002 (timer lo)
        Assert.Contains("A901" + "8D0340", hex);   // LDA #$01, STA $4003 (timer hi)
    }

    [Fact]
    public void PlayTone_Pulse2()
    {
        // apu_play_tone(PulseChannel.Pulse2, 0x00FD, APUDuty.Duty50, 15) should target pulse 2 registers ($4004-$4007):
        //   ctrl = (2 << 6) | 0x30 | 15 = 0xBF -> STA $4004
        //   sweep = 0x00 -> STA $4005
        //   timer_lo = 0xFD -> STA $4006
        //   timer_hi = 0x00 -> STA $4007
        var bytes = GetProgramBytes(
            """
            poke(APU_STATUS, 0x0F);
            apu_play_tone(PulseChannel.Pulse2, 0x00FD, APUDuty.Duty50, 15);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Verify poke(APU_STATUS, 0x0F) emits correctly (period <= 255 uses byte path)
        Assert.Contains("A90F" + "8D1540", hex);    // LDA #$0F, STA $4015 (APU_STATUS)
        // Assert full LDA+STA pairs as contiguous sequences
        Assert.Contains("A9BF" + "8D0440", hex);   // LDA #$BF, STA $4004 (ctrl)
        Assert.Contains("A900" + "8D0540", hex);   // LDA #$00, STA $4005 (sweep)
        Assert.Contains("A9FD" + "8D0640", hex);   // LDA #$FD, STA $4006 (timer lo)
        Assert.Contains("A900" + "8D0740", hex);   // LDA #$00, STA $4007 (timer hi)
    }

    [Fact]
    public void Stop_Pulse1()
    {
        // apu_stop(PulseChannel.Pulse1) should silence pulse 1:
        //   LDA #$30, STA $4000
        var bytes = GetProgramBytes(
            """
            poke(APU_STATUS, 0x0F);
            apu_stop(PulseChannel.Pulse1);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A930" + "8D0040", hex);   // LDA #$30, STA $4000
    }

    [Fact]
    public void Stop_Pulse2()
    {
        // apu_stop(PulseChannel.Pulse2) should silence pulse 2:
        //   LDA #$30, STA $4004
        var bytes = GetProgramBytes(
            """
            poke(APU_STATUS, 0x0F);
            apu_stop(PulseChannel.Pulse2);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A930" + "8D0440", hex);   // LDA #$30, STA $4004
    }
}
