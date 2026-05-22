using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ArraysTests : RoslynTests
{
    public ArraysTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Indexers()
    {
        AssertProgram(
            csharpSource:
                """
                byte[] ATTRIBUTE_TABLE = new byte[0x40] {
                  0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                  0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55,
                  0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa,
                  0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                  0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
                  0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
                  0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
                  0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f
                };
                byte[] PALETTE = new byte[16] {
                  0x03,
                  0x11,0x30,0x27,0x0,
                  0x1c,0x20,0x2c,0x0,
                  0x00,0x10,0x20,0x0,
                  0x06,0x16,0x26
                };
                ATTRIBUTE_TABLE[0] = 0x55; // This line is new
                pal_bg(PALETTE);
                vram_adr(NAMETABLE_A);
                vram_fill(0x16, 32 * 30);
                vram_write(ATTRIBUTE_TABLE);
                ppu_on_all();
                while (true) ;
                """,
            expectedAssembly:
                """
                A9F1    ; LDA array_lo (ATTRIBUTE_TABLE)
                A285    ; LDX array_hi
                20B885  ; JSR pushax
                A200    ; LDX #$00
                A940    ; LDA #$40
                20A285  ; JSR pusha
                A900    ; LDA #$00
                20A285  ; JSR pusha
                A955    ; LDA #$55
                A931    ; LDA #$31 (PALETTE address lo)
                A286    ; LDX #$86 (PALETTE address hi)
                202B82  ; JSR pal_bg
                A220    ; LDX #$20
                A900    ; LDA #$00
                20D483  ; JSR vram_adr
                A916    ; LDA #$16
                20A285  ; JSR pusha
                A203    ; LDX #$03
                A9C0    ; LDA #$C0
                20DF83  ; JSR vram_fill
                A9F1    ; LDA array_lo (ATTRIBUTE_TABLE)
                A285    ; LDX array_hi
                20B885  ; JSR pushax
                A200    ; LDX #$00
                A940    ; LDA #$40
                204F83  ; JSR vram_write
                208982  ; JSR ppu_on_all
                4C4085  ; JMP loop
                """);
    }

    [Fact]
    public void Indexers_i4()
    {
        AssertProgram(
            csharpSource:
                """
                int[] data = new int[4] { 0x12345678, 0x11111111, 0x22222222, 0x33333333 };
                data[0] = 0x55;
                ppu_on_all();
                while (true) ;
                """,
            expectedAssembly:
                """
                A900    ; LDA #$00
                206C85  ; JSR pusha
                A955    ; LDA #$55
                208982  ; JSR ppu_on_all
                4C0A85  ; JMP $850A
                """);
    }

    [Fact]
    public void FillConstant()
    {
        // Array.Fill with a constant value — no subsequent array access
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[8];
            System.Array.Fill(buf, (byte)0xFF);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A9FF", hex);  // LDA #$FF (fill value)
        Assert.Contains("A207", hex);  // LDX #$07 (size-1 = 7)
        Assert.Contains("CA", hex);    // DEX
        Assert.Contains("10FA", hex);  // BPL -6 (loop back to STA)
    }

    [Fact]
    public void FillZero()
    {
        // Array.Fill with zero — common memset(buf, 0, n) pattern
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[4];
            System.Array.Fill(buf, (byte)0);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A900", hex);  // LDA #$00 (fill value)
        Assert.Contains("A203", hex);  // LDX #$03 (size-1 = 3)
        Assert.Contains("CA", hex);    // DEX
        Assert.Contains("10FA", hex);  // BPL -6 (loop back to STA)
    }

    [Fact]
    public void CopyBasic()
    {
        // Array.Copy between two runtime arrays
        var bytes = GetProgramBytes(
            """
            byte[] src = new byte[4];
            src[0] = 0x10;
            src[1] = 0x20;
            byte[] dst = new byte[4];
            System.Array.Copy(src, dst, 4);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A200", hex);  // LDX #$00 (start index)
        Assert.Contains("E004", hex);  // CPX #$04 (length)
        Assert.Contains("D0", hex);    // BNE (loop back)
    }

    [Fact]
    public void ByteArrayConstantIndex()
    {
        // Access byte array with runtime index via for loop — pattern for type_message
        var bytes = GetProgramBytes(
            """
            byte[] msg = new byte[3];
            msg[0] = 0x48;
            msg[1] = 0x49;
            msg[2] = 0x00;
            for (byte i = 0; i < 2; i++)
            {
                pal_col(i, msg[i]);
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A948", hex); // LDA #$48 ('H') in stelem
    }

    [Fact]
    public void LdelemSubPreservesFirstOperand()
    {
        // Pattern from climber: row - heights[f] where both are loop variables.
        // The ldelem clobbers A (which held row). The transpiler must save row to TEMP
        // before the ldelem, then use the _savedRuntimeToTemp path in Sub.
        var bytes = GetProgramBytes(
            """
            byte[] heights = new byte[4];
            heights[0] = 10;
            for (byte row = 0; row < 4; row++)
            {
                for (byte f = 0; f < 4; f++)
                {
                    byte diff = (byte)(row - heights[f]);
                    pal_col(0, diff);
                }
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Sub hex: {hex}");
        // Must contain STA $17 (8517) to save row to TEMP before ldelem clobbers A
        Assert.Contains("8517", hex);
        // Must contain SBC $18 (E518) — TEMP - TEMP+1 pattern for the subtraction
        Assert.Contains("E518", hex);
    }

    [Fact]
    public void LdelemAddPreservesFirstOperand()
    {
        // Pattern: val + arr[idx] with loop variables — addition is commutative.
        // The transpiler should save val to TEMP, then CLC; ADC TEMP.
        var bytes = GetProgramBytes(
            """
            byte[] offsets = new byte[4];
            offsets[0] = 5;
            for (byte row = 0; row < 4; row++)
            {
                for (byte f = 0; f < 4; f++)
                {
                    byte total = (byte)(row + offsets[f]);
                    pal_col(0, total);
                }
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Add hex: {hex}");
        // Must contain STA $17 (8517) to save row to TEMP
        Assert.Contains("8517", hex);
        // Must contain ADC $17 (6517) — add from TEMP
        Assert.Contains("6517", hex);
    }

    [Fact]
    public void StelemWithIndexArithmetic()
    {
        // Pattern from climber: buf[(byte)(col + 1)] = (byte)(CH_FLOOR + 2)
        // The stelem handler must apply the +1 offset to the index when storing.
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[30];
            for (byte col = 0; col < 30; col += 2)
            {
                buf[col] = 0xF4;
                buf[(byte)(col + 1)] = 0xF6;
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemIndexArith hex: {hex}");
        // Must contain CLC (18) and ADC #$01 (6901) for computing col + 1
        Assert.Contains("186901", hex);
        // Must contain TAX (AA) to move computed index to X
        Assert.Contains("AA", hex);
    }

    [Fact]
    public void StelemWithTwoLocalIndexArithmetic()
    {
        // Pattern from climber: buf[(byte)(offset + j)] = 0
        // The stelem handler must compute X = offset + j at runtime.
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[30];
            byte offset = 4;
            for (byte j = 0; j < 4; j++)
            {
                buf[(byte)(offset + j)] = 0;
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemTwoLocal hex: {hex}");
        // Must contain CLC (18) for computing offset + j
        Assert.Contains("18", hex);
        // Must contain TAX (AA) to move computed index to X
        Assert.Contains("AA", hex);
        // The value 0 must be stored: LDA #$00 (A900) followed by STA TEMP (8517)
        Assert.Contains("A900", hex);
    }

    [Fact]
    public void StelemSameArrayDifferentIndex()
    {
        // Pattern: arr[i] = arr[j] — same-array copy with different indices
        // IL: ldloc arr, ldloc i, ldloc arr, ldloc j, ldelem.u1, stelem.i1
        // Should emit: LDX j_addr; LDA arr,X; LDX i_addr; STA arr,X
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[4];
            byte i = 2;
            byte j = 0;
            arr[i] = arr[j];
            pal_col(0, arr[i]);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemSameArrayDifferentIndex hex: {hex}");

        // Verify the contiguous LDX(src) -> LDA(arr,X) -> LDX(dst) -> STA(arr,X) sequence
        // AE xx xx BD yy yy AE zz zz 9D yy yy  (yy yy must match for same array)
        bool foundSequence = false;
        for (int i = 0; i <= bytes.Length - 12; i++)
        {
            if (bytes[i] == 0xAE          // LDX abs (source index)
                && bytes[i + 3] == 0xBD   // LDA abs,X (load from array)
                && bytes[i + 6] == 0xAE   // LDX abs (target index)
                && bytes[i + 9] == 0x9D   // STA abs,X (store to array)
                && bytes[i + 4] == bytes[i + 10]   // array addr lo must match
                && bytes[i + 5] == bytes[i + 11])  // array addr hi must match
            {
                // Source and target index addresses must differ
                if (bytes[i + 1] != bytes[i + 7] || bytes[i + 2] != bytes[i + 8])
                {
                    foundSequence = true;
                    break;
                }
            }
        }
        Assert.True(foundSequence, "Expected contiguous LDX(src) -> LDA(arr,X) -> LDX(dst) -> STA(arr,X) sequence not found");
    }

    [Fact]
    public void CompoundArrayIncrementByConstant()
    {
        // Pattern: arr[i] += 2 generates ldelema System.Byte / dup / ldind.u1 / ldc.i4.2 / add / conv.u1 / stind.i1
        // Should emit: LDX i_addr; LDA arr,X; CLC; ADC #02; STA arr,X
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[4];
            byte i = 0;
            arr[i] += 2;
            pal_col(0, arr[i]);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"CompoundIncr hex: {hex}");
        // Must contain ADC #$02 (6902) for the += 2
        Assert.Contains("6902", hex);
    }

    [Fact]
    public void CompoundArrayDecrement()
    {
        // Pattern: arr[i]-- generates ldelema System.Byte / dup / ldind.u1 / ldc.i4.1 / sub / conv.u1 / stind.i1
        // Should emit: LDX i_addr; LDA arr,X; SEC; SBC #01; STA arr,X
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[4];
            byte i = 0;
            arr[i]--;
            pal_col(0, arr[i]);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"CompoundDecr hex: {hex}");
        // Must contain SBC #$01 (E901) for the --
        Assert.Contains("E901", hex);
    }

    [Fact]
    public void LdelemConstantIndexCompareWithConstant()
    {
        // Pattern from climber: while (actor_floor[0] != MAX_FLOORS - 1)
        // After ldelem.u1 with constant index 0, the next comparison should be
        // CMP #constant, NOT CMP $array_addr (which would compare stale A with
        // the array element instead of comparing the element with the constant).
        var bytes = GetProgramBytes(
            """
            byte[] states = new byte[8];
            states[0] = 1;
            while (states[0] != 19)
            {
                states[0] = (byte)(states[0] + 1);
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"LdelemCmp hex: {hex}");
        // Must contain CMP #$13 (C913) — comparing element with constant 19
        Assert.Contains("C913", hex);
        // Must NOT contain only CMP $0325 (CD2503) without a preceding LDA —
        // that would mean comparing stale A with the array, not the element with 19
    }

    [Fact]
    public void LdelemDoesNotLeaveStaleSavedRuntimeToTemp()
    {
        // Bug: HandleLdelemU1 removes instructions (including STA $17 from WriteLdloc)
        // but did not clear _savedRuntimeToTemp. This caused HandleRem to use the
        // "runtime divisor in TEMP" path instead of the constant divisor path,
        // computing dividend % TEMP (stale rh) instead of dividend % 60.
        // Pattern: inner loop with ldelem comparisons (value stays on eval stack,
        // no explicit stloc), then modulo with constant after the loop.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[4];
            byte[] vals = new byte[4];
            arr[0] = 3;
            vals[0] = 5;
            byte rh = 10;
            for (byte f = 0; f < 4; f++)
            {
                if ((byte)(rh - arr[f]) < vals[f])
                {
                    break;
                }
            }
            byte result = (byte)(rh % 60);
            NESLib.pal_col(0, result);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"LdelemRem hex: {hex}");

        // The modulo must use CMP #$3C (C93C) for constant 60, NOT CMP $19 (C519)
        // which would mean using stale TEMP2 as divisor
        Assert.Contains("C93C", hex);
    }

    [Fact]
    public void LdelemAfterSubSavesToTempForBranchComparison()
    {
        // Bug: HandleLdelemU1's "save preceding value" check only looked for LDA as the
        // last instruction. After SUB, the last instruction is SBC, so the computed dy
        // value in A was NOT saved to TEMP. The ldelem's LDA then overwrote dy.
        // Pattern: dy = rh - arr[0]; if (dy < vals[0]) — the second ldelem must save dy.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[4];
            byte[] vals = new byte[4];
            arr[0] = 2;
            vals[0] = 6;
            byte rh = 3;
            byte dy = (byte)(rh - arr[0]);
            if (dy < vals[0])
            {
                NESLib.pal_col(0, 1);
            }
            NESLib.pal_col(0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"LdelemBranch hex: {hex}");

        // After SBC (dy computation), there must be a STA $17 (8517) to save dy to TEMP
        // before the LDA that loads vals[0] for the comparison.
        // The branch comparison should use CMP (Cxxx) not a bare LDA overwrite.
        // Specifically: SBC ... STA $17 ... LDA $addr ... CMP pattern
        Assert.Contains("8517", hex); // STA $17 (save dy to TEMP)
    }

    [Fact]
    public void ComplexArrayIndexExpression()
    {
        // Pattern from issue: array[(x >> 3) + ((y >> 3) << 4)]
        // The for loop forces the array into a local variable (stloc) so that
        // the ldelem.u1 is preceded by ldloc (array), <complex index>, ldelem.u1.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[16];
            for (byte i = 0; i < 16; i++)
            {
                arr[i] = i;
            }
            byte x = (byte)pad_poll(0);
            byte y = (byte)pad_poll(0);
            byte tile = arr[(byte)((x >> 3) + ((y >> 3) << 4))];
            pal_col(0, tile);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"ComplexArrayIndex hex: {hex}");
        // Three consecutive LSR A (4A) for >> 3 shift
        Assert.Contains("4A4A4A", hex);
        // Four consecutive ASL A (0A) for << 4 shift
        Assert.Contains("0A0A0A0A", hex);
        // TAX (AA) immediately followed by LDA absolute,X (BD) for indexed array access
        Assert.Contains("AABD", hex);
    }

    [Fact]
    public void SimpleShiftArrayIndex()
    {
        // Simpler complex index: array[x >> 3]
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[16];
            for (byte i = 0; i < 16; i++)
            {
                arr[i] = i;
            }
            byte x = (byte)pad_poll(0);
            byte tile = arr[(byte)(x >> 3)];
            pal_col(0, tile);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"SimpleShiftIndex hex: {hex}");
        // Three consecutive LSR A (4A) followed by TAX (AA) + LDA absolute,X (BD)
        // This is the full expected sequence: shift index, transfer to X, load from array
        Assert.Contains("4A4A4AAABD", hex);
    }

    [Fact]
    public void ComplexArrayIndex_PreservesOperandInTemp()
    {
        // Verifies that a runtime value computed before the array access
        // is preserved in TEMP ($17) across the complex index computation.
        // Pattern: (x - y) + arr[(byte)(x >> 3)]
        // The subtraction result must survive through the shift/TAX/LDA sequence.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[32];
            for (byte i = 0; i < 32; i++)
            {
                arr[i] = i;
            }
            byte x = (byte)pad_poll(0);
            byte y = (byte)pad_poll(0);
            byte result = (byte)((byte)(x - y) + arr[(byte)(x >> 3)]);
            pal_col(0, result);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"PreservesOperandInTemp hex: {hex}");
        // STA $17 (8517) must appear before the index computation to save (x - y) to TEMP
        Assert.Contains("8517", hex);
        // TAX + LDA absolute,X for the indexed array access
        Assert.Contains("AABD", hex);
    }

    [Fact]
    public void StaticFieldArraySimpleIndex()
    {
        // G.arr[G.i] where both arr and i are static fields.
        // IL pattern: ldsfld arr; ldsfld i; ldelem.u1
        // Fields: i at $0325 (1 byte), arr at $0326 (4 bytes) — alphabetical order
        var bytes = GetProgramBytes(
            """
            G.arr = new byte[4];
            G.i = 0;
            pal_col(0, G.arr[G.i]);
            ppu_on_all();
            while (true) ;

            static class G
            {
                public static byte[] arr;
                public static byte i;
            }
            """);
        var hex = Convert.ToHexString(bytes);

        // G.arr[G.i]: LDA $0325 (AD 25 03); TAX (AA); LDA $0326,X (BD 26 03)
        Assert.Contains("AD2503", hex);  // LDA $0325 — load G.i
        Assert.Contains("AA", hex);      // TAX — transfer index to X
        Assert.Contains("BD2603", hex);  // LDA $0326,X — load G.arr[X]
    }

    [Fact]
    public void StaticFieldArrayConstantIndex()
    {
        // G.arr[0] where arr is a static field, index is constant.
        // arr is the only field: allocated at $0325 (4 bytes)
        var bytes = GetProgramBytes(
            """
            G.arr = new byte[4];
            pal_col(0, G.arr[0]);
            ppu_on_all();
            while (true) ;

            static class G
            {
                public static byte[] arr;
            }
            """);
        var hex = Convert.ToHexString(bytes);

        // G.arr[0]: LDA $0325 (AD 25 03) — constant index 0, direct absolute load
        Assert.Contains("AD2503", hex);  // LDA $0325 (absolute, arr base)
    }

    [Fact]
    public void StaticFieldArrayStelem()
    {
        // G.arr[G.i] = 42 where arr and i are static fields.
        // Fields: i at $0325 (1 byte), arr at $0326 (4 bytes)
        var bytes = GetProgramBytes(
            """
            G.arr = new byte[4];
            G.i = 0;
            G.arr[G.i] = 42;
            ppu_on_all();
            while (true) ;

            static class G
            {
                public static byte[] arr;
                public static byte i;
            }
            """);
        var hex = Convert.ToHexString(bytes);

        // G.arr[G.i] = 42: value 42 stored at arr base + index
        Assert.Contains("A92A", hex);    // LDA #42 — load value
        Assert.Contains("9D2603", hex);  // STA $0326,X — store to G.arr[X]
    }

    [Fact]
    public void Stelem_TwoLocalsAdd()
    {
        // arr[i] = (byte)(local1 + local2) — both operands are locals.
        // Regression test: previously the second ldloc overwrote the first,
        // emitting ADC #$00 instead of ADC local2.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[8];
            byte i = 0;
            byte prev = 10;
            byte dv = 5;
            arr[i] = (byte)(prev + dv);
            while (true) ;
            """);
        var hex = Convert.ToHexString(bytes);

        // Must emit CLC + ADC Absolute (opcode 6D), not ADC #imm (opcode 69)
        Assert.Contains("18", hex);       // CLC
        Assert.Contains("6D", hex);       // ADC Absolute
        // Must NOT contain ADC #$00 (the old buggy pattern: 6900)
        Assert.DoesNotContain("6900", hex);
    }

    [Fact]
    public void Stelem_TwoLocalsSub()
    {
        // arr[i] = (byte)(local1 - local2) — subtraction variant.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[8];
            byte i = 0;
            byte prev = 10;
            byte dv = 3;
            arr[i] = (byte)(prev - dv);
            while (true) ;
            """);
        var hex = Convert.ToHexString(bytes);

        // Must emit SEC + SBC Absolute (opcode ED), not SBC #imm (opcode E9)
        Assert.Contains("38", hex);       // SEC
        Assert.Contains("ED", hex);       // SBC Absolute
    }

    [Fact]
    public void StelemI1_ArrayElementIndex()
    {
        // Regression: In the climber sample, the pattern:
        //   buf[(byte)(arr2[f] * 2)] = value;
        //   buf[(byte)(arr2[f] * 2 + 1)] = value2;
        // The transpiler treated arr2 as a scalar index variable, emitting
        // LDX arr2_addr (loading arr2[0]) instead of LDX f; LDA arr2,X.
        // The * 2 and + 1 arithmetic were also lost. Both tiles of each pair
        // wrote to the same position (arr2[0]), making items appear at the
        // wrong location.
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[30];
            byte[] positions = new byte[4];
            positions[0] = 3;
            positions[1] = 5;
            positions[2] = 7;
            positions[3] = 9;
            for (byte f = 0; f < 4; f++)
            {
                buf[(byte)(positions[f] * 2)] = (byte)(f + 1);
                buf[(byte)(positions[f] * 2 + 1)] = (byte)(f + 3);
            }
            vram_adr(0x2000);
            vram_write(buf);
            ppu_on_all();
            while (true) ;
            """);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemI1_ArrayElementIndex hex: {hex}");

        // STA $17 (85 17) — save computed index to TEMP
        Assert.Contains("8517", hex);

        // LDX $17 (A6 17) — reload index from TEMP
        Assert.Contains("A617", hex);

        // ASL + STA TEMP (first index: positions[f] * 2, no add)
        Assert.Contains("0A8517", hex);

        // ASL + CLC + ADC #1 (second index: positions[f] * 2 + 1)
        Assert.Contains("0A186901", hex);
    }

    [Fact]
    public void StelemDoesNotConsumeIfElseBranches()
    {
        // Bug: HandleStelemI1's backward IL scan walked past if/else branches,
        // and the _blockCountAtILOffset removal consumed the entire branch code.
        // Pattern: ldelem + if/else + stelem in the same scope.
        // Use a loop to force Roslyn to store the array in a local variable.
        var bytes = GetProgramBytes(
            """
            byte[] types = new byte[8];
            types[0] = 1;
            types[1] = 2;
            for (byte pf = 0; pf < 2; pf++)
            {
                byte ot = types[pf];
                if (ot == 1)
                {
                    oam_clear();
                }
                else
                {
                    pal_col(0, 0x30);
                }
                types[pf] = 0;
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemBranch hex: {hex}");
        // Must contain CMP #$01 (C901) for the if (ot == 1) comparison
        Assert.Contains("C901", hex);
        // Must contain LDA #$30 (A930) for the pal_col color argument in else branch
        Assert.Contains("A930", hex);
        // Must contain LDA #$00 (A900) for stelem value 0
        Assert.Contains("A900", hex);
    }

    [Fact]
    public void LdelemU1_NestedArrayIndex()
    {
        // Regression: arr1[arr2[i]] generated wrong code — the transpiler
        // loaded from arr2 twice instead of using arr2[i] as index into arr1.
        // The workaround (intermediate local) should produce correct code.
        // Direct nested access (arr1[arr2[i]]) requires further transpiler work.
        var bytes = GetProgramBytes(
            """
            byte[] names = new byte[4];
            byte[] lookup = new byte[4];
            names[0] = 10; names[1] = 20; names[2] = 30; names[3] = 40;
            lookup[0] = 3; lookup[1] = 2; lookup[2] = 1; lookup[3] = 0;
            for (byte i = 0; i < 4; i++)
            {
                byte idx = lookup[i];
                byte result = names[idx];
                pal_col(i, result);
            }
            ppu_on_all();
            while (true) ;
            """);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"LdelemU1_NestedArrayIndex hex: {hex}");

        // LDA names,X pattern (BD xx xx) — indexed load from names array
        Assert.Contains("BD", hex);
    }

    [Fact]
    public void StelemI1_ConstantIndex_AddExpression()
    {
        // Bug: tile_row[1] = (byte)(sprite + 1) emitted LDA #$01 (the constant)
        // instead of LDA sprite; CLC; ADC #$01 (the computed value).
        using var transpiler = BuildProgram(
            """
            byte sprite = (byte)pad_poll(0);
            byte[] tile_row = new byte[4];
            tile_row[1] = (byte)(sprite + 1);
            pal_col(0, tile_row[1]);
            ppu_on_all();
            while (true) ;
            """, out var program);

        var mainBlock = program.Blocks.Single(b => b.Label == "main");
        var instructions = mainBlock.InstructionsWithLabels.ToList();

        // Find the CLC instruction that starts the add sequence
        int clcIndex = instructions.FindIndex(il =>
            il.Instruction.Opcode == Opcode.CLC);
        Assert.True(clcIndex >= 0, "Expected CLC for the add operation");

        // The next instruction should be ADC #$01
        var adcInstr = instructions[clcIndex + 1].Instruction;
        Assert.Equal(Opcode.ADC, adcInstr.Opcode);
        Assert.Equal(AddressMode.Immediate, adcInstr.Mode);
        Assert.IsType<ImmediateOperand>(adcInstr.Operand);
        Assert.Equal(1, ((ImmediateOperand)adcInstr.Operand).Value);

        // The instruction before CLC should load the sprite local (LDA abs)
        var ldaInstr = instructions[clcIndex - 1].Instruction;
        Assert.Equal(Opcode.LDA, ldaInstr.Opcode);
        Assert.Equal(AddressMode.Absolute, ldaInstr.Mode);
    }

    [Fact]
    public void StelemI1_AddThenOr()
    {
        // Pattern from game2048: map[idx] = (byte)((val + 1) | 0xF0)
        // The stelem handler must emit both ADC and ORA instructions.
        var bytes = GetProgramBytes(
            """
            byte[] map = new byte[16];
            byte idx = 3;
            byte val = 5;
            map[idx] = (byte)((val + 1) | 0xF0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemI1_AddThenOr hex: {hex}");
        // Must contain CLC (18) + ADC #$01 (6901) for the add
        Assert.Contains("186901", hex);
        // Must contain ORA #$F0 (09F0) for the OR operation
        Assert.Contains("09F0", hex);
    }

    [Fact]
    public void StelemI1_AddThenOr_ConstantIndex()
    {
        // Same pattern but with constant array index: tile[0] = (byte)((v + 1) | 0xF0)
        using var transpiler = BuildProgram(
            """
            byte v = (byte)pad_poll(0);
            byte[] tile = new byte[4];
            tile[0] = (byte)((v + 1) | 0xF0);
            pal_col(0, tile[0]);
            ppu_on_all();
            while (true) ;
            """, out var program);

        var mainBlock = program.Blocks.Single(b => b.Label == "main");
        var instructions = mainBlock.InstructionsWithLabels.ToList();

        // Find CLC for the ADD
        int clcIndex = instructions.FindIndex(il =>
            il.Instruction.Opcode == Opcode.CLC);
        Assert.True(clcIndex >= 0, "Expected CLC for the add operation");
        Assert.True(clcIndex + 2 < instructions.Count, $"Expected at least 2 instructions after CLC at index {clcIndex}, but only {instructions.Count} total");

        // After CLC: ADC #$01
        var adcInstr = instructions[clcIndex + 1].Instruction;
        Assert.Equal(Opcode.ADC, adcInstr.Opcode);
        Assert.Equal(1, ((ImmediateOperand)adcInstr.Operand!).Value);

        // After ADC: ORA #$F0
        var oraInstr = instructions[clcIndex + 2].Instruction;
        Assert.Equal(Opcode.ORA, oraInstr.Opcode);
        Assert.Equal(0xF0, ((ImmediateOperand)oraInstr.Operand!).Value);
    }

    [Fact]
    public void Element_UsedInNTADR_A()
    {
        // Regression: byte[] x = [2]; byte[] y = [2]; vram_adr(NTADR_A(y[0], x[0]))
        // was throwing "Array element access requires the array to be stored in a local variable"
        // because the compiler inlines the array creation without storing to a local.
        var bytes = GetProgramBytes(
            """
            pal_col(0, 0x30);
            byte[] x = [2];
            byte[] y = [2];
            vram_adr(NTADR_A(y[0], x[0]));
            vram_write("B");
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Element_InlineBothArrays()
    {
        // Both arrays are inlined (neither stored to locals) — both should resolve
        // at compile time. NTADR_A(3, 5) = 0x2000 | (3 << 5) | 5 = 0x2065
        var bytes = GetProgramBytes(
            """
            byte[] y = [3];
            byte[] x = [5];
            vram_adr(NTADR_A(y[0], x[0]));
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Element_InlineConstantResolution()
    {
        // Verify inline array element access resolves to the correct constant.
        // Uses pal_bright (single-arg call) to avoid complex multi-arg interactions.
        // The compiler inlines the array when it's used only once.
        var bytes = GetProgramBytes(
            """
            byte[] x = [0x42];
            pal_bright(x[0]);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Element_InlineConstantResolution hex: {hex}");

        // x[0]=0x42 should be resolved at compile time (LDA #$42 = A942)
        Assert.Contains("A942", hex);
    }

    [Fact]
    public void InlineArrayInit_NoPushaDeadCode()
    {
        // Regression test: when the compiler optimizes byte[] x = [2] with a
        // dup + stelem.i1 pattern, HandleStelemI1 must clean up the instructions
        // that WriteLdc emitted for the stelem index/value constants.
        // Without the fix, dead JSR pusha instructions corrupt the cc65 stack.
        var bytes = GetProgramBytes(
            """
            byte[] x = [2];
            pal_col(0, x[0]);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"InlineArrayInit_NoPushaDeadCode hex: {hex}");

        // The value x[0]=2 should resolve to LDA #$02 (A902)
        Assert.Contains("A902", hex);

        // Verify the output contains exactly the expected sequence for pal_col(0, x[0]):
        // LDA #$00 (A900) → JSR pusha (20 xx xx) → LDA #$02 (A902) → JSR pal_col (20 yy yy)
        // The key check: every JSR pusha should be followed (after the next LDA) by a real
        // JSR call, not by another orphaned instruction.
        // Count how many JSR instructions target the pusha subroutine.
        // First, find the pusha address by looking for the pattern:
        // LDA #$00, JSR pusha, LDA #$02, JSR pal_col
        // The first JSR in that sequence targets pusha.
        ushort? pushaAddr = null;
        for (int i = 0; i < bytes.Length - 5; i++)
        {
            // Look for: LDA #imm (A9 xx), JSR addr (20 lo hi), LDA #imm (A9 xx), JSR addr (20 lo hi)
            if (bytes[i] == 0xA9 && bytes[i + 2] == 0x20 && bytes[i + 5] == 0xA9 && bytes[i + 7] == 0x20)
            {
                pushaAddr = (ushort)(bytes[i + 3] | (bytes[i + 4] << 8));
                break;
            }
        }

        if (pushaAddr != null)
        {
            int jsrPushaCount = 0;
            for (int i = 0; i < bytes.Length - 2; i++)
            {
                if (bytes[i] == 0x20) // JSR
                {
                    ushort target = (ushort)(bytes[i + 1] | (bytes[i + 2] << 8));
                    if (target == pushaAddr.Value)
                        jsrPushaCount++;
                }
            }
            // There should be exactly 1 pusha call (for pal_col's 2-arg call), not 2+
            // (which would indicate dead code from stelem init)
            Assert.True(jsrPushaCount <= 1,
                $"Found {jsrPushaCount} JSR pusha instructions — expected at most 1. Extra calls indicate dead code from stelem init.");
        }
    }

    [Fact]
    public void CollectionExpressionArrayIndexing()
    {
        // Collection expressions like byte[] x = [2, 3, 4] compile to
        // newarr → dup → ldtoken → call InitializeArray → stloc
        // The transpiler must correctly handle ldelem.u1 on these arrays.
        var bytes = GetProgramBytes(
            """
            byte[] x = [2, 3, 4, 5, 6, 7, 8, 9, 10];
            byte[] y = [2, 2, 2, 2, 2, 2, 2, 2, 2];
            vram_adr(NTADR_A(x[0], y[0]));
            vram_write("B");
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"CollectionExprArrayIdx hex: {hex}");

        // Look for the specific NTADR_A argument-load pattern:
        //   LDX #$00         (A2 00)  ; x index
        //   LDA xarr,X       (BD lo hi)
        //   STA $17          (85 17)  ; TEMP = x
        //   LDX #$00         (A2 00)  ; y index
        //   LDA yarr,X       (BD lo hi)
        //   JSR nametable_a  (20 lo hi)
        bool found = false;
        for (int i = 0; i <= bytes.Length - 14; i++)
        {
            if (bytes[i] == 0xA2 && bytes[i + 1] == 0x00      // LDX #$00
                && bytes[i + 2] == 0xBD                       // LDA abs,X (x[0])
                && bytes[i + 5] == 0x85 && bytes[i + 6] == 0x17 // STA $17
                && bytes[i + 7] == 0xA2 && bytes[i + 8] == 0x00 // LDX #$00
                && bytes[i + 9] == 0xBD                       // LDA abs,X (y[0])
                && bytes[i + 12] == 0x20)                     // JSR (nametable_a)
            {
                found = true;
                break;
            }
        }
        Assert.True(found,
            "Expected NTADR_A pattern: LDX #0; LDA xarr,X; STA TEMP; LDX #0; LDA yarr,X; JSR nametable_a");
    }

    [Fact]
    public void CollectionExpressionArrayIndexingNonZero()
    {
        // Same as above but with non-zero indices (x[1], y[1])
        var bytes = GetProgramBytes(
            """
            byte[] x = [2, 3, 4, 5, 6, 7, 8, 9, 10];
            byte[] y = [2, 2, 2, 2, 2, 2, 2, 2, 2];
            vram_adr(NTADR_A(x[1], y[1]));
            vram_write("L");
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"CollectionExprArrayIdxNonZero hex: {hex}");

        // Same pattern as above, but with LDX #$01 instead of LDX #$00.
        bool found = false;
        for (int i = 0; i <= bytes.Length - 14; i++)
        {
            if (bytes[i] == 0xA2 && bytes[i + 1] == 0x01      // LDX #$01
                && bytes[i + 2] == 0xBD                       // LDA abs,X (x[1])
                && bytes[i + 5] == 0x85 && bytes[i + 6] == 0x17 // STA $17
                && bytes[i + 7] == 0xA2 && bytes[i + 8] == 0x01 // LDX #$01
                && bytes[i + 9] == 0xBD                       // LDA abs,X (y[1])
                && bytes[i + 12] == 0x20)                     // JSR (nametable_a)
            {
                found = true;
                break;
            }
        }
        Assert.True(found,
            "Expected NTADR_A pattern: LDX #1; LDA xarr,X; STA TEMP; LDX #1; LDA yarr,X; JSR nametable_a");
    }

    [Fact]
    public void CollectionExpressionArrayIndexingConstantY()
    {
        // Regression test: NTADR_A(x[i], <constant>) — runtime x from an array element
        // plus a constant y. WriteLdc skips emitting LDA #y because _runtimeValueInA
        // is true after the AbsoluteX array load, so the block ends with the x load
        // (LDA arr,X) and the constant y lives only on the IL stack.
        // The NTADR handler must route this through the xFromFlag path, not misclassify
        // the trailing AbsoluteX load as a runtime y.
        var bytes = GetProgramBytes(
            """
            byte[] x = [2, 3, 4, 5, 6, 7, 8, 9, 10];
            byte[] y = [2, 2, 2, 2, 2, 2, 2, 2, 2];
            vram_adr(NTADR_A(x[0], 2));
            pal_col(0, y[0]);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"CollectionExprArrayIdxConstY hex: {hex}");

        // Look for the xFromFlag pattern:
        //   LDX #$00         (A2 00)  ; x index
        //   LDA xarr,X       (BD lo hi)
        //   STA $17          (85 17)  ; TEMP = x
        //   LDA #$02         (A9 02)  ; constant y
        //   JSR nametable_a  (20 lo hi)
        bool found = false;
        for (int i = 0; i <= bytes.Length - 12; i++)
        {
            if (bytes[i] == 0xA2 && bytes[i + 1] == 0x00      // LDX #$00
                && bytes[i + 2] == 0xBD                       // LDA abs,X (x[0])
                && bytes[i + 5] == 0x85 && bytes[i + 6] == 0x17 // STA $17
                && bytes[i + 7] == 0xA9 && bytes[i + 8] == 0x02 // LDA #$02
                && bytes[i + 9] == 0x20)                      // JSR (nametable_a)
            {
                found = true;
                break;
            }
        }
        Assert.True(found,
            "Expected NTADR_A pattern: LDX #0; LDA xarr,X; STA TEMP; LDA #2; JSR nametable_a");
    }

    [Fact]
    public void Array2D_ConstantIndex()
    {
        // Rectangular byte[2,3] literal indexed with two constants.
        // Expected: a[1,2] resolves to ROM byte at offset 1*3 + 2 = 5, value 6.
        var bytes = GetProgramBytes(
            """
            byte[,] a = new byte[2, 3]
            {
                { 1, 2, 3 },
                { 4, 5, 6 }
            };
            byte v = a[1, 2];
            pal_col(0, v);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Array2D_ConstantIndex hex: {hex}");

        // Constant index 5 → LDX #$05 (A2 05); LDA bytearray_0,X (BD lo hi)
        Assert.Contains("A205BD", hex);
    }

    [Fact]
    public void Array2D_PowerOfTwoStride_RuntimeIndex()
    {
        // 4-wide rectangular array — stride of 4 is a power of two, so the
        // index should be computed with ASL A, ASL A (no multiplication).
        var bytes = GetProgramBytes(
            """
            byte[,] def_L = new byte[4, 4]
            {
                { 0, 1, 2, 6 },
                { 1, 5, 9, 8 },
                { 0, 4, 5, 6 },
                { 1, 0, 4, 8 },
            };
            byte rotation = 2;
            byte slot     = 1;
            byte offset   = def_L[rotation, slot];
            pal_col(0, offset);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Array2D_PowerOfTwoStride_RuntimeIndex hex: {hex}");

        // Expected sequence somewhere in the body:
        //   LDA rotation_addr   (AD lo hi)
        //   ASL A               (0A)
        //   ASL A               (0A)
        //   CLC                 (18)
        //   ADC slot_addr       (6D lo hi)
        //   TAX                 (AA)
        //   LDA bytearray_0,X   (BD lo hi)
        // Power-of-two stride must use ASL not a multiplication subroutine.
        Assert.Contains("0A0A18", hex); // ASL; ASL; CLC
        Assert.Contains("AABD", hex);   // TAX; LDA abs,X
        // Make sure no multiplication helper (umul/smul) was emitted.
        Assert.DoesNotContain("umul", hex.ToLower());
        Assert.DoesNotContain("smul", hex.ToLower());
    }

    [Fact]
    public void Array2D_NonPowerOfTwoStride_RuntimeIndex()
    {
        // Stride of 3 is NOT a power of two. The fallback shift-and-add path
        // (STA TEMP; ASL; CLC; ADC TEMP) should be emitted instead of ASLs.
        var bytes = GetProgramBytes(
            """
            byte[,] tbl = new byte[2, 3]
            {
                { 10, 20, 30 },
                { 40, 50, 60 }
            };
            byte r = 1;
            byte c = 2;
            byte v = tbl[r, c];
            pal_col(0, v);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Array2D_NonPowerOfTwoStride_RuntimeIndex hex: {hex}");

        // For stride 3 (binary 11), the multiplier emits:
        //   STA $17 (TEMP)     ; 85 17
        //   ASL A              ; 0A
        //   CLC                ; 18
        //   ADC $17 (TEMP)     ; 65 17
        Assert.Contains("85170A1865", hex);
        // The result is then combined with the column and used to index the array.
        Assert.Contains("AABD", hex); // TAX; LDA abs,X
    }
}

