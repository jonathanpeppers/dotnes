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
        // Stores of the four constant values, written to successive absolute RAM addresses
        // (struct buffer base + 0..3). These are absolute addressing in RAM, not zero-page.
        Assert.Contains("A910", hex); // LDA #$10 (sprite)
        Assert.Contains("A905", hex); // LDA #$05
        Assert.Contains("A906", hex); // LDA #$06
        Assert.Contains("A907", hex); // LDA #$07
        Assert.Contains("A908", hex); // LDA #$08
        // Absolute store byte (8D = STA abs) writing into the struct's allocated RAM (e.g. $0325+).
        Assert.Contains("8D", hex);
        // Absolute load byte (AD = LDA abs) for the read
        Assert.Contains("AD", hex);
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
        Assert.Contains("A963", hex); // LDA #$63 (99)
        // STA absolute,X (9D) for AbsoluteX store
        Assert.Contains("9D", hex);
        // LDX absolute (AE) to load the runtime index
        Assert.Contains("AE", hex);
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
        Assert.Contains("A910", hex);
        Assert.Contains("A905", hex);
        Assert.Contains("A906", hex);
        Assert.Contains("A907", hex);
        Assert.Contains("A908", hex);
        Assert.Contains("8D", hex);
        Assert.Contains("AD", hex);
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
        Assert.Contains("A963", hex); // LDA #$63 (99)
        Assert.Contains("9D", hex);   // STA abs,X
        Assert.Contains("AE", hex);   // LDX abs (runtime index)
    }
}
