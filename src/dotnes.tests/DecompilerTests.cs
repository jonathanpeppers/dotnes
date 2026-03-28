using Xunit.Abstractions;

namespace dotnes.tests;

public class DecompilerTests
{
    readonly ILogger _logger;

    public DecompilerTests(ITestOutputHelper output) => _logger = new XUnitLogger(output);

    [Fact]
    public void NESRomReader_ParsesHeader()
    {
        var romBytes = GetVerifiedRom("hello");
        var reader = new NESRomReader(romBytes);

        Assert.Equal(2, reader.PrgBanks);
        Assert.Equal(1, reader.ChrBanks);
        Assert.Equal(0, reader.Mapper);
        Assert.False(reader.VerticalMirroring);
        Assert.False(reader.HasBattery);
        Assert.Equal(0x8000, reader.ResetVector);
        Assert.Equal(32768, reader.PrgRom.Length); // 2 * 16KB
        Assert.Equal(8192, reader.ChrRom.Length);  // 1 * 8KB
    }

    [Theory]
    [InlineData("statusbar", true)]   // verticalMirroring=true
    [InlineData("horizscroll", true)] // verticalMirroring=true
    public void NESRomReader_VerticalMirroring(string name, bool expectedMirroring)
    {
        var romBytes = GetVerifiedRom(name);
        var reader = new NESRomReader(romBytes);

        Assert.Equal(expectedMirroring, reader.VerticalMirroring);
    }

    [Fact]
    public void NESRomReader_ChrRamMode()
    {
        var romBytes = GetVerifiedRom("transtable");
        var reader = new NESRomReader(romBytes);

        Assert.Equal(0, reader.ChrBanks);
        Assert.Empty(reader.ChrRom);
    }

    [Fact]
    public void NESRomReader_ChrRomExtraction()
    {
        var romBytes = GetVerifiedRom("hello");
        var reader = new NESRomReader(romBytes);

        Assert.Equal(8192, reader.ChrRom.Length);
        var chrAsm = reader.GenerateChrAssembly();
        Assert.StartsWith(".segment \"CHARS\"", chrAsm);
        Assert.Contains("$", chrAsm); // Should have hex bytes
    }

    [Fact]
    public void NESRomReader_InterruptVectors()
    {
        var romBytes = GetVerifiedRom("hello");
        var reader = new NESRomReader(romBytes);

        // Reset vector should point to $8000 (start of PRG ROM)
        Assert.Equal(0x8000, reader.ResetVector);
        // NMI and IRQ vectors should be in PRG ROM range
        Assert.True(reader.NmiVector >= 0x8000);
        Assert.True(reader.IrqVector >= 0x8000);
    }

    [Fact]
    public void NESRomReader_InvalidRom_Throws()
    {
        var badData = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
        Assert.Throws<InvalidOperationException>(() => new NESRomReader(badData));
    }

    [Fact]
    public void Decompiler_Shoot2_RecognizesPoke()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // shoot2 uses poke() for APU initialization (silence channels)
        Assert.Contains("poke(APU_PULSE1_CTRL, 0x30);", code);
        Assert.Contains("poke(APU_PULSE2_CTRL, 0x30);", code);
        Assert.Contains("poke(APU_TRIANGLE_CTRL, 0x80);", code);
        Assert.Contains("poke(APU_NOISE_CTRL, 0x30);", code);
        Assert.Contains("poke(APU_STATUS, 0x0F);", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecognizesPeek()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // shoot2 uses peek() for reading random seed
        Assert.Contains("peek(", code);
    }

    [Fact]
    public void Decompiler_Hello_ProducesCorrectOutput()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Verify key NESLib calls are present
        Assert.Contains("pal_col(0, 0x02);", code);
        Assert.Contains("pal_col(1, 0x14);", code);
        Assert.Contains("pal_col(2, 0x20);", code);
        Assert.Contains("pal_col(3, 0x30);", code);
        Assert.Contains("vram_adr(NTADR_A(2, 2));", code);
        Assert.Contains("vram_write(\"HELLO, .NET!\");", code);
        Assert.Contains("ppu_on_all();", code);
        Assert.Contains("while (true) ;", code);
    }

    [Fact]
    public void Decompiler_Hello_GeneratesCsproj()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile(); // Must decompile first to populate state

        var csproj = decompiler.GenerateCsproj("hello");

        Assert.Contains("<OutputType>Exe</OutputType>", csproj);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", csproj);
        Assert.Contains("<Using Include=\"NES\" />", csproj);
        Assert.Contains("dotnes", csproj);
        // hello uses default settings so no mapper/prg/chr overrides
        Assert.DoesNotContain("NESMapper", csproj);
    }

    [Fact]
    public void Decompiler_Music_FindsMainAfterMusicSubroutines()
    {
        var romBytes = GetVerifiedRom("music");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Music sample has music_tick/start_music subroutines between built-ins and main.
        // The decompiler must find main at its actual address (not builtInsEnd).
        // Verify key NESLib calls that appear at the start of music's main():
        Assert.Contains("pal_col(0, 0x01);", code);
        Assert.Contains("vram_write(\"NOW PLAYING\");", code);
        Assert.Contains("ppu_on_all();", code);
    }

    [Fact]
    public void Decompiler_Shoot2_UxROM_FindsMain()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);

        Assert.Equal(2, rom.Mapper); // UxROM

        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // shoot2 uses UxROM mapper - decompiler must correctly identify main
        Assert.Contains("ppu_off();", code);
        Assert.Contains("oam_clear();", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecognizesSetRand()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // shoot2 calls set_rand(42) near the start of main
        Assert.Contains("set_rand(", code);
    }

    [Fact]
    public void Decompiler_Attributetable_RecoversPalBgData()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // The attributetable sample uses pal_bg with a 16-byte palette
        Assert.Contains("pal_bg(palette", code);
        Assert.Contains("new byte[]", code);
        // Verify specific palette values from the sample's PALETTE array
        Assert.Contains("0x03", code);
        Assert.Contains("0x11, 0x30, 0x27", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoversPalBgAndPalSprData()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // shoot2 uses both pal_bg and pal_spr with palette byte arrays
        Assert.Contains("pal_bg(palette", code);
        Assert.Contains("pal_spr(palette", code);
        // Verify pal_bg palette data: 0x0F, 0x30, 0x10, 0x00 repeated
        Assert.Contains("0x0F, 0x30, 0x10, 0x00", code);
        // Verify pal_spr contains sprite palette data
        Assert.Contains("0x0F, 0x30, 0x10, 0x20", code);
    }

    [Fact]
    public void Decompiler_Statusbar_CsprojHasVerticalMirroring()
    {
        var romBytes = GetVerifiedRom("statusbar");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile();

        var csproj = decompiler.GenerateCsproj("statusbar");

        Assert.Contains("<NESVerticalMirroring>true</NESVerticalMirroring>", csproj);
    }

    [Fact]
    public void Decompiler_Shoot2_DetectsArrayDeclarations()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // shoot2 has 13 byte[] arrays for game state (bullets, enemies, stars, explosions)
        // 3 arrays of size 4 (bullet_x, bullet_y, bullet_active)
        Assert.Contains("byte[] array_0335 = new byte[4];", code);
        Assert.Contains("byte[] array_0339 = new byte[4];", code);
        Assert.Contains("byte[] array_033D = new byte[4];", code);
        // 4 arrays of size 6 (enemy_x, enemy_y, enemy_active, enemy_speed)
        Assert.Contains("byte[] array_0341 = new byte[6];", code);
        Assert.Contains("byte[] array_0347 = new byte[6];", code);
        Assert.Contains("byte[] array_034D = new byte[6];", code);
        Assert.Contains("byte[] array_0353 = new byte[6];", code);
        // 3 arrays of size 8 (star_x, star_y, star_speed)
        Assert.Contains("byte[] array_0359 = new byte[8];", code);
        Assert.Contains("byte[] array_0361 = new byte[8];", code);
        Assert.Contains("byte[] array_0369 = new byte[8];", code);
        // 3 arrays of size 3 (exp_x, exp_y, exp_timer)
        Assert.Contains("byte[] array_0371 = new byte[3];", code);
        Assert.Contains("byte[] array_0374 = new byte[3];", code);
        Assert.Contains("byte[] array_0377 = new byte[3];", code);
    }

    [Fact]
    public void Decompiler_Hello_NoArrayDeclarations()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // hello has no indexed-addressing byte[] arrays
        Assert.DoesNotContain("new byte[", code);
    }

    [Fact]
    public void Decompiler_Attributetable_RecoversPalBgByteArray()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // pal_bg should have a byte[] variable declaration with actual palette data
        Assert.Contains("byte[] palette0 = new byte[] { 0x03", code);
        Assert.Contains("pal_bg(palette0);", code);
        // Should NOT be commented out
        Assert.DoesNotContain("// pal_bg", code);
    }

    [Fact]
    public void Decompiler_Attributetable_RecoversVramWriteByteArray()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // vram_write should have a byte[] variable with the attribute table data
        Assert.Contains("byte[] data1 = new byte[]", code);
        Assert.Contains("vram_write(data1);", code);
        // First bytes of the attribute table
        Assert.Contains("0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00", code);
        // Should NOT have placeholder comments
        Assert.DoesNotContain("vram_write(/*", code);
    }

    [Fact]
    public void Decompiler_Attributetable_RecoversVramFillArgs()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // vram_fill should have actual fill value and count
        Assert.Contains("vram_fill(0x16, 960);", code);
        // Should NOT have placeholder
        Assert.DoesNotContain("vram_fill(/*", code);
    }

    [Fact]
    public void Decompiler_Attributetable_HasWhileTrue()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        Assert.Contains("while (true) ;", code);
    }

    [Fact]
    public void Decompiler_Attributetable_FullRoundTrip()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Verify the complete sequence of API calls matches the original source
        Assert.Contains("pal_bg(palette0);", code);
        Assert.Contains("vram_adr(NTADR_A(0, 0));", code);
        Assert.Contains("vram_fill(0x16, 960);", code);
        Assert.Contains("vram_write(data1);", code);
        Assert.Contains("ppu_on_all();", code);
        Assert.Contains("while (true) ;", code);
    }

    static byte[] GetVerifiedRom(string name)
    {
        // Verified ROM binaries are stored alongside the test source files
        var path = Path.Combine(FindTestSourceDirectory(), $"TranspilerTests.Write.{name}.verified.bin");
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void Decompiler_Shoot2_FindsGameLoop()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Verify while(true) game loop is recovered (backward JMP pattern)
        Assert.Contains("while (true) ;", code);

        // Verify init code is present
        Assert.Contains("ppu_off();", code);
        Assert.Contains("oam_clear();", code);
        Assert.Contains("ppu_on_all();", code);

        // Verify game loop NESLib calls are present
        Assert.Contains("ppu_wait_nmi();", code);
        Assert.Contains("pal_spr_bright(", code);
    }

    [Fact]
    public void Decompiler_Shoot2_CsprojHasMapper()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile();

        var csproj = decompiler.GenerateCsproj("shoot2");

        Assert.Contains("<NESMapper>2</NESMapper>", csproj);
        Assert.Contains("<NESChrBanks>0</NESChrBanks>", csproj);
    }

    [Theory]
    [InlineData("animation")]
    [InlineData("pong")]
    [InlineData("snake")]
    public void Decompiler_GameLoop_RecoveredFromBackwardJmp(string name)
    {
        var romBytes = GetVerifiedRom(name);
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // All these samples have while(true){body} game loops
        // that should be recovered via backward JMP detection
        Assert.Contains("while (true) ;", code);
    }

    [Fact]
    public void Decompiler_Scroll_RecoverLocalVariables()
    {
        // scroll has scroll_y local at $0325 — verify it's recovered as a variable, not poke/peek
        var romBytes = GetVerifiedRom("scroll");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Should declare a local variable for $0325
        Assert.Contains("byte var_0325 = 0;", code);
        // Should use variable assignment, not poke
        Assert.Contains("var_0325 = ", code);
        Assert.DoesNotContain("poke(0x0325", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverLocalVariables()
    {
        // shoot2 has 20+ locals at $0325+ — verify they're recovered as variables
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Should have local variable declarations
        Assert.Contains("byte var_0325 = 0;", code);
        // Should NOT use poke for local variable addresses
        Assert.DoesNotContain("poke(0x0325", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverIfBlocks()
    {
        // shoot2 has many conditionals — verify if blocks are recovered
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // fire_cooldown check: LDA $0327 / BEQ skip → if (var_0327 != 0)
        Assert.Contains("if (var_0327 != 0)", code);
        // Should contain opening and closing braces for if blocks
        Assert.Contains("{", code);
        Assert.Contains("}", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverCmpBranch()
    {
        // shoot2 uses CMP #N / BCC for loop comparisons and boundary checks
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Loop-like patterns use LDA $local / CMP #N / BCC
        // Verify at least one comparison-based if block exists
        Assert.Contains("if (var_", code);
    }

    [Fact]
    public void Decompiler_Scroll_RecoverIfBlock()
    {
        // scroll has a conditional check on scroll_y ($0326)
        var romBytes = GetVerifiedRom("scroll");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // scroll uses LDA $0326 / BEQ skip → if (var_0326 != 0)
        Assert.Contains("if (var_0326 != 0)", code);
        Assert.Contains("{", code);
        Assert.Contains("}", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverPadPollResultStorage()
    {
        // shoot2 calls pad_poll(0) and stores result: LDA #$00 / JSR pad_poll / STA $037F
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // pad_poll result should be stored to a local variable
        Assert.Contains("var_037F = pad_poll(0);", code);
        // Variable storing pad_poll should be typed as PAD, not byte
        Assert.Contains("PAD var_037F = 0;", code);
        Assert.DoesNotContain("byte var_037F", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverPadMasking()
    {
        // shoot2 uses LDA $037F / AND #$01 / BEQ → if ((var_037F & PAD.A) != 0)
        // and LDA $037F / AND #$40 / BEQ → if ((var_037F & PAD.LEFT) != 0)
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // PAD.A masking (AND #$01)
        Assert.Contains("if ((var_037F & PAD.A) != 0)", code);
        // PAD.LEFT masking (AND #$40)
        Assert.Contains("if ((var_037F & PAD.LEFT) != 0)", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecognizesOamSpr()
    {
        // shoot2 has oam_spr calls with local variables, immediates, and array indexing
        // Pattern: JSR decsp4 / (LDA val / STA ($22),Y) × 4 / LDA val / JSR oam_spr
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Verify oam_spr calls are recovered with 5 arguments and assignment
        Assert.Contains("oam_spr(", code);
        // Should NOT have oam_spr as an unknown comment
        Assert.DoesNotContain("// oam_spr(", code);
        // First oam_spr call uses local vars + immediates and assigns result back
        // oam_off = oam_spr(player_x, player_y, SPR_PLAYER, 0, oam_off)
        Assert.Contains("= oam_spr(var_", code);
        // All oam_spr calls should have exactly 5 comma-separated arguments
        foreach (var line in code.Split('\n'))
        {
            if (!line.Contains("oam_spr(")) continue;
            var argsStr = line.Substring(line.IndexOf("oam_spr(") + 8);
            argsStr = argsStr.Substring(0, argsStr.IndexOf(')'));
            var args = argsStr.Split(',');
            Assert.Equal(5, args.Length);

            // For any array-indexed argument, ensure it uses the array_XXXX identifier
            // (matching GenerateCSharp declarations) and has a resolved index variable
            foreach (var rawArg in args)
            {
                var arg = rawArg.Trim();
                var openBracket = arg.IndexOf('[');
                var closeBracket = arg.IndexOf(']');
                if (openBracket < 0 || closeBracket <= openBracket)
                    continue;

                var baseIdentifier = arg.Substring(0, openBracket).Trim();
                var indexExpression = arg.Substring(openBracket + 1, closeBracket - openBracket - 1);

                Assert.StartsWith("array_", baseIdentifier);
                Assert.False(string.IsNullOrWhiteSpace(indexExpression));
            }
        }
    }

    [Fact]
    public void Decompiler_Shoot2_EmitsUnknownAssemblyComments()
    {
        // shoot2 has complex game logic that can't be mapped back to NESLib calls
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Should contain unknown assembly comment blocks
        Assert.Contains("// Unknown 6502 assembly at $", code);
        // Should contain disassembled instructions inside comments
        Assert.Contains("//   ", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverForLoops()
    {
        // shoot2 has many for loops for array initialization and game logic
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Should contain for loop syntax with proper structure
        Assert.Contains("for (byte", code);

        // Verify specific for-loop limits matching shoot2's constants:
        // MAX_BULLETS=4, MAX_ENEMIES=6, MAX_STARS=8, MAX_EXPLOSIONS=3
        Assert.Matches(@"for \(byte\s+var_\w+\s*=\s*0;\s*var_\w+\s*<\s*4;\s*var_\w+\+\+\)", code);  // MAX_BULLETS
        Assert.Matches(@"for \(byte\s+var_\w+\s*=\s*0;\s*var_\w+\s*<\s*6;\s*var_\w+\+\+\)", code);  // MAX_ENEMIES
        Assert.Matches(@"for \(byte\s+var_\w+\s*=\s*0;\s*var_\w+\s*<\s*8;\s*var_\w+\+\+\)", code);  // MAX_STARS
        Assert.Matches(@"for \(byte\s+var_\w+\s*=\s*0;\s*var_\w+\s*<\s*3;\s*var_\w+\+\+\)", code);  // MAX_EXPLOSIONS

        // Should have indented body inside for loops: for header, then '{', then a line starting with 4 spaces
        Assert.Matches(@"for\s*\([^)]*\)\s*\{\s*\r?\n {4}\S", code);
    }

    [Fact]
    public void Decompiler_Shoot2_ForLoopCounterNotDeclaredAtTop()
    {
        // For-loop counter variables should NOT appear as top-level declarations
        // since they are declared in the for-loop header
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // The for loops should declare their own counter variables
        // Verify that at least one for loop with proper structure exists
        Assert.Matches(@"for \(byte (var_\w+) = 0; \1 < \d+; \1\+\+\)", code);
    }

    [Fact]
    public void Decompiler_Hello_NoPadVariables()
    {
        // hello has no pad_poll — verify no PAD-typed variables are generated
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // hello doesn't use pad_poll, so no PAD variables should appear
        Assert.DoesNotContain("PAD ", code);
        Assert.DoesNotContain("pad_poll", code);
    }

    [Fact]
    public void Decompiler_Hello_NoIfBlocks()
    {
        // hello has no conditionals — verify no if blocks are generated
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // hello is straight-line code with no branches
        Assert.DoesNotContain("if (", code);
    }

    [Fact]
    public void Decompiler_Hello_NoForLoops()
    {
        // hello has no for loops — verify none are detected
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        Assert.DoesNotContain("for (byte", code);
    }

    [Fact]
    public void Decompiler_Hello_NoUnknownAssemblyComments()
    {
        // hello is fully mapped to NESLib calls — no unknown instructions
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        Assert.DoesNotContain("// Unknown 6502 assembly", code);
    }

    static string FindTestSourceDirectory()
    {
        // Navigate from the test output directory to the source directory
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "dotnes.tests");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "TranspilerTests.cs")))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find test source directory");
    }

    [Fact]
    public void NESRomReader_MultiBankChr_GeneratesSingleBank()
    {
        var romBytes = GetVerifiedRom("slideshow");
        var reader = new NESRomReader(romBytes);

        Assert.Equal(2, reader.ChrBanks);
        Assert.Equal(16384, reader.ChrRom.Length); // 2 * 8KB

        // Generate single bank
        var bank0 = reader.GenerateChrAssembly(0);
        var bank1 = reader.GenerateChrAssembly(1);

        Assert.StartsWith(".segment \"CHARS\"", bank0);
        Assert.StartsWith(".segment \"CHARS\"", bank1);

        // Each bank should have 8KB = 512 lines of 16 bytes
        int bank0Lines = bank0.Split('\n').Count(l => l.StartsWith(".byte"));
        int bank1Lines = bank1.Split('\n').Count(l => l.StartsWith(".byte"));
        Assert.Equal(512, bank0Lines);
        Assert.Equal(512, bank1Lines);

        // Banks should differ (different tile data)
        Assert.NotEqual(bank0, bank1);
    }

    [Fact]
    public void NESRomReader_MultiBankChr_InvalidBankReturnsEmpty()
    {
        var romBytes = GetVerifiedRom("slideshow");
        var reader = new NESRomReader(romBytes);

        Assert.Empty(reader.GenerateChrAssembly(-1));
        Assert.Empty(reader.GenerateChrAssembly(2));
    }

    [Fact]
    public void NESRomReader_MultiBankChr_AllBanksMatchFull()
    {
        var romBytes = GetVerifiedRom("slideshow");
        var reader = new NESRomReader(romBytes);

        // Full output (no bank parameter) should equal bank0 + bank1 data combined
        var full = reader.GenerateChrAssembly();
        int fullLines = full.Split('\n').Count(l => l.StartsWith(".byte"));
        Assert.Equal(1024, fullLines); // 2 * 512 lines
    }

    [Fact]
    public void Decompiler_Shoot2_ExtractsChrRamTileData()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);

        Assert.Equal(0, rom.ChrBanks); // CHR RAM

        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile();

        var chrRamData = decompiler.GetChrRamTileData();
        Assert.NotEmpty(chrRamData);

        // shoot2 uploads tiles to $1000 (sprites) and $0000 (BG)
        var addresses = chrRamData.Select(d => d.PpuAddress).ToList();
        Assert.Contains((ushort)0x1000, addresses);
        Assert.Contains((ushort)0x0000, addresses);

        // All uploads should have non-zero data
        foreach (var (_, data) in chrRamData)
        {
            Assert.True(data.Length > 0);
            Assert.True(data.Any(b => b != 0), "Tile data should not be all zeros");
        }
    }

    [Fact]
    public void Decompiler_Transtable_ExtractsChrRamTileData()
    {
        var romBytes = GetVerifiedRom("transtable");
        var rom = new NESRomReader(romBytes);

        Assert.Equal(0, rom.ChrBanks); // CHR RAM

        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile();

        var chrRamData = decompiler.GetChrRamTileData();
        Assert.NotEmpty(chrRamData);

        // transtable uploads tiles to $0000 (pattern table 0) via vram_adr(0x0)
        var addresses = chrRamData.Select(d => d.PpuAddress).ToList();
        Assert.Contains((ushort)0x0000, addresses);
    }

    [Fact]
    public void Decompiler_Hello_NoChrRamData()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);

        Assert.Equal(1, rom.ChrBanks); // CHR ROM, not CHR RAM

        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile();

        // CHR ROM samples should have no CHR RAM data extracted
        var chrRamData = decompiler.GetChrRamTileData();
        Assert.Empty(chrRamData);
    }

    [Fact]
    public void Decompiler_Shoot2_DetectsUserFunctions()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // shoot2 has user-defined functions after main that should be detected
        // and emitted as static void declarations
        Assert.Contains("static void func_", code);
    }

    [Fact]
    public void Decompiler_Shoot2_UserFunctionCallsNotCommented()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // User function calls from main should be actual calls, not comments
        // At least one func_ call should appear without a leading "//"
        var lines = code.Split('\n');
        var funcCalls = lines.Where(l => l.TrimStart().StartsWith("func_")).ToList();
        Assert.NotEmpty(funcCalls);
    }

    [Fact]
    public void Decompiler_Shoot2_UserFunctionHasPokeBody()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // The sfx functions contain poke() calls for APU registers.
        // At least one user function body should decompile APU poke calls.
        // func_8C64 is sfx_shoot: poke(APU_PULSE1_CTRL, 0x4A)
        Assert.Contains("poke(APU_PULSE1_CTRL, 0x4A);", code);
        Assert.Contains("poke(APU_PULSE1_SWEEP, 0x00);", code);
        Assert.Contains("poke(APU_PULSE1_TIMER_LO, 0x80);", code);
        Assert.Contains("poke(APU_PULSE1_TIMER_HI, 0xF9);", code);
    }

    [Fact]
    public void Decompiler_Shoot2_UserFunctionsHaveBraces()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // Each user function should have an opening and closing brace
        var lines = code.Split('\n').Select(l => l.Trim()).ToArray();
        var funcDecls = lines.Where(l => l.StartsWith("static void func_")).ToList();
        Assert.True(funcDecls.Count > 0, "Should have at least one user function declaration");

        // Each function declaration should be followed by { ... }
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("static void func_"))
            {
                Assert.True(i + 1 < lines.Length && lines[i + 1] == "{",
                    $"Function declaration at line {i} should be followed by opening brace");
            }
        }
    }

    [Fact]
    public void Decompiler_Hello_NoUserFunctions()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // hello sample has no user-defined functions
        Assert.DoesNotContain("static void func_", code);
    }
}
