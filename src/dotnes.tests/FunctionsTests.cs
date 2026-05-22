using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class FunctionsTests : RoslynTests
{
    public FunctionsTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void FunctionReturnConstant()
    {
        // Simplest case: function returns a constant byte
        var bytes = GetProgramBytes(
            """
            pal_col(0, get_value());
            ppu_on_all();
            while (true) ;

            static byte get_value() => 5;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Main block should contain JSR to get_value and JSR to pal_col
        // LDA #$00 for pal_col first arg
        Assert.Contains("A900", hex);
        // Multiple JSR calls (20 = JSR opcode)
        Assert.True(hex.Split("20").Length >= 3, "Expected at least 2 JSR calls in main block");
    }

    [Fact]
    public void FunctionReturnParameter()
    {
        // Function takes a byte param and returns a computed value
        var bytes = GetProgramBytes(
            """
            pal_col(0, add_one(3));
            ppu_on_all();
            while (true) ;

            static byte add_one(byte x) => (byte)(x + 1);
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // LDA #$03 for the argument to add_one
        Assert.Contains("A903", hex);
    }

    [Fact]
    public void FunctionReturnUsedInExpression()
    {
        // Return value stored to local, then used as argument
        var bytes = GetProgramBytes(
            """
            byte r = get_value();
            pal_col(0, r);
            ppu_on_all();
            while (true) ;

            static byte get_value() => 0x14;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // STA to local variable (8D = STA absolute, for storing return value)
        Assert.Contains("8D", hex);
    }

    [Fact]
    public void FunctionReturnWithParamUsed()
    {
        // Return value from parameterized function used in pal_col
        // Verifies return value survives incsp1 parameter cleanup
        var bytes = GetProgramBytes(
            """
            pal_col(0, double_it(3));
            ppu_on_all();
            while (true) ;

            static byte double_it(byte x) => (byte)(x + x);
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // LDA #$00 for pal_col first arg
        Assert.Contains("A900", hex);
        // LDA #$03 for double_it arg
        Assert.Contains("A903", hex);
        // 4 JSR calls: pusha, double_it, pal_col, ppu_on_all
        int jsrCount = hex.Split("20").Length - 1;
        Assert.True(jsrCount >= 4, $"Expected at least 4 JSR calls, got {jsrCount}");
    }

    [Fact]
    public void ExternMethodCall()
    {
        // Create a temporary .s file with a labeled subroutine
        var tempDir = Path.Combine(Path.GetTempPath(), "dotnes_test_extern");
        Directory.CreateDirectory(tempDir);
        var sFilePath = Path.Combine(tempDir, "test_extern.s");
        try
        {
            // Write a minimal .s file: _my_extern_func label with an RTS (0x60)
            File.WriteAllText(sFilePath,
                """
                ; Test extern subroutine
                _my_extern_func:
                .byte $A9,$42,$60
                """);

            using var reader = new AssemblyReader(sFilePath);
            var assemblyFiles = new List<AssemblyReader> { reader };
            var bytes = GetProgramBytes(
                """
                static extern void my_extern_func();
                my_extern_func();
                ppu_on_all();
                while (true) ;
                """,
                additionalAssemblyFiles: assemblyFiles,
                allowUnsafe: true);
            Assert.NotNull(bytes);
            Assert.NotEmpty(bytes);

            var hex = Convert.ToHexString(bytes);
            // Should contain JSR (0x20) to _my_extern_func label
            Assert.Contains("20", hex);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExternMethodWithArgs()
    {
        // Extern method with one argument: verifies arg passing + JSR
        var tempDir = Path.Combine(Path.GetTempPath(), "dotnes_test_extern2");
        Directory.CreateDirectory(tempDir);
        var sFilePath = Path.Combine(tempDir, "test_extern2.s");
        try
        {
            File.WriteAllText(sFilePath,
                """
                ; Test extern subroutine with arg
                _set_value:
                .byte $85,$17,$60
                """);

            using var reader = new AssemblyReader(sFilePath);
            var assemblyFiles = new List<AssemblyReader> { reader };
            var bytes = GetProgramBytes(
                """
                static extern void set_value(byte val);
                set_value(42);
                ppu_on_all();
                while (true) ;
                """,
                additionalAssemblyFiles: assemblyFiles,
                allowUnsafe: true);
            Assert.NotNull(bytes);
            Assert.NotEmpty(bytes);

            var hex = Convert.ToHexString(bytes);
            Assert.Contains("A92A", hex);  // LDA #$2A (42) — arg loaded before JSR
            Assert.Contains("20", hex);    // JSR to _set_value
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ThreeParameterFunction()
    {
        // Function with 3 byte parameters
        var bytes = GetProgramBytes(
            """
            byte r = add3(1, 2, 3);
            pal_col(0, r);
            ppu_on_all();
            while (true) ;

            static byte add3(byte a, byte b, byte c) => (byte)(a + b + c);
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // All three constants should appear
        Assert.Contains("A901", hex); // LDA #$01
        Assert.Contains("A902", hex); // LDA #$02
        Assert.Contains("A903", hex); // LDA #$03
    }

    [Fact]
    public void NestedFunctionCalls()
    {
        // f(g(x)) — nested call
        var bytes = GetProgramBytes(
            """
            pal_col(0, outer(3));
            ppu_on_all();
            while (true) ;

            static byte outer(byte x) => inner(x);
            static byte inner(byte x) => (byte)(x + 10);
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A903", hex); // LDA #$03 (initial arg)
    }

    [Fact]
    public void UserFuncLdargAfterCall()
    {
        // User function that reads params after calling rand8
        // Tests that WriteLdarg saves _runtimeValueInA to TEMP
        var bytes = GetProgramBytes(
            """
            static byte rndint(byte a, byte b)
            {
                byte range = (byte)(b - a);
                byte r = rand8();
                return (byte)((byte)(r % range) + a);
            }
            byte result = rndint(3, 10);
            pal_col(0, result);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void UserFunctionLocalsDoNotOverlapMain()
    {
        // User function locals must be allocated AFTER main's locals to avoid
        // memory corruption when the function is called.
        // Main allocates an array of 20 bytes; the user function has a local.
        // The function's local must be at an address >= main's array end.
        var (program, transpiler) = BuildProgram(
            """
            static byte add_offset(byte x)
            {
                byte temp = (byte)(x + 5);
                return temp;
            }
            byte[] data = new byte[20];
            data[0] = add_offset(3);
            pal_col(0, data[0]);
            while (true) ;
            """);

        var mainBytes = program.GetMainBlock();
        Assert.NotNull(mainBytes);

        // Main local: data array is 20 bytes starting at $0325 (base local address).
        // So main occupies $0325-$0338.
        // User function local "temp" must be at $0339 or later, NOT at $0325.
        var userBytes = program.GetMainBlock("add_offset");
        Assert.NotEmpty(userBytes);

        var userHex = Convert.ToHexString(userBytes);
        // The function stores to its local via STA Absolute (8D xx yy).
        // The address must NOT be $0325 (25 03 in little-endian) — that's main's array.
        // It should be $0339 or higher.
        Assert.DoesNotContain("8D2503", userHex);

        transpiler.Dispose();
    }

    [Fact]
    public void NestedFunctionCallsGetSeparateLocalAddresses()
    {
        // When outer_func calls inner_func, each must have its own local storage
        // to prevent inner_func from clobbering outer_func's locals.
        var (program, transpiler) = BuildProgram(
            """
            outer_func();
            ppu_on_all();
            while (true) ;

            static void outer_func()
            {
                byte local_outer = 42;
                inner_func();
                pal_col(0, local_outer);
            }

            static void inner_func()
            {
                byte local_inner = 99;
                pal_col(1, local_inner);
            }
            """);
        transpiler.Dispose();

        // Get full program bytes (main + user methods)
        var allBytes = program.ToBytes();
        var hex = Convert.ToHexString(allBytes);
        _logger.WriteLine($"NestedFunctionCalls full hex: {hex}");

        // Both literal values must appear: LDA #42 (A92A) and LDA #99 (A963)
        Assert.Contains("A92A", hex);
        Assert.Contains("A963", hex);

        // outer_func's local is stored at $0325 (STA $0325 = 8D2503)
        Assert.Contains("8D2503", hex);
        // inner_func's local must be at a DIFFERENT address ($0326 = 8D2603)
        // because outer_func calls inner_func and both are on the stack simultaneously
        Assert.Contains("8D2603", hex);
    }

    [Fact]
    public void NestedCallsWithMultipleLocalsGetCorrectOffsets()
    {
        // When a caller has multiple locals, the callee's frame must start
        // AFTER all the caller's locals, not just 1 byte later.
        var (program, transpiler) = BuildProgram(
            """
            multi_local_func();
            ppu_on_all();
            while (true) ;

            static void multi_local_func()
            {
                byte a = 10;
                byte b = 20;
                byte c = 30;
                byte d = 40;
                pal_col(0, a);
                pal_col(1, b);
                pal_col(2, c);
                pal_col(3, d);
                callee_func();
            }

            static void callee_func()
            {
                byte val = 77;
                pal_col(0, val);
            }
            """);
        transpiler.Dispose();

        var allBytes = program.ToBytes();
        var hex = Convert.ToHexString(allBytes);
        _logger.WriteLine($"MultiLocalNestedCalls full hex: {hex}");

        // multi_local_func has 4 byte locals at $0325-$0328
        Assert.Contains("8D2503", hex); // STA $0325 (local a)
        Assert.Contains("8D2603", hex); // STA $0326 (local b)
        Assert.Contains("8D2703", hex); // STA $0327 (local c)
        Assert.Contains("8D2803", hex); // STA $0328 (local d)

        // callee_func's local must be at $0329 (after all 4 caller locals)
        Assert.Contains("A94D", hex);   // LDA #77
        Assert.Contains("8D2903", hex); // STA $0329
    }

    [Fact]
    public void RecursiveUserMethodThrows()
    {
        // Self-recursive user methods should fail fast during transpilation
        // rather than silently producing overlapping frame offsets.
        var ex = Assert.ThrowsAny<Exception>(() => BuildProgram(
            """
            recurse();
            ppu_on_all();
            while (true) ;

            static void recurse()
            {
                byte x = 1;
                pal_col(0, x);
                recurse();
            }
            """));

        Assert.Contains("ecursive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultiParamUserFunction_LocalVarArgs()
    {
        // Test: calling a user-defined function with a runtime local + constant.
        // Use rand8() for runtime value and reference x after the call to force ldloc.
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            my_func(x, 5);
            pal_col(0, x);
            ppu_on_all();
            while (true) ;

            static void my_func(byte a, byte b) { pal_col(a, b); }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"MultiParamUserFunction_LocalVarArgs hex: {hex}");

        // Local 0 (x) is at $0325. After ldloc.0 (AD2503) loads x,
        // the next ldc.i4.5 (A905) loads the constant.
        // With the fix, a JSR pusha should appear between them.
        int ldloc = hex.IndexOf("AD2503");
        Assert.True(ldloc >= 0, $"LDA $0325 (load local 0) not found. Hex: {hex}");
        int ldc = hex.IndexOf("A905", ldloc);
        Assert.True(ldc >= 0, $"LDA #$05 (load constant 5) not found after ldloc. Hex: {hex}");
        Assert.True(ldc > ldloc, $"LDA #$05 should come after LDA $0325. Hex: {hex}");

        // The two LDA instructions should NOT be adjacent — there must be a JSR pusha between them.
        // AD2503 is 6 hex chars; if ldc == ldloc + 6, they're adjacent (no pusha).
        Assert.True(ldc > ldloc + 6,
            $"No JSR pusha between loading local and constant args — first arg will be lost. " +
            $"LDA $0325 at {ldloc}, LDA #$05 at {ldc}. Hex: {hex}");
    }

    [Fact]
    public void MultiParamUserFunction_TwoLocals()
    {
        // Two local variable args to a user-defined function.
        // Both x and y are used after the call to force the compiler to keep them as locals.
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte y = rand8();
            my_func(x, y);
            pal_col(0, x);
            pal_col(1, y);
            ppu_on_all();
            while (true) ;

            static void my_func(byte a, byte b) { pal_col(a, b); }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"MultiParamUserFunction_TwoLocals hex: {hex}");

        // x at $0325, y at $0326
        // For my_func(x, y): ldloc.0 (AD2503), ldloc.1 (AD2603), call my_func
        // A JSR pusha must appear between the two LDA instructions.
        int idx0 = hex.IndexOf("AD2503");
        Assert.True(idx0 >= 0, $"LDA $0325 not found. Hex: {hex}");
        int idx1 = hex.IndexOf("AD2603", idx0 + 6);
        Assert.True(idx1 >= 0, $"LDA $0326 not found after LDA $0325. Hex: {hex}");
        Assert.True(idx1 > idx0 + 6,
            $"No JSR pusha between two local arg loads. " +
            $"LDA $0325 at {idx0}, LDA $0326 at {idx1}. Hex: {hex}");
    }

    [Fact]
    public void MultiParamUserFunction_ThreeArgs()
    {
        // Three-arg user function with mix of local and constant args.
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            add3(x, 2, 3);
            pal_col(0, x);
            ppu_on_all();
            while (true) ;

            static void add3(byte a, byte b, byte c) { pal_col(a, (byte)(b + c)); }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"MultiParamUserFunction_ThreeArgs hex: {hex}");

        // Local 0 (x) at $0325, then constants 2 and 3
        int ldloc = hex.IndexOf("AD2503");
        Assert.True(ldloc >= 0, $"LDA $0325 not found. Hex: {hex}");
        // There should be a JSR pusha after loading x (before loading 2)
        int ldc2Index = hex.IndexOf("A902", ldloc);
        Assert.True(ldc2Index > ldloc + 6,
            $"No JSR pusha after loading local x before constant arg. Hex: {hex}");
    }

    [Fact]
    public void MultiParamUserFunction_ComputedArg()
    {
        // Test: calling a user-defined function where the second arg is a computed
        // expression: my_func(x, (byte)(y + 1)). The look-ahead must track IL stack
        // depth through the Add/Conv opcodes to find the Call.
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte y = rand8();
            my_func(x, (byte)(y + 1));
            pal_col(0, x);
            pal_col(1, y);
            ppu_on_all();
            while (true) ;

            static void my_func(byte a, byte b) { pal_col(a, b); }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"MultiParamUserFunction_ComputedArg hex: {hex}");

        // x at $0325, y at $0326.
        // For my_func(x, (byte)(y + 1)):
        //   ldloc.0 → LDA $0325 (load x)
        //   JSR pusha           (preserve x on cc65 stack)
        //   ldloc.1 → LDA $0326 (load y)
        //   ldc.i4.1 → ...
        //   add → ...
        //   conv.u1 → ...
        //   call my_func
        // A JSR pusha must appear after loading x so it survives the y+1 computation.
        int ldlocX = hex.IndexOf("AD2503");
        Assert.True(ldlocX >= 0, $"LDA $0325 (load local x) not found. Hex: {hex}");
        int ldlocY = hex.IndexOf("AD2603", ldlocX + 6);
        Assert.True(ldlocY >= 0, $"LDA $0326 (load local y) not found after x. Hex: {hex}");
        // The 6 hex chars before LDA $0326 should be a JSR (20 xx xx) for pusha
        Assert.True(ldlocY >= 6, $"LDA $0326 too early for preceding JSR. Hex: {hex}");
        Assert.Equal("20", hex.Substring(ldlocY - 6, 2));
    }
}
