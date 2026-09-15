using System.Reflection.Metadata;
using Xunit.Abstractions;

namespace dotnes.tests;

public class InlinedSnapshotTests(ITestOutputHelper output) : ExecutionTests(output)
{
    static Dictionary<int, int> Spill(ILInstruction[] instructions,
        params (int Producer, PrimitiveTypeCode Type, int? Expression)[] producers)
    {
        var analysis = new ILValueAnalysis(instructions, new ReflectionCache());
        var reusable = producers.Where(p => p.Expression.HasValue).ToDictionary(
            p => p.Producer, p => (p.Type, p.Expression!.Value));
        var slots = new Dictionary<int, int>();
        var rewritten = ILExpressionSpiller.Rewrite(instructions, analysis,
            producers.Select(p => p.Producer).ToHashSet(),
            wordProducers: producers.Where(p => NumericStorage.IsWord(p.Type)).Select(p => p.Producer).ToHashSet(),
            spillLocals: slots, minimumLocalIndex: 12,
            signedWordProducers: producers.Where(p => p.Type == PrimitiveTypeCode.Int16).Select(p => p.Producer).ToHashSet(),
            reusableSnapshots: reusable);
        Assert.All(slots.Values, local => Assert.True(local >= 12));
        Assert.Equal(rewritten.Length, rewritten.Select(i => i.Offset).Distinct().Count());
        Assert.Equal(instructions.SelectMany(ILValueAnalysis.GetBranchTargets).Order(),
            rewritten.SelectMany(ILValueAnalysis.GetBranchTargets).Order());
        return slots;
    }

    [Theory]
    [InlineData(PrimitiveTypeCode.Byte, PrimitiveTypeCode.Byte, true)]
    [InlineData(PrimitiveTypeCode.Boolean, PrimitiveTypeCode.Boolean, true)]
    [InlineData(PrimitiveTypeCode.SByte, PrimitiveTypeCode.SByte, true)]
    [InlineData(PrimitiveTypeCode.Int16, PrimitiveTypeCode.Int16, true)]
    [InlineData(PrimitiveTypeCode.UInt16, PrimitiveTypeCode.UInt16, true)]
    [InlineData(PrimitiveTypeCode.Byte, PrimitiveTypeCode.SByte, false)]
    [InlineData(PrimitiveTypeCode.Byte, PrimitiveTypeCode.Boolean, false)]
    [InlineData(PrimitiveTypeCode.Int16, PrimitiveTypeCode.UInt16, false)]
    [InlineData(PrimitiveTypeCode.Byte, PrimitiveTypeCode.UInt16, false)]
    [InlineData(PrimitiveTypeCode.Object, PrimitiveTypeCode.Object, false)]
    [InlineData(PrimitiveTypeCode.String, PrimitiveTypeCode.String, false)]
    public void DifferentExpressionsShareOnlyExactTypeSlots(PrimitiveTypeCode first,
        PrimitiveTypeCode second, bool shared)
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloc_0, 0), new(ILOpCode.Pop, 1),
            new(ILOpCode.Ldloc_1, 2), new(ILOpCode.Pop, 3), new(ILOpCode.Ret, 4),
        ];
        var slots = Spill(il, (0, first, 1), (2, second, 2));
        Assert.Equal(shared, slots[0] == slots[2]);
    }

    [Fact]
    public void SameExpressionAndUnmarkedSnapshotsRetainDedicatedSlots()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloc_0, 0), new(ILOpCode.Pop, 1),
            new(ILOpCode.Ldloc_1, 2), new(ILOpCode.Pop, 3),
            new(ILOpCode.Ldloc_2, 4), new(ILOpCode.Pop, 5),
            new(ILOpCode.Ldloc_3, 6), new(ILOpCode.Pop, 7), new(ILOpCode.Ret, 8),
        ];
        var slots = Spill(il, (0, PrimitiveTypeCode.Byte, 1), (2, PrimitiveTypeCode.Byte, 1),
            (4, PrimitiveTypeCode.Byte, null), (6, PrimitiveTypeCode.Byte, 2));
        Assert.Equal(3, slots.Values.Distinct().Count());
        Assert.NotEqual(slots[0], slots[2]);
        Assert.NotEqual(slots[0], slots[4]);
        Assert.NotEqual(slots[2], slots[4]);
        Assert.Equal(slots[0], slots[6]);
    }

    [Fact]
    public void DuplicatedValueRemainsLiveThroughItsLastConsumer()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldarg_0, 0), new(ILOpCode.Dup, 1),
            new(ILOpCode.Ldarg_1, 2), new(ILOpCode.Add, 3),
            new(ILOpCode.Pop, 4), new(ILOpCode.Pop, 5),
            new(ILOpCode.Ldarg_2, 6), new(ILOpCode.Pop, 7), new(ILOpCode.Ret, 8),
        ];
        var slots = Spill(il, (0, PrimitiveTypeCode.Byte, 1), (2, PrimitiveTypeCode.Byte, 2),
            (3, PrimitiveTypeCode.Byte, 3), (6, PrimitiveTypeCode.Byte, 4));
        Assert.NotEqual(slots[0], slots[2]);
        Assert.NotEqual(slots[0], slots[3]);
        Assert.NotEqual(slots[2], slots[3]);
        Assert.Equal(slots[0], slots[6]);
    }

    [Fact]
    public void BranchArmsShareSlotsButNotTheValueCarriedAcrossThem()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldarg_0, 0), new(ILOpCode.Ldarg_1, 1),
            new(ILOpCode.Brfalse_s, 2, 4),
            new(ILOpCode.Ldarg_2, 4), new(ILOpCode.Pop, 5), new(ILOpCode.Br_s, 6, 2),
            new(ILOpCode.Ldarg_3, 8), new(ILOpCode.Pop, 9),
            new(ILOpCode.Pop, 10), new(ILOpCode.Ret, 11),
        ];
        var slots = Spill(il, (0, PrimitiveTypeCode.Byte, 1), (3, PrimitiveTypeCode.Byte, 2),
            (6, PrimitiveTypeCode.Byte, 3));
        Assert.NotEqual(slots[0], slots[3]);
        Assert.NotEqual(slots[0], slots[6]);
        Assert.Equal(slots[3], slots[6]);
    }

    [Fact]
    public void ValueCarriedAcrossLoopDoesNotShareLoopSnapshot()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Ldarg_0, 0), new(ILOpCode.Ldarg_1, 1), new(ILOpCode.Pop, 2),
            new(ILOpCode.Ldarg_2, 3), new(ILOpCode.Brtrue_s, 4, 251),
            new(ILOpCode.Pop, 6), new(ILOpCode.Ret, 7),
        ];
        var slots = Spill(il, (0, PrimitiveTypeCode.Byte, 1), (1, PrimitiveTypeCode.Byte, 2));
        Assert.NotEqual(slots[0], slots[1]);
    }

    [Fact]
    public void BackwardEdgeKeepsLexicallyLaterDefinitionLive()
    {
        ILInstruction[] il =
        [
            new(ILOpCode.Br_s, 0, 6),
            new(ILOpCode.Ldarg_0, 2), new(ILOpCode.Pop, 3), new(ILOpCode.Pop, 4),
            new(ILOpCode.Br_s, 5, 1), new(ILOpCode.Nop, 7),
            new(ILOpCode.Ldarg_1, 8), new(ILOpCode.Br_s, 9, 247),
        ];
        var slots = Spill(il, (1, PrimitiveTypeCode.Byte, 1), (6, PrimitiveTypeCode.Byte, 2));
        Assert.NotEqual(slots[1], slots[6]);
    }

    [Fact]
    public void NegativeOffsetsAloneDoNotAuthorizeReuse()
    {
        using var dll = Utilities.GetResource("hello.release.dll");
        using var transpiler = new Transpiler(dll, Array.Empty<AssemblyReader>());
        transpiler.NumericTypes["main"] = new([PrimitiveTypeCode.Byte], [], PrimitiveTypeCode.Void);
        ILInstruction[] il =
        [
            new(ILOpCode.Ldloc_0, -1), new(ILOpCode.Pop, -2),
            new(ILOpCode.Ldloc_0, -3), new(ILOpCode.Pop, -4), new(ILOpCode.Ret, -5),
        ];
        var analysis = new ILValueAnalysis(il, new ReflectionCache());
        var rewritten = transpiler.RewriteTypedExpressionValues(il, analysis, new HashSet<int> { 0, 2 },
            [PrimitiveTypeCode.Byte, null, PrimitiveTypeCode.Byte, null, null], "main");
        Assert.Equal(3, transpiler.NumericTypes["main"].Locals.Length);
        Assert.Equal(new[] { 1, 2 }, rewritten.Where(i => i.GetStlocIndex() != null)
            .Select(i => i.GetStlocIndex()!.Value));
    }

    [Fact]
    public void RepeatedInlinedExpressionsBoundSnapshotStorageButAuthoredExpressionsDoNot()
    {
        int LocalCount(int repetitions, bool inline)
        {
            string expression = inline ? "Combine(a, b)" : "(byte)(a + (b << 4))";
            string body = string.Concat(Enumerable.Repeat(
                $"value = {expression}; poke(0x6000, value);", repetitions));
            string source = $$"""
                Run(255, 8);
                while (true) ;
                static void Run(byte a, byte b)
                {
                    byte value;
                    {{body}}
                }
                static byte Combine(byte a, byte b) => (byte)(a + (b << 4));
                """;
            using var transpiler = BuildProgram(source, out _);
            return transpiler.NumericTypes["Run"].Locals.Length;
        }
        int once = LocalCount(1, inline: true);
        int repeated = LocalCount(12, inline: true);
        int authored = LocalCount(12, inline: false);
        _logger.WriteLine($"Local slots: one inline={once}; twelve inline={repeated}; twelve authored={authored}");
        Assert.Equal(once, repeated);
        Assert.True(authored > once);
    }

    [Fact]
    public void OnlyNonconstantInliningEnablesMethodSnapshotCleanup()
    {
        int LocalCount(string prefix)
        {
            string body = string.Concat(Enumerable.Repeat(
                "value = (byte)(a + (b << 4)); poke(0x6000, value);", 12));
            using var transpiler = BuildProgram(
                $$"""
                Run(255, 8);
                while (true) ;
                static void Run(byte a, byte b)
                {
                    byte value;
                    {{prefix}}
                    {{body}}
                }
                static byte Combine(byte a, byte b) => (byte)(a + (b << 4));
                """, out _);
            return transpiler.NumericTypes["Run"].Locals.Length;
        }
        int untouched = LocalCount("");
        int constantOnly = LocalCount("value = Combine(255, 8); poke(0x6001, value);");
        int expanded = LocalCount("value = Combine(a, b); poke(0x6001, value);");
        _logger.WriteLine($"Method cleanup slots: untouched={untouched}; constant-only={constantOnly}; expanded={expanded}");
        Assert.Equal(untouched, constantOnly);
        Assert.True(expanded < untouched);
    }

    [Fact]
    public void InliningAnotherMethodDoesNotChangeUninlinedMethodInstructionsOrLocalTypes()
    {
        (ILInstruction[] Body, PrimitiveTypeCode?[] Locals) Capture(bool inlineOtherMethod)
        {
            string body = string.Concat(Enumerable.Repeat(
                "value = (byte)(a + (b << 4)); poke(0x6000, value);", 12));
            string other = inlineOtherMethod ? "Combine(a, b)" : "(byte)(a + (b << 4))";
            using var transpiler = BuildProgram(
                $$"""
                Untouched(255, 8);
                Other(255, 8);
                while (true) ;
                static void Untouched(byte a, byte b)
                {
                    byte value;
                    {{body}}
                }
                static void Other(byte a, byte b)
                {
                    byte value = {{other}};
                    poke(0x6001, value);
                }
                static byte Combine(byte a, byte b) => (byte)(a + (b << 4));
                """, out _);
            return (transpiler.UserMethods["Untouched"], transpiler.NumericTypes["Untouched"].Locals.ToArray());
        }
        var baseline = Capture(inlineOtherMethod: false);
        var withInlining = Capture(inlineOtherMethod: true);
        Assert.Equal(baseline.Body, withInlining.Body);
        Assert.Equal(baseline.Locals, withInlining.Locals);
    }

    [Theory]
    [InlineData(0, 266)]
    [InlineData(1, 274)]
    public void OriginalSnapshotsInExpandedMethodPreserveCallsMergesAndAuthoredLocals(byte flag, ushort selected)
    {
        var cpu = ExecuteProgram(
            """
            Run(255, 8, peek(0x6010));
            test_stop(); while (true) ;
            static extern void test_stop();
            static void Run(byte a, byte b, byte flag)
            {
                byte inlined = Combine(a, b);
                ushort full = (ushort)((a + b) + Helpers.Next());
                short difference = (short)((b - a) - Helpers.Next());
                ushort selected = (ushort)((a + b) + (flag == 0 ? Helpers.Next() : Helpers.Next() + 8));
                poke(0x6000, inlined);
                poke(0x6001, (byte)full);
                poke(0x6002, (byte)(full >> 8));
                poke(0x6003, (byte)difference);
                poke(0x6004, (byte)(difference >> 8));
                poke(0x6005, (byte)selected);
                poke(0x6006, (byte)(selected >> 8));
                poke(0x6007, Helpers.Calls);
            }
            static byte Combine(byte a, byte b) => (byte)(a + (b << 4));
            static class Helpers
            {
                public static byte Calls;
                public static byte Next() { Calls++; return Calls; }
            }
            """, initialize: cpu => cpu.Memory[0x6010] = flag);
        Assert.Equal(new byte[] { 127, 8, 1, 7, 255, (byte)selected, (byte)(selected >> 8), 3 },
            cpu.Memory[0x6000..0x6008]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(255, 8)]
    [InlineData(0, 8)]
    [InlineData(128, 127)]
    public void RepeatedWordExpressionsPreserveCarryBorrowAndFullPromotedSum(byte a, byte b)
    {
        var cpu = ExecuteProgram(
            """
            Run(peek(0x6010), peek(0x6011));
            test_stop(); while (true) ;
            static extern void test_stop();
            static void Run(byte a, byte b)
            {
                byte result = Carry(a, b);
                poke(0x6000, result);
                result = Borrow(a, b);
                poke(0x6001, result);
                result = Carry(a, b);
                poke(0x6002, result);
                result = Borrow(a, b);
                poke(0x6003, result);
                ushort full = (ushort)(a + b);
                poke(0x6004, (byte)full);
                poke(0x6005, (byte)(full >> 8));
            }
            static byte Carry(byte a, byte b) => (byte)(((a + b) << 1) >> 8);
            static byte Borrow(byte a, byte b) => (byte)(((a - b) << 1) >> 8);
            """, initialize: cpu =>
            {
                cpu.Memory[0x6010] = a;
                cpu.Memory[0x6011] = b;
            });
        byte carry = unchecked((byte)(((a + b) << 1) >> 8));
        byte borrow = unchecked((byte)(((a - b) << 1) >> 8));
        Assert.Equal(new byte[] { carry, borrow, carry, borrow, (byte)(a + b), (byte)((a + b) >> 8) },
            cpu.Memory[0x6000..0x6006]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0, 129)]
    [InlineData(1, 24)]
    public void BranchAndLoopExpressionsPreserveAuthoredLocalMutation(byte flag, byte expected)
    {
        var cpu = ExecuteProgram(
            """
            Run(255, 8, peek(0x6010));
            test_stop(); while (true) ;
            static extern void test_stop();
            static void Run(byte a, byte b, byte flag)
            {
                byte result = 0;
                for (byte i = 0; i < 3; i++)
                {
                    if (flag == 0) result = Combine(a, b);
                    else result = Combine(b, a);
                    a++;
                }
                poke(0x6000, result);
                poke(0x6001, a);
            }
            static byte Combine(byte a, byte b) => (byte)(a + (b << 4));
            """, initialize: cpu => cpu.Memory[0x6010] = flag);
        Assert.Equal(new byte[] { expected, 2 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void ArrayAliasesRemainSeparateFromInlinedScalarSnapshots()
    {
        var cpu = ExecuteProgram(
            """
            byte[] original = new byte[4];
            Run(original, 255, 8);
            byte value = original[0];
            poke(0x6000, value);
            value = original[1];
            poke(0x6001, value);
            value = original[2];
            poke(0x6002, value);
            test_stop(); while (true) ;
            static extern void test_stop();
            static void Run(byte[] original, byte a, byte b)
            {
                byte[] alias = original;
                byte result = Combine(a, b);
                original[0] = result;
                result = Combine(b, a);
                alias[1] = result;
                result = Combine(a, b);
                alias[2] = result;
            }
            static byte Combine(byte a, byte b) => (byte)(a + (b << 4));
            """);
        Assert.Equal(new byte[] { 127, 248, 127 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
