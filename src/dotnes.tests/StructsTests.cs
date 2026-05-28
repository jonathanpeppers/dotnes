using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class StructsTests : RoslynTests
{
    public StructsTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void FieldAccess()
    {
        // Struct with byte fields — stfld/ldfld map to STA/LDA on zero page
        var bytes = GetProgramBytes(
            """
            Point p;
            p.X = 2;
            p.Y = 0x14;
            pal_col(0, p.X);
            pal_col(1, p.Y);
            ppu_on_all();
            while (true) ;

            struct Point { public byte X; public byte Y; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // LDA #$02 for p.X = 2
        Assert.Contains("A902", hex);
        // LDA #$14 for p.Y = 0x14
        Assert.Contains("A914", hex);
        // STA to zero page for stfld (85 = STA zero page)
        Assert.Contains("85", hex);
    }

    [Fact]
    public void FieldArithmetic()
    {
        // Read struct fields, do arithmetic, use result
        var bytes = GetProgramBytes(
            """
            Vec2 p;
            p.X = 5;
            p.Y = 3;
            byte sum = (byte)(p.X + p.Y);
            pal_col(0, sum);
            ppu_on_all();
            while (true) ;

            struct Vec2 { public byte X; public byte Y; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // LDA #$05 for p.X = 5, LDA #$03 for p.Y = 3
        Assert.Contains("A905", hex);
        Assert.Contains("A903", hex);
        // CLC + ADC pattern for add (18 = CLC, 65/6D = ADC)
        Assert.Contains("18", hex);
    }

    [Fact]
    public void ArrayConstantIndex()
    {
        // Store and load struct fields via constant array index
        var bytes = GetProgramBytes(
            """
            Actor[] actors = new Actor[4];
            actors[0].x = 10;
            actors[1].y = 20;
            byte val = actors[0].x;
            pal_col(0, val);
            ppu_on_all();
            while (true) ;

            struct Actor { public byte x; public byte y; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A90A", hex); // LDA #$0A (10)
        Assert.Contains("A914", hex); // LDA #$14 (20)
        Assert.Contains("8D", hex); // STA absolute
        Assert.Contains("AD", hex); // LDA absolute (loading actors[0].x)
    }

    [Fact]
    public void ArrayFieldReadWrite()
    {
        // Write to one element, read from another, pass to function
        var bytes = GetProgramBytes(
            """
            Actor[] actors = new Actor[2];
            actors[0].x = 42;
            actors[1].x = 99;
            pal_col(0, actors[0].x);
            ppu_on_all();
            while (true) ;

            struct Actor { public byte x; public byte y; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A92A", hex); // LDA #$2A (42)
        Assert.Contains("A963", hex); // LDA #$63 (99)
        Assert.Contains("8D", hex); // STA absolute
        Assert.Contains("AD", hex); // LDA absolute (loading from actors[0].x)
    }

    [Fact]
    public void ArrayThreeFields()
    {
        // Struct with 3 fields to verify field offset calculation
        var bytes = GetProgramBytes(
            """
            Pos[] positions = new Pos[2];
            positions[0].x = 10;
            positions[0].y = 20;
            positions[0].flags = 3;
            positions[1].x = 40;
            pal_col(0, positions[1].x);
            ppu_on_all();
            while (true) ;

            struct Pos { public byte x; public byte y; public byte flags; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A90A", hex); // LDA #10
        Assert.Contains("A914", hex); // LDA #20
        Assert.Contains("A903", hex); // LDA #3
        Assert.Contains("A928", hex); // LDA #40
    }

    [Fact]
    public void FixedBufferConstantIndex()
    {
        // Option A: C# fixed buffer with constant indexes.
        var bytes = GetProgramBytes(
            """
            unsafe {
                Cluster cur;
                cur.sprite = 0x10;
                cur.id = 1;
                cur.layout[0] = 5;
                cur.layout[1] = 6;
                cur.layout[2] = 7;
                cur.layout[3] = 8;
                pal_col(0, cur.layout[2]);
            }
            ppu_on_all();
            while (true) ;

            unsafe struct Cluster {
                public byte sprite;
                public byte id;
                public fixed byte layout[4];
            }
            """, additionalAssemblyFiles: null, allowUnsafe: true);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Cluster: sprite@$0325, id@$0326, layout[0..3]@$0327..$032A
        // Each store is LDA #imm (A9 imm) + STA absolute (8D lo hi).
        Assert.Contains("A9108D2503", hex); // sprite = 0x10 -> $0325
        Assert.Contains("A9018D2603", hex); // id = 1 -> $0326
        Assert.Contains("A9058D2703", hex); // layout[0] = 5 -> $0327
        Assert.Contains("A9068D2803", hex); // layout[1] = 6 -> $0328
        Assert.Contains("A9078D2903", hex); // layout[2] = 7 -> $0329
        Assert.Contains("A9088D2A03", hex); // layout[3] = 8 -> $032A
        // Read of cur.layout[2] for the pal_col call: LDA $0329
        Assert.Contains("AD2903", hex);
    }

    [Fact]
    public void FixedBufferRuntimeIndex()
    {
        // Option A: runtime byte index uses LDX + AbsoluteX addressing.
        var bytes = GetProgramBytes(
            """
            byte i = 2;
            unsafe {
                Cluster cur;
                cur.layout[i] = 99;
            }
            ppu_on_all();
            while (true) ;

            unsafe struct Cluster {
                public byte sprite;
                public byte id;
                public fixed byte layout[4];
            }
            """, additionalAssemblyFiles: null, allowUnsafe: true);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        // Layout: i@$0325, cur.sprite@$0326, cur.id@$0327, cur.layout[0..3]@$0328..$032B
        Assert.Contains("A9028D2503", hex); // i = 2 -> $0325
        Assert.Contains("AE2503", hex);     // LDX $0325 (load index i)
        Assert.Contains("A9639D2803", hex); // LDA #99; STA $0328,X (layout[i] = 99)
    }

    [Fact]
    public void InlineArrayConstantIndex()
    {
        // Option B: [InlineArray(N)] field, constant indexes.
        var bytes = GetProgramBytes(
            """
            Cluster cur = default;
            cur.sprite = 0x10;
            cur.id = 1;
            cur.layout[0] = 5;
            cur.layout[1] = 6;
            cur.layout[2] = 7;
            cur.layout[3] = 8;
            pal_col(0, cur.layout[2]);
            ppu_on_all();
            while (true) ;

            [System.Runtime.CompilerServices.InlineArray(4)]
            struct Bytes4 { private byte _element0; }

            struct Cluster {
                public byte sprite;
                public byte id;
                public Bytes4 layout;
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        // Same layout as the fixed-buffer variant: sprite@$0325, id@$0326, layout[0..3]@$0327..$032A
        Assert.Contains("A9108D2503", hex); // sprite = 0x10
        Assert.Contains("A9018D2603", hex); // id = 1
        Assert.Contains("A9058D2703", hex); // layout[0] = 5
        Assert.Contains("A9068D2803", hex); // layout[1] = 6
        Assert.Contains("A9078D2903", hex); // layout[2] = 7
        Assert.Contains("A9088D2A03", hex); // layout[3] = 8
        Assert.Contains("AD2903", hex);     // LDA $0329 (read layout[2])
    }

    [Fact]
    public void InlineArrayRuntimeIndex()
    {
        // Option B: runtime index into [InlineArray].
        var bytes = GetProgramBytes(
            """
            byte i = 1;
            Cluster cur = default;
            cur.layout[i] = 99;
            ppu_on_all();
            while (true) ;

            [System.Runtime.CompilerServices.InlineArray(4)]
            struct Bytes4 { private byte _element0; }

            struct Cluster {
                public byte sprite;
                public byte id;
                public Bytes4 layout;
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        // Layout: i@$0325, cur.sprite@$0326, cur.id@$0327, cur.layout[0..3]@$0328..$032B
        Assert.Contains("A9018D2503", hex); // i = 1 -> $0325
        Assert.Contains("AE2503", hex);     // LDX $0325 (load index i)
        Assert.Contains("A9639D2803", hex); // LDA #99; STA $0328,X (layout[i] = 99)
    }
}
