using System.Collections.Immutable;
using System.Reflection.Metadata;
using dotnes.ObjectModel;
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

    IL2NESWriter GetWriter()
    {
        _stream.SetLength(0);

        var writer = new IL2NESWriter(_stream, leaveOpen: true, logger: _logger);
        writer.StartBlockBuffering();
        return writer;
    }

    [Fact]
    public Task Write_Main_hello()
    {
        using var writer = GetWriter();
        // Set up Labels needed for IL->NES translation
        writer.Labels["pusha"] = 0x85A2;
        writer.Labels["pushax"] = 0x85B8;
        writer.Labels["pal_col"] = 0x823E;
        writer.Labels["vram_adr"] = 0x83D4;
        writer.Labels["vram_write"] = 0x834F;
        writer.Labels["ppu_on_all"] = 0x8289;
        // Set up instruction label for Br_s target (while(true) infinite loop)
        writer.Labels["instruction_00"] = 0x0000;
        // String label placeholder (resolved by transpiler in real builds)
        writer.Labels["string_0"] = 0x8657;

        // pal_col(0, 0x02);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_0));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2));
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col));

        // pal_col(1, 0x14);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_1));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x14);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col));

        // pal_col(2, 0x20);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x20);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col));

        // pal_col(3, 0x30);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_3));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x30);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pal_col));

        // vram_adr(NTADR_A(2, 2));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_2));
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NTADR_A));
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(vram_adr));

        // vram_write("HELLO, .NET!");
        var text = "HELLO, .NET!";
        writer.Write(new ILInstruction(ILOpCode.Ldstr), text);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(vram_write));

        // ppu_on_all();
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(ppu_on_all));

        // while (true) ;
        writer.Write(new ILInstruction(ILOpCode.Br_s), 254);

        writer.FlushMainBlock();
        return Verify(_stream.ToArray());
    }

    [Fact]
    public Task Write_Main_attributetable()
    {
        using var writer = GetWriter();
        // Set up Labels needed for IL->NES translation
        writer.Labels["pusha"] = 0x85A2;
        writer.Labels["pushax"] = 0x85B8;
        writer.Labels["pal_bg"] = 0x822B;
        writer.Labels["vram_adr"] = 0x83D4;
        writer.Labels["vram_fill"] = 0x83DF;
        writer.Labels["vram_write"] = 0x834F;
        writer.Labels["ppu_on_all"] = 0x8289;
        writer.Labels["pushax"] = 0x8D85;
        // Set up byte array labels
        writer.Labels["bytearray_0"] = 0x85DC; // ATTRIBUTE_TABLE (64 bytes)
        writer.Labels["bytearray_1"] = 0x861C; // PALETTE (16 bytes)
        // Set up instruction label for Br_s target (while(true) infinite loop)
        writer.Labels["instruction_00"] = 0x0000;
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_s), 64);
        writer.Write(new ILInstruction(ILOpCode.Newarr), 16777235);
        writer.Write(new ILInstruction(ILOpCode.Dup));
        writer.Write(new ILInstruction(ILOpCode.Ldtoken), [
          0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // rows 0-3
          0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, // rows 4-7
          0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, // rows 8-11
          0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, // rows 12-15
          0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, // rows 16-19
          0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, // rows 20-23
          0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, // rows 24-27
          0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f  // rows 28-29
        ]);
        writer.Write(new ILInstruction(ILOpCode.Stloc_0));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_s), 16);
        writer.Write(new ILInstruction(ILOpCode.Newarr), 16777235);
        writer.Write(new ILInstruction(ILOpCode.Dup));
        writer.Write(new ILInstruction(ILOpCode.Ldtoken), [
          0x03,			// screen color

          0x11,0x30,0x27,0x0,	// background palette 0
          0x1c,0x20,0x2c,0x0,	// background palette 1
          0x00,0x10,0x20,0x0,	// background palette 2
          0x06,0x16,0x26        // background palette 3
        ]);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NESLib.pal_bg));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x2000);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NESLib.vram_adr));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_s), 22);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 960);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NESLib.vram_fill));
        writer.Write(new ILInstruction(ILOpCode.Ldloc_0));
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NESLib.vram_write));
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(NESLib.ppu_on_all));
        writer.Write(new ILInstruction(ILOpCode.Br_s), 254);

        writer.FlushMainBlock();
        return Verify(_stream.ToArray());
    }

    /// <summary>
    /// Test that pad_poll pushes a return value onto the IL evaluation stack.
    /// This verifies the fix for IL stack underflow when pad_poll result is stored to a local.
    /// Before the fix, pad_poll would not push the return value, causing a stack underflow
    /// when the result was stored to a local variable.
    /// </summary>
    [Fact]
    public void PadPoll_PushesReturnValueOnStack()
    {
        using var writer = GetWriter();
        writer.Labels["pad_poll"] = 0x85CB;

        // Simulate: PAD pad = pad_poll(0);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_0)); // Push argument
        Assert.Single(writer.Stack); // Argument on stack
        
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(pad_poll)); // Call pad_poll
        
        // After pad_poll, exactly 1 item should remain (the return value)
        // The argument should have been consumed, and a return value pushed
        Assert.Single(writer.Stack);
        Assert.Equal(0, writer.Stack.Peek()); // Placeholder value
    }

    /// <summary>
    /// Test that the ternary operator optimization for compile-time arithmetic works correctly.
    /// This verifies the refactoring of if-else to ternary operator in HandleAddSub.
    /// </summary>
    [Fact]
    public void HandleAddSub_TernaryOperator_ComputesCorrectly()
    {
        using var writer = GetWriter();
        
        // Test addition: 5 + 3 = 8
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 5);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 3);
        writer.Write(new ILInstruction(ILOpCode.Add));
        Assert.Equal(8, writer.Stack.Peek());
        writer.Stack.Pop();
        
        // Test subtraction: 10 - 4 = 6  
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 10);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 4);
        writer.Write(new ILInstruction(ILOpCode.Sub));
        Assert.Equal(6, writer.Stack.Peek());
    }
    
    /// <summary>
    /// Test that the _pendingLabels field being readonly doesn't prevent normal operation.
    /// This verifies the immutability improvement made to Block.cs.
    /// The field was changed from 'private List' to 'private readonly List' to ensure
    /// the reference cannot be reassigned, improving code safety.
    /// </summary>
    [Fact]
    public void Block_ReadonlyPendingLabels_AllowsNormalOperation()
    {
        var block = new ObjectModel.Block("test");
        
        // Create a simple instruction using the factory method
        var nop = ObjectModel.Asm.NOP();
        
        // Emit an instruction with a label
        block.Emit(nop, "test_label");
        
        // Verify the instruction was emitted
        var instructionsWithLabels = block.InstructionsWithLabels.ToList();
        Assert.Single(instructionsWithLabels);
        Assert.NotNull(instructionsWithLabels[0].Instruction);
        Assert.Equal("test_label", instructionsWithLabels[0].Label);
    }
    
    /// <summary>
    /// Test that combined if statements in HandleAddSub work correctly.
    /// This verifies that the nested conditions were properly merged into a compound condition
    /// without changing behavior. The optimization improves readability while maintaining
    /// the same logic for detecting x++ and x-- patterns.
    /// </summary>
    [Fact]
    public void CombinedIfStatements_MaintainCorrectBehavior()
    {
        using var writer = GetWriter();
        
        // Test that simple addition still works correctly
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 42);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_1));
        writer.Write(new ILInstruction(ILOpCode.Add));
        
        // The result should be on the stack
        Assert.Equal(43, writer.Stack.Peek());
        
        // Test subtraction
        writer.Stack.Clear();
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 50);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_1));
        writer.Write(new ILInstruction(ILOpCode.Sub));
        
        Assert.Equal(49, writer.Stack.Peek());
    }

    /// <summary>
    /// Test that poke(ushort, byte) emits LDA #value, STA abs addr and consumes both args.
    /// </summary>
    [Fact]
    public void Poke_EmitsLdaSta_ConsumesArgs()
    {
        using var writer = GetWriter();
        writer.Labels["pusha"] = 0x85A2;

        // poke(0x4015, 0x0F)
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x4015);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x0F);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(poke));

        // Both arguments should be consumed, stack empty
        Assert.Empty(writer.Stack);
    }

    /// <summary>
    /// Test that peek(ushort) emits LDA abs addr and pushes a return value placeholder.
    /// </summary>
    [Fact]
    public void Peek_EmitsLdaAbsolute_PushesReturnValue()
    {
        using var writer = GetWriter();

        // peek(0x2002)
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x2002);
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(peek));

        // Return value should be on stack
        Assert.Single(writer.Stack);
    }

    [Theory]
    [InlineData(ILOpCode.Conv_i8, "long", "byte", "sbyte")]
    [InlineData(ILOpCode.Conv_r4, "float", "double", "integer")]
    [InlineData(ILOpCode.Conv_r8, "float", "double", "integer")]
    [InlineData(ILOpCode.Box, "boxing", "object", "generics")]
    [InlineData(ILOpCode.Newobj, "new", "byte[]", "ushort[]")]
    [InlineData(ILOpCode.Callvirt, "virtual method", "static methods", "NESLib")]
    [InlineData(ILOpCode.Throw, "throw", "try/catch", "not supported")]
    [InlineData(ILOpCode.Ldlen, ".Length", "array", "variable")]
    public void GetUnsupportedOpcodeMessage_KnownOpcodes_ContainsHelpfulGuidance(ILOpCode opCode, string expectedTerm1, string expectedTerm2, string expectedTerm3)
    {
        var message = IL2NESWriter.GetUnsupportedOpcodeMessage(opCode);
        Assert.Contains(opCode.ToString(), message);
        Assert.Contains(expectedTerm1, message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedTerm2, message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedTerm3, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not yet supported", message);
    }

    [Fact]
    public void GetUnsupportedOpcodeMessage_UnknownOpcode_FallsBackToGenericMessage()
    {
        // Use an opcode unlikely to get a specific message
        var message = IL2NESWriter.GetUnsupportedOpcodeMessage(ILOpCode.Arglist);
        Assert.Contains("not yet supported", message);
        Assert.Contains("Arglist", message);
    }

    /// <summary>
    /// Test that XOR with two compile-time constants produces correct stack value.
    /// </summary>
    [Fact]
    public void Xor_CompileTime_ProducesCorrectResult()
    {
        using var writer = GetWriter();

        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0xAA);
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x55);
        writer.Write(new ILInstruction(ILOpCode.Xor));

        Assert.Equal(0xAA ^ 0x55, writer.Stack.Peek());
    }

    /// <summary>
    /// Test that XOR with a runtime value and constant emits EOR #immediate.
    /// Pattern: rand8() ^ 0x0F
    /// </summary>
    [Fact]
    public void Xor_RuntimeAndConstant_EmitsEorImmediate()
    {
        using var writer = GetWriter();
        writer.Labels["rand8"] = 0x8400;

        // rand8() ^ 0x0F
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(rand8));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x0F);
        writer.Write(new ILInstruction(ILOpCode.Xor));

        // Should have emitted EOR #$0F
        var block = writer.CurrentBlock!;
        bool foundEor = false;
        for (int i = 0; i < block.Count; i++)
        {
            if (block[i].Opcode == Opcode.EOR && block[i].Mode == AddressMode.Immediate)
            {
                foundEor = true;
                break;
            }
        }
        Assert.True(foundEor, "Expected EOR Immediate instruction for runtime XOR with constant");
    }

    /// <summary>
    /// Test that AND with two runtime values emits AND ZeroPage,TEMP.
    /// Pattern: rand8() & rand8() (via stloc/ldloc)
    /// </summary>
    [Fact]
    public void And_RuntimeAndRuntime_EmitsAndZeroPage()
    {
        using var writer = GetWriter();
        writer.Labels["rand8"] = 0x8400;

        // Simulate: byte a = rand8(); byte result = rand8() & a;
        // IL: call rand8, stloc.0, call rand8, ldloc.0, and
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(rand8));
        writer.Write(new ILInstruction(ILOpCode.Stloc_0));
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(rand8));
        writer.Write(new ILInstruction(ILOpCode.Ldloc_0));
        writer.Write(new ILInstruction(ILOpCode.And));

        var block = writer.CurrentBlock!;
        bool foundAndZp = false;
        for (int i = 0; i < block.Count; i++)
        {
            if (block[i].Opcode == Opcode.AND && block[i].Mode == AddressMode.ZeroPage)
            {
                foundAndZp = true;
                break;
            }
        }
        Assert.True(foundAndZp, "Expected AND ZeroPage instruction for runtime-runtime AND");
    }

    /// <summary>
    /// Test that OR with two runtime values emits ORA ZeroPage,TEMP.
    /// Pattern: rand8() | rand8() (via stloc/ldloc)
    /// </summary>
    [Fact]
    public void Or_RuntimeAndRuntime_EmitsOraZeroPage()
    {
        using var writer = GetWriter();
        writer.Labels["rand8"] = 0x8400;

        // Simulate: byte a = rand8(); byte result = rand8() | a;
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(rand8));
        writer.Write(new ILInstruction(ILOpCode.Stloc_0));
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(rand8));
        writer.Write(new ILInstruction(ILOpCode.Ldloc_0));
        writer.Write(new ILInstruction(ILOpCode.Or));

        var block = writer.CurrentBlock!;
        bool foundOraZp = false;
        for (int i = 0; i < block.Count; i++)
        {
            if (block[i].Opcode == Opcode.ORA && block[i].Mode == AddressMode.ZeroPage)
            {
                foundOraZp = true;
                break;
            }
        }
        Assert.True(foundOraZp, "Expected ORA ZeroPage instruction for runtime-runtime OR");
    }

    /// <summary>
    /// Test that XOR with two runtime values emits EOR ZeroPage,TEMP.
    /// Pattern: rand8() ^ rand8() (via stloc/ldloc)
    /// </summary>
    [Fact]
    public void Xor_RuntimeAndRuntime_EmitsEorZeroPage()
    {
        using var writer = GetWriter();
        writer.Labels["rand8"] = 0x8400;

        // Simulate: byte a = rand8(); byte result = rand8() ^ a;
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(rand8));
        writer.Write(new ILInstruction(ILOpCode.Stloc_0));
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(rand8));
        writer.Write(new ILInstruction(ILOpCode.Ldloc_0));
        writer.Write(new ILInstruction(ILOpCode.Xor));

        var block = writer.CurrentBlock!;
        bool foundEorZp = false;
        for (int i = 0; i < block.Count; i++)
        {
            if (block[i].Opcode == Opcode.EOR && block[i].Mode == AddressMode.ZeroPage)
            {
                foundEorZp = true;
                break;
            }
        }
        Assert.True(foundEorZp, "Expected EOR ZeroPage instruction for runtime-runtime XOR");
    }

    /// <summary>
    /// Test that MUL with a non-power-of-2 constant emits shift-and-add multiply loop.
    /// Pattern: rand8() * 3
    /// </summary>
    [Fact]
    public void Mul_RuntimeTimesNonPow2_EmitsMultiplyLoop()
    {
        using var writer = GetWriter();
        writer.Labels["rand8"] = 0x8400;

        // rand8() * 3
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(rand8));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_3));
        writer.Write(new ILInstruction(ILOpCode.Mul));

        // Should emit a shift-and-add multiply loop with BNE back branch
        var block = writer.CurrentBlock!;
        bool foundLsr = false;
        bool foundBne = false;
        for (int i = 0; i < block.Count; i++)
        {
            if (block[i].Opcode == Opcode.LSR && block[i].Mode == AddressMode.ZeroPage)
                foundLsr = true;
            if (block[i].Opcode == Opcode.BNE)
                foundBne = true;
        }
        Assert.True(foundLsr, "Expected LSR ZeroPage for multiply loop");
        Assert.True(foundBne, "Expected BNE for multiply loop back-branch");
    }

    /// <summary>
    /// Test that MUL with two runtime values emits shift-and-add multiply.
    /// Pattern: rand8() * rand8() (via stloc/ldloc)
    /// </summary>
    [Fact]
    public void Mul_RuntimeTimesRuntime_EmitsMultiplyLoop()
    {
        using var writer = GetWriter();
        writer.Labels["rand8"] = 0x8400;

        // Simulate: byte a = rand8(); byte result = rand8() * a;
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(rand8));
        writer.Write(new ILInstruction(ILOpCode.Stloc_0));
        writer.Write(new ILInstruction(ILOpCode.Call), nameof(rand8));
        writer.Write(new ILInstruction(ILOpCode.Ldloc_0));
        writer.Write(new ILInstruction(ILOpCode.Mul));

        var block = writer.CurrentBlock!;
        bool foundLsr = false;
        bool foundBne = false;
        for (int i = 0; i < block.Count; i++)
        {
            if (block[i].Opcode == Opcode.LSR && block[i].Mode == AddressMode.ZeroPage)
                foundLsr = true;
            if (block[i].Opcode == Opcode.BNE)
                foundBne = true;
        }
        Assert.True(foundLsr, "Expected LSR ZeroPage for runtime-runtime multiply loop");
        Assert.True(foundBne, "Expected BNE for runtime-runtime multiply loop back-branch");
    }

    /// <summary>
    /// Test that subtraction of two runtime ushort values emits proper 16-bit subtraction
    /// with borrow propagation. Pattern: ushort a = ...; ushort b = ...; ushort c = (ushort)(a - b);
    /// </summary>
    [Fact]
    public void Sub_UshortMinusUshort_Emits16BitSubtraction()
    {
        _stream.SetLength(0);
        using var writer = new IL2NESWriter(_stream, leaveOpen: true, logger: _logger)
        {
            WordLocals = new HashSet<int> { 0, 1 }
        };
        writer.StartBlockBuffering();

        // ushort a = 0x0200;
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x0200);
        writer.Write(new ILInstruction(ILOpCode.Stloc_0));

        // ushort b = 0x0100;
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x0100);
        writer.Write(new ILInstruction(ILOpCode.Stloc_1));

        // (ushort)(a - b)
        writer.Write(new ILInstruction(ILOpCode.Ldloc_0));
        writer.Write(new ILInstruction(ILOpCode.Ldloc_1));
        writer.Write(new ILInstruction(ILOpCode.Sub));

        // Should have:
        // 1. STA TEMP + STX TEMP2 (save first ushort before loading second)
        // 2. SEC + SBC ZeroPage (16-bit subtraction with borrow)
        var block = writer.CurrentBlock!;
        bool foundSec = false;
        bool foundSbcZp = false;
        for (int i = 0; i < block.Count; i++)
        {
            if (block[i].Opcode == Opcode.SEC)
                foundSec = true;
            if (block[i].Opcode == Opcode.SBC && block[i].Mode == AddressMode.ZeroPage)
                foundSbcZp = true;
        }
        Assert.True(foundSec, "Expected SEC instruction for 16-bit subtraction");
        Assert.True(foundSbcZp, "Expected SBC ZeroPage instruction for 16-bit subtraction with borrow");
    }

    /// <summary>
    /// Test that addition of two runtime ushort values emits proper 16-bit addition
    /// with carry propagation. Pattern: ushort a = ...; ushort b = ...; ushort c = (ushort)(a + b);
    /// </summary>
    [Fact]
    public void Add_UshortPlusUshort_Emits16BitAddition()
    {
        _stream.SetLength(0);
        using var writer = new IL2NESWriter(_stream, leaveOpen: true, logger: _logger)
        {
            WordLocals = new HashSet<int> { 0, 1 }
        };
        writer.StartBlockBuffering();

        // ushort a = 0x0200;
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x0200);
        writer.Write(new ILInstruction(ILOpCode.Stloc_0));

        // ushort b = 0x0100;
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4), 0x0100);
        writer.Write(new ILInstruction(ILOpCode.Stloc_1));

        // (ushort)(a + b)
        writer.Write(new ILInstruction(ILOpCode.Ldloc_0));
        writer.Write(new ILInstruction(ILOpCode.Ldloc_1));
        writer.Write(new ILInstruction(ILOpCode.Add));

        // Should have CLC + ADC ZeroPage (16-bit addition with carry)
        var block = writer.CurrentBlock!;
        bool foundClc = false;
        bool foundAdcZp = false;
        for (int i = 0; i < block.Count; i++)
        {
            if (block[i].Opcode == Opcode.CLC)
                foundClc = true;
            if (block[i].Opcode == Opcode.ADC && block[i].Mode == AddressMode.ZeroPage)
                foundAdcZp = true;
        }
        Assert.True(foundClc, "Expected CLC instruction for 16-bit addition");
        Assert.True(foundAdcZp, "Expected ADC ZeroPage instruction for 16-bit addition with carry");
    }
}
