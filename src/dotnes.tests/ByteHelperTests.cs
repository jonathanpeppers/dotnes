using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ByteHelperTests(ITestOutputHelper output) : RoslynTests(output)
{
    [Fact]
    public void PrivateByteParameterUsesHomeOnlyWhenEnabled()
    {
        const string source = """
            State.Result = helper(42);
            while (true) ;
            static byte helper(byte value) => (byte)(value ^ 0xA5);
            static class State { public static byte Result; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertStackParameter(original, "helper");
        var home = AssertHomeParameter(program, "helper");
        Assert.True(home >= NESConstants.LocalStackBase + 1); // separate from Result
        Assert.True(program.GetMainBlock("helper").Length < original.GetMainBlock("helper").Length);
        Assert.Equal(CallTargets(original, "main"), CallTargets(program, "main"));
    }

    [Fact]
    public void LinkedNativeCallerDisablesHomesWithoutExternDeclarations()
    {
        const string source = """
            State.Result = helper(42);
            while (true) ;
            static byte helper(byte value) => value;
            static class State { public static byte Result; }
            """;
        using var transpiler = BuildProgram(source, out var program);
        Assert.Empty(transpiler.ExternMethods);
        using var nativeSource = new StringReader("""
            .segment "CODE"
            native_callback:
                lda #7
                jsr helper
                rts
            """);
        var nativeBlocks = new Ca65Assembler().Assemble(nativeSource).ToArray();
        Assert.NotEmpty(nativeBlocks);
        foreach (var block in nativeBlocks)
            program.AddBlock(block);
        byte[] original = program.ToBytes();

        Assert.False(transpiler.TryOptimizeByteHelpers(program, transpiler.ReadStaticVoidMain().ToArray(),
            localHighWater: 1, hasNativeCode: nativeBlocks.Length > 0, out _));
        AssertStackParameter(program, "helper");
        Assert.Equal(original, program.ToBytes());
        Assert.True(program.Labels.TryResolve("native_callback", out _));
    }

    [Fact]
    public void LateBoundHostCallingManagedHelperRetainsStandardStorage()
    {
        using var assembly = CompileAssembly("""
            State.First = helper(42);
            State.Second = native_entry();
            while (true) ;
            static byte helper(byte value) => (byte)(value ^ 3);
            static extern byte native_entry();
            static class State { public static byte First, Second; }
            """);
        var baseline = NesCompiler.Compile(assembly);
        assembly.Position = 0;
        var program = NesCompiler.Compile(assembly, new CompilationOptions { OptimizeByteHelpers = true });
        AssertStackParameter(program, "helper");
        baseline.DefineExternalLabel("_native_entry", 0x6000);
        program.DefineExternalLabel("_native_entry", 0x6000);
        Assert.Equal(baseline.ToBytes(), program.ToBytes());
        ushort helper = program.GetLabels()["helper"];

        // The host uses the unchanged A-register call ABI. The callee, not the
        // native caller, creates and removes the software-stack parameter frame.
        byte[] host = [0xA9, 7, 0x20, (byte)helper, (byte)(helper >> 8), 0x60];
        var before = Execute(baseline, cpu => host.CopyTo(cpu.Memory, 0x6000));
        var after = Execute(program, cpu => host.CopyTo(cpu.Memory, 0x6000));
        Assert.Equal(new byte[] { 41, 4 }, before.Memory.AsSpan(NESConstants.LocalStackBase, 2).ToArray());
        Assert.Equal(before.Memory.AsSpan(NESConstants.LocalStackBase, 2).ToArray(),
            after.Memory.AsSpan(NESConstants.LocalStackBase, 2).ToArray());
        AssertBalancedStacks(before, after);
    }

    [Fact]
    public void UnreferencedExternalBindingDoesNotAddNativeEntryPoints()
    {
        using var assembly = CompileAssembly("""
            State.Result = helper(42);
            while (true) ;
            static byte helper(byte value) => (byte)(value ^ 3);
            static class State { public static byte Result; }
            """);
        var program = NesCompiler.Compile(assembly, new CompilationOptions { OptimizeByteHelpers = true });
        AssertHomeParameter(program, "helper");
        byte[] original = program.ToBytes();
        int blockCount = program.Blocks.Count;
        program.DefineExternalLabel("unreferenced_host", 0x6000);
        program.ResolveAddresses();
        Assert.Equal(0x6000, program.GetLabels()["unreferenced_host"]);
        Assert.Equal(blockCount, program.Blocks.Count);
        Assert.Equal(original, program.ToBytes());
        var result = Execute(program);
        Assert.Equal(41, result.Memory[NESConstants.LocalStackBase]);
        Assert.Equal(Cpu6502.SoftwareStackTop, result.SoftwareStackPointer);
        Assert.Equal(0xFF, result.SP);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BankPayloadBarrierDistinguishesPrgFromChr(bool isPrg)
    {
        using var assembly = CompileAssembly("""
            State.Result = helper(42);
            while (true) ;
            static byte helper(byte value) => value;
            static class State { public static byte Result; }
            """);
        string path = Path.Combine(Path.GetTempPath(), $"dotnes-byte-helper-bank-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, [0x60]);
            var asset = new BankedRomAsset(path, Bank: 0, Offset: 0, CpuAddress: isPrg ? (ushort)0x8000 : null);
            var baseline = Build(false);
            var optimized = Build(true);
            if (isPrg)
            {
                AssertStackParameter(optimized, "helper");
                Assert.Equal(baseline.ToBytes(), optimized.ToBytes());
            }
            else
            {
                AssertHomeParameter(optimized, "helper");
                Assert.True(optimized.GetMainBlock("helper").Length < baseline.GetMainBlock("helper").Length);
            }

            Program6502 Build(bool optimize)
            {
                assembly.Position = 0;
                using var transpiler = new Transpiler(assembly, [], _logger,
                    mapper: 4, mmc3BankedLayout: true,
                    prgBankAssets: isPrg ? [asset] : [],
                    chrBankAssets: isPrg ? [] : [asset])
                {
                    OptimizeByteHelpers = optimize,
                };
                var program = transpiler.BuildProgram6502(out _, out _);
                Assert.Empty(transpiler.ExternMethods);
                return program;
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NestedHelpersHaveDistinctHomes()
    {
        using var transpiler = BuildProgram(
            """
            State.Result = outer(42);
            while (true) ;
            static byte outer(byte value)
            {
                byte saved = (byte)(value ^ 3);
                byte result = inner(value);
                return (byte)(result + saved);
            }
            static byte inner(byte value) => (byte)(value ^ 0xA5);
            static class State { public static byte Result; }
            """, out var program, optimizeByteHelpers: true);
        ushort outer = AssertHomeParameter(program, "outer");
        ushort inner = AssertHomeParameter(program, "inner");
        Assert.NotEqual(outer, inner);
        var stores = program.GetBlock("outer")!.InstructionsWithLabels
            .Select(i => i.Instruction).Where(i => i.Opcode == Opcode.STA && i.Mode == AddressMode.Absolute)
            .Select(i => Assert.IsType<AbsoluteOperand>(i.Operand).Address).ToArray();
        Assert.DoesNotContain(inner, stores);
        Assert.All(stores.Skip(1), address => Assert.True(address < Math.Min(outer, inner)));
    }

    [Theory]
    [InlineData("public static byte helper(byte value) => value;")]
    [InlineData("internal static byte helper(byte value) => value;")]
    [InlineData("private static int helper(byte value) => value;")]
    [InlineData("private static byte helper(int value) => (byte)value;")]
    [InlineData("[System.Obsolete] private static byte helper(byte value) => value;")]
    [InlineData("private static byte helper(byte value) => rand8();")]
    [InlineData("private static byte helper(byte value) => State.Result;")]
    public void UnprovenHelpersRetainStandardStorage(string helper)
    {
        string source = $$"""
            class Program
            {
                static void Main() { State.Result = (byte)helper(42); while (true) ; }
                {{helper}}
            }
            static class State { public static byte Result; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertStackParameter(program, "helper");
        Assert.Equal(original.ToBytes(), program.ToBytes());
    }

    [Fact]
    public void PrivateHelperReachableFromPublicEntryRetainsStorage()
    {
        const string source = """
            State.Run();
            while (true) ;
            static class State
            {
                public static byte Result;
                public static void Run() { Result = helper(42); }
                private static byte helper(byte value) => value;
            }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertStackParameter(program, "helper");
        Assert.Equal(original.ToBytes(), program.ToBytes());
    }

    [Theory]
    [InlineData("value++; return value;")]
    [InlineData("unsafe { byte* address = &value; return *address; }")]
    public void UnsupportedMutableOrAddressTakenParametersKeepTheirDiagnostic(string body)
    {
        string source = $$"""
            byte result = helper(42);
            pal_col(0, result);
            while (true) ;
            static byte helper(byte value) { {{body}} }
            """;
        var baseline = Assert.Throws<TranspileException>(() => BuildProgram(source, out _, allowUnsafe: true));
        var optimized = Assert.Throws<TranspileException>(() =>
            BuildProgram(source, out _, allowUnsafe: true, optimizeByteHelpers: true));
        Assert.Equal(baseline.Message, optimized.Message);
    }

    [Fact]
    public void EveryByteInputAndAliasedDestinationRetainByValueSemantics()
    {
        const string source = """
            State.Input = helper(State.Input);
            while (true) ;
            static byte helper(byte value) => (byte)(value ^ 0xA5);
            static class State { public static byte Input; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertHomeParameter(program, "helper");
        for (int input = 0; input <= byte.MaxValue; input++)
        {
            var before = Execute(original, cpu => cpu.Memory[NESConstants.LocalStackBase] = (byte)input);
            var after = Execute(program, cpu => cpu.Memory[NESConstants.LocalStackBase] = (byte)input);
            Assert.Equal((byte)(input ^ 0xA5), before.Memory[NESConstants.LocalStackBase]);
            Assert.Equal(before.Memory[NESConstants.LocalStackBase], after.Memory[NESConstants.LocalStackBase]);
            AssertBalancedStacks(before, after);
        }
    }

    [Fact]
    public void PrivateClassHelperPreservesEarlyReturns()
    {
        const string source = """
            class Program
            {
                static void Main()
                {
                    State.Result = helper(State.Input);
                    while (true) ;
                }
                private static byte helper(byte value)
                {
                    if (value < 128) return 0;
                    return value;
                }
            }
            static class State { public static byte Input, Result; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertHomeParameter(program, "helper");
        for (int input = 0; input <= byte.MaxValue; input++)
        {
            var before = Execute(original, cpu => cpu.Memory[NESConstants.LocalStackBase] = (byte)input);
            var after = Execute(program, cpu => cpu.Memory[NESConstants.LocalStackBase] = (byte)input);
            Assert.Equal(input < 128 ? 0 : input, before.Memory[NESConstants.LocalStackBase + 1]);
            Assert.Equal(before.Memory[NESConstants.LocalStackBase + 1], after.Memory[NESConstants.LocalStackBase + 1]);
            AssertBalancedStacks(before, after);
        }
    }

    [Theory]
    [InlineData("==", 42)]
    [InlineData("<", 128)]
    [InlineData(">", 127)]
    public void NumericRelativeBranchesPreserveEveryByteResult(string comparison, int threshold)
    {
        string source = $$"""
            State.Result = helper(State.Input);
            while (true) ;
            static byte helper(byte value) => (byte)(value {{comparison}} {{threshold}} ? 1 : 0);
            static class State { public static byte Input, Result; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertHomeParameter(program, "helper");
        var beforeBlock = original.GetBlock("helper")!;
        var afterBlock = program.GetBlock("helper")!;
        Assert.Contains(beforeBlock.InstructionsWithLabels, i => i.Instruction.Operand is RelativeByteOperand);
        Assert.DoesNotContain(afterBlock.InstructionsWithLabels, i => i.Instruction.Operand is RelativeByteOperand);
        Assert.Contains(afterBlock.InstructionsWithLabels, i =>
            i.Instruction.Operand is RelativeOperand target
            && target.Label.StartsWith("@bytehelper_target_", StringComparison.Ordinal));

        // The comparison's false path targets cleanup, which is removed; its
        // synthesized branch label must move to RTS rather than disappear.
        int offset = 0;
        int cleanupOffset = beforeBlock.Size - beforeBlock[beforeBlock.Count - 1].Size
            - beforeBlock[beforeBlock.Count - 2].Size;
        bool targetsCleanup = false;
        foreach (var (instruction, _) in beforeBlock.InstructionsWithLabels)
        {
            if (instruction.Operand is RelativeByteOperand branch)
                targetsCleanup |= offset + instruction.Size + branch.Offset == cleanupOffset;
            offset += instruction.Size;
        }
        Assert.True(targetsCleanup);

        for (int input = 0; input <= byte.MaxValue; input++)
        {
            var before = Execute(original, cpu => cpu.Memory[NESConstants.LocalStackBase] = (byte)input);
            var after = Execute(program, cpu => cpu.Memory[NESConstants.LocalStackBase] = (byte)input);
            bool expected = comparison switch
            {
                "==" => input == threshold,
                "<" => input < threshold,
                ">" => input > threshold,
                _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
            };
            Assert.Equal(expected ? 1 : 0, before.Memory[NESConstants.LocalStackBase + 1]);
            Assert.Equal(before.Memory[NESConstants.LocalStackBase + 1], after.Memory[NESConstants.LocalStackBase + 1]);
            AssertBalancedStacks(before, after);
        }
    }

    [Fact]
    public void NumericBranchTargetsCannotCollideWithMethodNames()
    {
        const string source = """
            State.First = helper(State.Input);
            State.Second = helper_bytehelper_target_7(7);
            State.Third = helper_bytehelper_target_8(8);
            while (true) ;
            static byte helper(byte value) => (byte)(value == 42 ? 1 : 0);
            static byte helper_bytehelper_target_7(byte value) => (byte)(value ^ 3);
            static byte helper_bytehelper_target_8(byte value) => (byte)(value ^ 3);
            static class State { public static byte First, Input, Second, Third; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertHomeParameter(program, "helper");
        var block = original.GetBlock("helper")!;
        int[] offsets = new int[block.Count];
        for (int i = 1; i < block.Count; i++)
            offsets[i] = offsets[i - 1] + block[i - 1].Size;
        Assert.Contains(Enumerable.Range(0, block.Count), i =>
            block[i].Operand is RelativeByteOperand branch
            && original.GetBlock($"helper_bytehelper_target_{Array.IndexOf(offsets, offsets[i] + block[i].Size + branch.Offset)}") != null);

        for (int input = 0; input <= byte.MaxValue; input++)
        {
            var before = Execute(original, cpu => cpu.Memory[NESConstants.LocalStackBase + 1] = (byte)input);
            var after = Execute(program, cpu => cpu.Memory[NESConstants.LocalStackBase + 1] = (byte)input);
            byte[] expected = [(byte)(input == 42 ? 1 : 0), (byte)input, 4, 11];
            Assert.Equal(expected, before.Memory.AsSpan(NESConstants.LocalStackBase, 4).ToArray());
            Assert.Equal(expected, after.Memory.AsSpan(NESConstants.LocalStackBase, 4).ToArray());
            AssertBalancedStacks(before, after);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FacadeOnlyLinksSuppliedNativeCodeWhenExternsAreDeclared(bool hasExtern)
    {
        using var assembly = CompileAssembly($$"""
            State.Result = helper(42);
            while (true) ;
            static byte helper(byte value) => (byte)(value ^ 3);
            {{(hasExtern ? "static extern void native_callback();" : "")}}
            static class State { public static byte Result; }
            """);
        using var native = new AssemblyReader(new StringReader("""
            .segment "CODE"
            _native_callback:
                lda #7
                jsr helper
                rts
            .segment "CHARS"
            .byte $12,$34
            """));
        var baseline = NesCompiler.Compile(assembly, assemblyFiles: [native]);
        assembly.Position = 0;
        var optimized = NesCompiler.Compile(assembly,
            new CompilationOptions { OptimizeByteHelpers = true }, [native]);
        if (hasExtern)
        {
            Assert.NotNull(baseline.GetBlock("_native_callback"));
            Assert.NotNull(optimized.GetBlock("_native_callback"));
            AssertStackParameter(optimized, "helper");
            Assert.Equal(baseline.ToBytes(), optimized.ToBytes());
        }
        else
        {
            Assert.Null(baseline.GetBlock("_native_callback"));
            Assert.Null(optimized.GetBlock("_native_callback"));
            AssertHomeParameter(optimized, "helper");
            foreach (bool optimize in new[] { false, true })
            {
                assembly.Position = 0;
                var withoutSources = NesCompiler.Compile(assembly,
                    new CompilationOptions { OptimizeByteHelpers = optimize });
                Assert.Equal(withoutSources.ToBytes(), (optimize ? optimized : baseline).ToBytes());
            }
        }
        Assert.Equal(new byte[] { 0x12, 0x34 }, Assert.Single(native.GetSegments()).Bytes);
        var before = Execute(baseline);
        var after = Execute(optimized);
        Assert.Equal(41, before.Memory[NESConstants.LocalStackBase]);
        Assert.Equal(41, after.Memory[NESConstants.LocalStackBase]);
        AssertBalancedStacks(before, after);
    }

    [Fact]
    public void FlagDependentCallResultsKeepStandardStorage()
    {
        const string source = """
            if (helper(State.Input) != 0) State.Result = 7;
            else State.Result = 9;
            while (true) ;
            static byte helper(byte value) => value;
            static class State { public static byte Input, Result; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertStackParameter(program, "helper");
        Assert.Equal(original.ToBytes(), program.ToBytes());
    }

    [Fact]
    public void MirroredRamAccessKeepsTheOriginalProgram()
    {
        const string source = """
            poke(0x0B26, 90);
            State.Result = helper(42);
            State.Result = peek(0x0B26);
            while (true) ;
            static byte helper(byte value) => value;
            static class State { public static byte Result; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertStackParameter(program, "helper");
        Assert.Equal(original.ToBytes(), program.ToBytes());
    }

    [Fact]
    public void VoidHelpersLeaveBothStacksBalanced()
    {
        const string source = """
            helper(42);
            helper(7);
            while (true) ;
            static void helper(byte value) { }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        ushort home = AssertHomeParameter(program, "helper");
        var before = Execute(original);
        var after = Execute(program);
        Assert.Equal(7, after.Memory[home]);
        AssertBalancedStacks(before, after);
    }

    [Fact]
    public void LargeHelpersRemainUnoptimized()
    {
        string source = $$"""
            State.Result = helper(State.Result);
            while (true) ;
            static byte helper(byte value)
            {
                byte result = value;
                {{string.Join(Environment.NewLine, Enumerable.Repeat("result = (byte)(result ^ 0xA5);", 32))}}
                return result;
            }
            static class State { public static byte Result; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertStackParameter(program, "helper");
        Assert.Equal(original.ToBytes(), program.ToBytes());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void LocalCountBoundaryRetainsBaselineSemantics(int count)
    {
        string source = $$"""
            class Program
            {
                static void Main() { State.Result = helper(State.Input); while (true) ; }
                static byte helper(byte value)
                {
                    {{string.Join(Environment.NewLine, Enumerable.Range(0, count).Select(i => $"byte v{i} = (byte)(value ^ {i + 1});"))}}
                    {{string.Join(Environment.NewLine, Enumerable.Range(0, count - 1).Select(i => $"if (value == {i}) return v{i};"))}}
                    return v{{count - 1}};
                }
            }
            static class State { public static byte Input, Result; }
            """;
        var assembly = CompileAssembly(source);
        using (var pe = new PEReader(assembly, PEStreamOptions.LeaveOpen))
        {
            var metadata = pe.GetMetadataReader();
            var method = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition)
                .Single(m => metadata.GetString(m.Name) == "helper");
            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            var signature = metadata.GetBlobReader(metadata.GetStandaloneSignature(body.LocalSignature).Signature);
            Assert.Equal(0x07, signature.ReadByte());
            Assert.Equal(count, signature.ReadCompressedInteger());
        }
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        Assert.True(baseline.UserMethods["helper"].Length <= 64);
        if (count == 4)
            AssertHomeParameter(program, "helper");
        else
        {
            AssertStackParameter(program, "helper");
            Assert.Equal(original.ToBytes(), program.ToBytes());
        }
        for (int input = 0; input <= byte.MaxValue; input++)
        {
            var before = Execute(original, cpu => cpu.Memory[NESConstants.LocalStackBase] = (byte)input);
            var after = Execute(program, cpu => cpu.Memory[NESConstants.LocalStackBase] = (byte)input);
            int expected = input ^ (input < count - 1 ? input + 1 : count);
            Assert.Equal(expected, before.Memory[NESConstants.LocalStackBase + 1]);
            Assert.Equal(expected, after.Memory[NESConstants.LocalStackBase + 1]);
            AssertBalancedStacks(before, after);
        }
    }

    [Theory]
    [InlineData(NESConstants.MaxLocalBytes - 2, false)]
    [InlineData(NESConstants.MaxLocalBytes - 4, true)]
    public void ParameterHomesRespectPendingArgumentsAndNeighboringStorage(int padding, bool fits)
    {
        string source = $$"""
            {{string.Join(Environment.NewLine, Enumerable.Range(0, padding).Select(i => $"State.P{i} = 90;"))}}
            State.Result = pair(10, helper(42));
            while (true) ;
            static byte helper(byte value) => value;
            static byte pair(byte first, byte second) => first;
            static class State
            {
                public static byte {{string.Join(", ", Enumerable.Range(0, padding).Select(i => $"P{i}"))}}, Result;
            }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        var before = Execute(original);
        var after = Execute(program);
        int result = NESConstants.LocalStackBase + padding;
        Assert.Equal(10, before.Memory[result]);
        Assert.Equal(10, after.Memory[result]);
        if (fits)
            Assert.Equal(result + 1, AssertHomeParameter(program, "helper"));
        else
        {
            AssertStackParameter(program, "helper");
            Assert.Equal(original.ToBytes(), program.ToBytes());
        }
        AssertBalancedStacks(before, after);
        Assert.All(before.Memory.AsSpan(NESConstants.LocalStackBase, padding).ToArray(), value => Assert.Equal(90, value));
        Assert.All(after.Memory.AsSpan(NESConstants.LocalStackBase, padding).ToArray(), value => Assert.Equal(90, value));
    }

    [Fact]
    public void EffectfulNestedArgumentsRunExactlyOnceInOrder()
    {
        const string source = """
            State.Result = helper(next(helper(next(7))));
            while (true) ;
            static byte next(byte value)
            {
                State.Count = (byte)(State.Count + 1);
                State.Order = (byte)(State.Order * 2 + value);
                return State.Count;
            }
            static byte helper(byte value) => (byte)(value ^ 3);
            static class State { public static byte Count, Order, Result; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertHomeParameter(program, "helper");
        AssertStackParameter(program, "next");
        var before = Execute(original);
        var after = Execute(program);
        Assert.Equal(new byte[] { 2, 16, 1 }, before.Memory.AsSpan(NESConstants.LocalStackBase, 3).ToArray());
        Assert.Equal(before.Memory.AsSpan(NESConstants.LocalStackBase, 3).ToArray(),
            after.Memory.AsSpan(NESConstants.LocalStackBase, 3).ToArray());
        AssertBalancedStacks(before, after);
    }

    [Fact]
    public void RepeatedCallsInitializeLoopLocalsAndKeepNestedCallerState()
    {
        const string source = """
            State.First = outer(3);
            State.Second = outer(5);
            while (true) ;
            static byte outer(byte value)
            {
                byte saved = (byte)(value ^ 1);
                byte result = inner(value);
                return (byte)(result + saved);
            }
            static byte inner(byte value)
            {
                byte total = 0;
                for (byte i = 0; i < 3; i++)
                    total++;
                return (byte)(value + total);
            }
            static class State { public static byte First, Second; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertHomeParameter(program, "outer");
        AssertHomeParameter(program, "inner");
        var before = Execute(original);
        var after = Execute(program);
        Assert.Equal(new byte[] { 8, 12 }, before.Memory.AsSpan(NESConstants.LocalStackBase, 2).ToArray());
        Assert.Equal(before.Memory.AsSpan(NESConstants.LocalStackBase, 2).ToArray(),
            after.Memory.AsSpan(NESConstants.LocalStackBase, 2).ToArray());
        AssertBalancedStacks(before, after);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParameterHomesReduceMeasuredInstructionAndStackTraffic(bool nested)
    {
        string source = $$"""
            for (byte batch = 0; batch < 10; batch++)
                for (byte i = 0; i < 200; i++)
                    State.Result = {{(nested ? "outer" : "helper")}}(State.Result);
            while (true) ;
            static byte helper(byte value) => (byte)(value ^ 0xA5);
            {{(nested ? "static byte outer(byte value) => helper(value);" : "")}}
            static class State { public static byte Result; }
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertHomeParameter(program, "helper");
        if (nested)
            AssertHomeParameter(program, "outer");
        var before = Execute(original, instructionLimit: 500000);
        var after = Execute(program, instructionLimit: 500000);
        Assert.Equal(0, before.Memory[NESConstants.LocalStackBase]);
        Assert.Equal(before.Memory[NESConstants.LocalStackBase], after.Memory[NESConstants.LocalStackBase]);
        AssertBalancedStacks(before, after);
        Assert.True(after.InstructionCount < before.InstructionCount * 0.95);
        Assert.True(after.SoftwareStackWrites < before.SoftwareStackWrites * 0.20);
        Assert.True(after.SoftwareStackPointerWrites < before.SoftwareStackPointerWrites);
        _logger.WriteLine($"{(nested ? "Nested" : "Leaf")} 2000 updates: instructions {before.InstructionCount} -> {after.InstructionCount}; software-stack writes {before.SoftwareStackWrites} -> {after.SoftwareStackWrites}; stack-pointer writes {before.SoftwareStackPointerWrites} -> {after.SoftwareStackPointerWrites}; main/helper bytes {original.GetMainBlock().Length + original.GetMainBlock("helper").Length + (nested ? original.GetMainBlock("outer").Length : 0)} -> {program.GetMainBlock().Length + program.GetMainBlock("helper").Length + (nested ? program.GetMainBlock("outer").Length : 0)}.");
    }

    static Cpu6502 Execute(Program6502 program, Action<Cpu6502>? initialize = null, int instructionLimit = 100000)
    {
        var main = program.GetBlock("main")!;
        var terminal = main[main.Count - 1];
        Assert.Equal(Opcode.JMP, terminal.Opcode);
        string label = Assert.IsType<LabelOperand>(terminal.Operand).Label;
        byte[] bytes = program.ToBytes();
        Assert.True(program.Labels.TryResolve(label, out ushort stop));
        ushort entry = program.GetBlockAddress(main);
        Assert.Equal(entry + main.Size - terminal.Size, stop); // the fixture's final while (true)
        var cpu = new Cpu6502(bytes, program.BaseAddress, entry);
        initialize?.Invoke(cpu);
        cpu.RunUntil(stop, instructionLimit);
        return cpu;
    }

    static void AssertBalancedStacks(Cpu6502 before, Cpu6502 after)
    {
        Assert.Equal(Cpu6502.SoftwareStackTop, before.SoftwareStackPointer);
        Assert.Equal(Cpu6502.SoftwareStackTop, after.SoftwareStackPointer);
        Assert.Equal(0xFF, before.SP);
        Assert.Equal(0xFF, after.SP);
    }

    [Theory]
    [InlineData("static byte helper(byte value) => inner(value); static byte inner(byte value) => rand8();")]
    [InlineData("static byte helper(byte value) => helper(value);")]
    [InlineData("static byte helper(byte value) => inner(value); static byte inner(byte value) => helper(value);")]
    public void UnsafeCalleesAndParameterOnlyCyclesRetainStandardStorage(string helpers)
    {
        string source = $$"""
            byte result = helper(42);
            pal_col(0, result);
            while (true) ;
            {{helpers}}
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertStackParameter(program, "helper");
        Assert.Equal(original.ToBytes(), program.ToBytes());
    }

    [Fact]
    public void ExternDeclarationDisablesHomesEvenWhenUnused()
    {
        const string source = """
            byte result = helper(42);
            pal_col(0, result);
            while (true) ;
            static byte helper(byte value) => value;
            static extern void unproven();
            """;
        using var baseline = BuildProgram(source, out var original, allowUnsafe: true);
        using var optimized = BuildProgram(source, out var program, allowUnsafe: true, optimizeByteHelpers: true);
        AssertStackParameter(program, "helper");
        Assert.Equal(original.ToBytes(), program.ToBytes());
    }

    [Fact]
    public void CallbackDisablesHomesForMainAndCallbackCallees()
    {
        const string source = """
            unsafe { nmi_set_callback(&callback); }
            byte result = helper(42);
            pal_col(0, result);
            while (true) ;
            static byte helper(byte value) => value;
            static void callback() { pal_col(0, helper(10)); }
            """;
        using var baseline = BuildProgram(source, out var original, allowUnsafe: true);
        using var optimized = BuildProgram(source, out var program, allowUnsafe: true, optimizeByteHelpers: true);
        AssertStackParameter(program, "helper");
        Assert.Equal(original.ToBytes(), program.ToBytes());
    }

    [Fact]
    public void MultiArgumentHelpersRetainStandardStorage()
    {
        const string source = """
            byte result = helper(42, 10);
            pal_col(0, result);
            while (true) ;
            static byte helper(byte first, byte second) => (byte)(first ^ second);
            """;
        using var baseline = BuildProgram(source, out var original);
        using var optimized = BuildProgram(source, out var program, optimizeByteHelpers: true);
        AssertStackParameter(program, "helper");
        Assert.Equal(original.ToBytes(), program.ToBytes());
    }

    static ushort AssertHomeParameter(Program6502 program, string method)
    {
        var block = program.GetBlock(method)!;
        Assert.Equal(Opcode.STA, block[0].Opcode);
        Assert.Equal(AddressMode.Absolute, block[0].Mode);
        ushort address = Assert.IsType<AbsoluteOperand>(block[0].Operand).Address;
        Assert.DoesNotContain("incsp1", CallTargets(program, method));
        Assert.DoesNotContain(block.InstructionsWithLabels, i =>
            i.Instruction.Opcode == Opcode.LDA && i.Instruction.Mode == AddressMode.IndirectIndexed);
        return address;
    }

    static void AssertStackParameter(Program6502 program, string method)
    {
        var block = program.GetBlock(method)!;
        Assert.Equal(Opcode.JSR, block[0].Opcode);
        Assert.Equal("pusha", Assert.IsType<LabelOperand>(block[0].Operand).Label);
    }

    static string[] CallTargets(Program6502 program, string method) =>
        program.GetBlock(method)!.InstructionsWithLabels.Select(i => i.Instruction)
            .Where(i => i.Opcode == Opcode.JSR).Select(i => Assert.IsType<LabelOperand>(i.Operand).Label).ToArray();
}
