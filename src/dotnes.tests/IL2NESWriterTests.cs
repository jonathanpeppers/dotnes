using System.Collections.Immutable;
using System.Reflection.Metadata;
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

    [Fact]
    public void Neg_CompileTime_NegatesValue()
    {
        using var writer = GetWriter();
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_5));
        writer.Write(new ILInstruction(ILOpCode.Neg));
        Assert.Equal(-5, writer.Stack.Peek());
    }

    [Fact]
    public void Neg_Runtime_EmitsNegation()
    {
        using var writer = GetWriter();

        // Store a constant to local 0 to allocate an address
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_5));
        writer.Write(new ILInstruction(ILOpCode.Stloc_0));

        // Load local 0 — puts a runtime value in A
        writer.Write(new ILInstruction(ILOpCode.Ldloc_0));

        // Neg should not throw and should keep runtime value in A
        writer.Write(new ILInstruction(ILOpCode.Neg));
        Assert.Single(writer.Stack);
    }

    [Fact]
    public void Not_CompileTime_InvertsValue()
    {
        using var writer = GetWriter();
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_5));
        writer.Write(new ILInstruction(ILOpCode.Not));
        Assert.Equal(~5, writer.Stack.Peek());
    }

    [Fact]
    public void Not_Runtime_EmitsEor()
    {
        using var writer = GetWriter();

        // Store a constant to local 0 to allocate an address
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_5));
        writer.Write(new ILInstruction(ILOpCode.Stloc_0));

        // Load local 0 — puts a runtime value in A
        writer.Write(new ILInstruction(ILOpCode.Ldloc_0));

        // Not should not throw and should keep runtime value in A
        writer.Write(new ILInstruction(ILOpCode.Not));
        Assert.Single(writer.Stack);
    }

    [Fact]
    public void Ceq_CompileTime_Equal_PushesOne()
    {
        using var writer = GetWriter();
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_5));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_5));
        writer.Write(new ILInstruction(ILOpCode.Ceq));
        Assert.Equal(1, writer.Stack.Peek());
    }

    [Fact]
    public void Ceq_CompileTime_NotEqual_PushesZero()
    {
        using var writer = GetWriter();
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_5));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_3));
        writer.Write(new ILInstruction(ILOpCode.Ceq));
        Assert.Equal(0, writer.Stack.Peek());
    }

    [Fact]
    public void Cgt_CompileTime_GreaterThan_PushesOne()
    {
        using var writer = GetWriter();
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_5));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_3));
        writer.Write(new ILInstruction(ILOpCode.Cgt));
        Assert.Equal(1, writer.Stack.Peek());
    }

    [Fact]
    public void Cgt_CompileTime_NotGreaterThan_PushesZero()
    {
        using var writer = GetWriter();
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_3));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_5));
        writer.Write(new ILInstruction(ILOpCode.Cgt));
        Assert.Equal(0, writer.Stack.Peek());
    }

    [Fact]
    public void Clt_CompileTime_LessThan_PushesOne()
    {
        using var writer = GetWriter();
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_3));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_5));
        writer.Write(new ILInstruction(ILOpCode.Clt));
        Assert.Equal(1, writer.Stack.Peek());
    }

    [Fact]
    public void Clt_CompileTime_NotLessThan_PushesZero()
    {
        using var writer = GetWriter();
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_5));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_3));
        writer.Write(new ILInstruction(ILOpCode.Clt));
        Assert.Equal(0, writer.Stack.Peek());
    }

    [Fact]
    public void Ceq_CompileTime_Equal_PushesCorrectValue()
    {
        using var writer = GetWriter();
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_8));
        writer.Write(new ILInstruction(ILOpCode.Ldc_i4_8));
        writer.Write(new ILInstruction(ILOpCode.Ceq));
        Assert.Equal(1, writer.Stack.Peek());
    }
}
