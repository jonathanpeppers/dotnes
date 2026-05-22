using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class LocalsTests : RoslynTests
{
    public LocalsTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void EnumVariable()
    {
        // Enums compile to plain integer IL — no special transpiler support needed
        var bytes = GetProgramBytes(
            """
            pal_col(0, 0x30);
            GameState state = GameState.Playing;
            if (state == GameState.Playing)
                pal_col(1, 0x14);
            if (state == GameState.GameOver)
                pal_col(2, 0x27);
            ppu_on_all();
            while (true) ;

            enum GameState : byte { Title, Playing, GameOver }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        // Verify key patterns: store enum value, compare, branch
        var hex = Convert.ToHexString(bytes);
        // GameState.Playing = 1, stored with LDA #$01 (A901)
        Assert.Contains("A901", hex);
        // CMP #$01 (C901) for == Playing
        Assert.Contains("C901", hex);
        // CMP #$02 (C902) for == GameOver
        Assert.Contains("C902", hex);
    }

    [Fact]
    public void StaticFieldStore()
    {
        // Static field via class — produces stsfld/ldsfld IL
        var bytes = GetProgramBytes(
            """
            State.brightness = 4;
            pal_bright(State.brightness);
            ppu_on_all();
            while (true) ;

            static class State { public static byte brightness; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // STA absolute for store, LDA absolute for load
        Assert.Contains("8D", hex); // STA abs
        Assert.Contains("AD", hex); // LDA abs
    }

    [Fact]
    public void StaticFieldInLoop()
    {
        // Static field read and written in a loop — like climber.c globals
        var bytes = GetProgramBytes(
            """
            State.counter = 0;
            while (State.counter < 10) {
                pal_col(0, State.counter);
                State.counter = (byte)(State.counter + 1);
            }
            ppu_on_all();
            while (true) ;

            static class State { public static byte counter; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should have STA absolute and LDA absolute for the static field
        Assert.Contains("8D", hex);
        Assert.Contains("AD", hex);
    }

    [Fact]
    public void LocalVarInFunction()
    {
        // Local variable passed to a user function — same as static field version
        var bytes = GetProgramBytes(
            """
            byte brightness = 4;
            apply_bright(brightness);
            ppu_on_all();
            while (true) ;

            static void apply_bright(byte b) { pal_bright(b); }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should have JSR calls: pusha, apply_bright, ppu_on_all
        int jsrCount = hex.Split("20").Length - 1;
        Assert.True(jsrCount >= 2, $"Expected at least 2 JSR calls, got {jsrCount}");
    }

    [Fact]
    public void LocalVarInLoop()
    {
        // Same logic as StaticFieldInLoop but using locals (already works)
        var bytes = GetProgramBytes(
            """
            byte counter = 0;
            while (counter < 10) {
                pal_col(0, counter);
                counter = (byte)(counter + 1);
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("8D", hex); // STA absolute for local
        Assert.Contains("AD", hex); // LDA absolute for local reload
    }

    [Fact]
    public void SbyteNegativeConstant()
    {
        // sbyte -1 should emit LDA #$FF (two's complement)
        var bytes = GetProgramBytes(
            """
            sbyte vel = -1;
            pal_col(0, (byte)vel);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A9FF", hex); // LDA #$FF (-1 in two's complement)
    }

    [Fact]
    public void SbyteNegativeArithmetic()
    {
        // sbyte arithmetic: -5 + 3 = -2 (0xFE)
        var bytes = GetProgramBytes(
            """
            sbyte x = -5;
            x = (sbyte)(x + 3);
            pal_col(0, (byte)x);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A9FB", hex); // LDA #$FB (-5 in two's complement)
    }

    [Fact]
    public void IntPtrSize()
    {
        // IntPtr.Size should be 1 on the 6502 (8-bit CPU)
        var bytes = GetProgramBytes(
            """
            byte size = (byte)System.IntPtr.Size;
            pal_col(0, size);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A901", hex);  // LDA #$01 (IntPtr.Size = 1)
    }

    [Fact]
    public void SizeofNint()
    {
        // sizeof(nint) produces the same 6502 output as IntPtr.Size
        var bytes = GetProgramBytes(
            """
            unsafe
            {
                byte size = (byte)sizeof(nint);
                pal_col(0, size);
            }
            ppu_on_all();
            while (true) ;
            """,
            additionalAssemblyFiles: null,
            allowUnsafe: true);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A901", hex);  // LDA #$01 (sizeof(nint) = 1)
    }

    [Fact]
    public void SizeofUshort()
    {
        // sizeof(ushort) is folded by Roslyn to ldc.i4.2 at compile time (no Sizeof opcode)
        var bytes = GetProgramBytes(
            """
            unsafe
            {
                byte size = (byte)sizeof(ushort);
                pal_col(0, size);
            }
            ppu_on_all();
            while (true) ;
            """,
            additionalAssemblyFiles: null,
            allowUnsafe: true);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A902", hex);  // LDA #$02 (sizeof(ushort) = 2, folded by Roslyn)
    }

    [Fact]
    public void SizeofByte()
    {
        // sizeof(byte) is folded by Roslyn to ldc.i4.1 at compile time (no Sizeof opcode)
        var bytes = GetProgramBytes(
            """
            unsafe
            {
                byte size = (byte)sizeof(byte);
                pal_col(0, size);
            }
            ppu_on_all();
            while (true) ;
            """,
            additionalAssemblyFiles: null,
            allowUnsafe: true);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A901", hex);  // LDA #$01 (sizeof(byte) = 1, folded by Roslyn)
    }

    [Fact]
    public void UshortReturnFromFunction()
    {
        // User function returning ushort
        var bytes = GetProgramBytes(
            """
            ushort val = get_addr(5);
            pal_col(0, (byte)val);
            ppu_on_all();
            while (true) ;

            static ushort get_addr(byte x) => (ushort)(x * 8 + 16);
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A905", hex); // LDA #$05 (argument)
    }

    [Fact]
    public void UshortStoreAndLoad()
    {
        // Store ushort to local, load it back, use high/low bytes
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            ushort yy = (ushort)(x * 8 + 16);
            pal_col(0, (byte)yy);
            pal_col(1, (byte)(yy >> 8));
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void ByteMaxValue()
    {
        // Regression test: byte value 255 must use 1-byte store path.
        // Without the <= byte.MaxValue fix, 255 falls to the ushort branch
        // which calls RemoveLastInstructions(2) when only 1 instruction was
        // emitted by WriteLdc(byte), corrupting the preceding JSR.
        var bytes = GetProgramBytes("""
            pal_col(0, 0x30);
            byte x = 255;
            pal_col(1, x);
            while (true) ;
            """);

        var hex = Convert.ToHexString(bytes);

        // Verify correct 1-byte store: LDA #$FF (A9FF), STA $0325 (8D2503)
        Assert.Contains("A9FF", hex);
        Assert.Contains("8D2503", hex);

        // Verify no 2-byte ushort high-byte store: STX $0326 (8E2603)
        // If the ushort branch ran, it would emit STX for the high byte
        Assert.DoesNotContain("8E2603", hex);
    }

    [Fact]
    public void WordStaticField_StoreImmediate()
    {
        // ushort static field should get 2 bytes and store both lo/hi
        var bytes = GetProgramBytes(
            """
            G.word_val = 300;
            while (true) ;

            static class G { public static ushort word_val; }
            """);
        var hex = Convert.ToHexString(bytes);
        // 300 = 0x012C: LDA #$2C (A92C), STA abs (8D), LDX #$01 or STX abs (8E)
        Assert.Contains("A92C", hex); // LDA #$2C (low byte)
        Assert.Contains("8E", hex);   // STX abs (high byte store)
    }

    [Fact]
    public void WordStaticField_LoadZeroExtend()
    {
        // Storing a byte field value into a word field should zero-extend the high byte
        var bytes = GetProgramBytes(
            """
            G.byte_val = 42;
            G.int_val = G.byte_val;
            while (true) ;

            static class G
            {
                public static byte byte_val;
                public static int int_val;
            }
            """);
        var hex = Convert.ToHexString(bytes);
        // Zero-extend: LDA #$00 (A900) for high byte
        Assert.Contains("A900", hex);
    }

    [Fact]
    public void WordStaticField_LoadWord()
    {
        // Loading a ushort field should emit LDA abs + LDX abs (16-bit)
        var bytes = GetProgramBytes(
            """
            G.word_val = 300;
            byte lo = (byte)G.word_val;
            pal_col(0, lo);
            while (true) ;

            static class G { public static ushort word_val; }
            """);
        var hex = Convert.ToHexString(bytes);
        // LDX absolute = AE (loading high byte of word field)
        Assert.Contains("AE", hex);
    }

    [Fact]
    public void WordStaticField_TwoByteAllocation()
    {
        // Word fields should not overlap adjacent fields.
        // byte_val at $0325 (1 byte), int_val at $0326 (2 bytes), word_val at $0328 (2 bytes)
        var bytes = GetProgramBytes(
            """
            G.byte_val = 1;
            G.int_val = 2;
            G.word_val = 3;
            while (true) ;

            static class G
            {
                public static byte byte_val;
                public static int int_val;
                public static ushort word_val;
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"WordStaticField_TwoByteAllocation hex: {hex}");

        // Fields are allocated alphabetically: byte_val@$0325, int_val@$0326-7, word_val@$0328-9
        // Store byte_val=1: LDA #1 (A901), STA $0325 (8D2503)
        Assert.Contains("8D2503", hex);
        // Store int_val=2 low: STA $0326 (8D2603)
        Assert.Contains("8D2603", hex);
        // Store int_val=2 high: STA $0327 (8D2703) — zero or value high byte
        Assert.Contains("8D2703", hex);
        // Store word_val=3 low: STA $0328 (8D2803)
        Assert.Contains("8D2803", hex);
        // Store word_val=3 high: STA $0329 (8D2903)
        Assert.Contains("8D2903", hex);
    }

    // ===================================================================
    // Converted from pre-compiled test DLLs → inline Roslyn tests
    // ===================================================================

    [Fact]
    public void StaticFieldOverflow_Throws()
    {
        // Allocating too many static fields must throw, not silently corrupt memory.
        // MaxLocalBytes is 0x0800 - 0x0325 = 1243 bytes.
        // 13 byte[100] arrays = 1300 bytes, which exceeds the limit.
        var fieldDecls = new System.Text.StringBuilder();
        var fieldUsage = new System.Text.StringBuilder();
        for (int i = 0; i < 13; i++)
        {
            fieldDecls.AppendLine($"    public static byte[] f{i:D2};");
            fieldUsage.AppendLine($"G.f{i:D2} = new byte[100];");
        }

        var source = fieldUsage.ToString() + """

            ppu_on_all();
            while (true) ;

            static class G
            {
            """ + fieldDecls.ToString() + "}";

        var ex = Assert.Throws<TranspileException>(() =>
            GetProgramBytes(source));
        Assert.Contains("1300 bytes", ex.Message);
        Assert.Contains("NES RAM", ex.Message);
        Assert.Contains("$0325", ex.Message);
    }

    [Fact]
    public void IncrementLocalIndex4_UsesStlocS()
    {
        // Regression test for #485: GetStlocIndex must handle Stloc_s (local index > 3).
        // With 5+ locals, the compiler uses Stloc_s for the 5th local (index 4).
        // Before the fix, GetStlocIndex returned null for Stloc_s, so the x++ pattern
        // was not detected and the less efficient pushax/popax path was used instead of INC.
        var bytes = GetProgramBytes(
            """
            byte a = 1;
            byte b = 2;
            byte c = 3;
            byte d = 4;
            byte e = 5;
            pal_col(0, a);
            pal_col(1, b);
            pal_col(2, c);
            pal_col(3, d);
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                e++;
                pal_col(0, e);
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"IncrementLocalIndex4 hex: {hex}");

        // Local e (index 4) is at address $0329. INC absolute = EE.
        // The optimized x++ pattern should emit EE2903 (INC $0329).
        Assert.Contains("EE2903", hex);
    }

    [Fact]
    public void TwoLocals_AddModulo_AssignBack()
    {
        // Regression test: nx = (byte)((x1 + y1) % 32); x1 = nx;
        // When Roslyn optimizes away stloc/ldloc for x1 and nx, the runtime
        // arithmetic result stays in A. WriteLdloc saves it to TEMP before
        // loading y. The NTADR handler must recognize this _savedRuntimeToTemp
        // pattern: TEMP = x (runtime result), A = y (loaded from local).
        var bytes = GetProgramBytes("""
            byte y1 = 2;
            byte x1 = 5;
            byte nx;
            nx = (byte)((x1 + y1) % 32);
            x1 = nx;
            vrambuf_put(NTADR_A(x1, y1), "B");
            while (true) ;
            """);
        var hex = Convert.ToHexString(bytes);

        // CLC (18) + ADC $0325 (6D2503) for runtime addition
        Assert.Contains("186D2503", hex);

        // AND #$1F (291F) for % 32 (power-of-2 modulo)
        Assert.Contains("291F", hex);

        // STA TEMP ($17) to save runtime result before loading y
        // 8517 = STA $17 (zero page)
        Assert.Contains("8517", hex);

        // STA TEMP2 ($19) — NTADR result lo byte stored after nametable_a returns
        Assert.Contains("8519", hex);

        // Verify the runtime-NTADR sequence: STA TEMP must be immediately
        // followed by LDA absolute (AD) loading y from its local address,
        // and a JSR (20) for nametable_a must appear between the TEMP and
        // TEMP2 stores. This catches regressions where the NTADR handler
        // emits the wrong instruction sequence (e.g., popa instead of using
        // the already-saved TEMP, producing a wrong-position address).
        int tempIdx = hex.IndexOf("8517");
        int temp2Idx = hex.IndexOf("8519");
        Assert.True(tempIdx >= 0, "Expected STA TEMP (8517) in output.");
        Assert.True(temp2Idx > tempIdx, "TEMP2 store should occur after TEMP store.");

        // STA TEMP (8517) immediately followed by LDA absolute (AD) for y.
        Assert.Equal("AD", hex.Substring(tempIdx + 4, 2));

        // JSR (20) between TEMP and TEMP2 stores — the nametable_a call.
        string between = hex.Substring(tempIdx, temp2Idx - tempIdx);
        Assert.Contains("20", between);
    }
}
