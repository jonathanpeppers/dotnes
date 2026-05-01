using dotnes.ObjectModel;

namespace dotnes.tests;

/// <summary>
/// Tests for <see cref="LocalVariableManager"/> — the pure state management class
/// for local variable allocation, struct field tracking, and static field addresses.
/// These tests verify the allocation logic in isolation without requiring 6502 code emission.
/// </summary>
public class LocalVariableManagerTests
{
    [Fact]
    public void GetOrAllocateStructLocal_AllocatesNewStruct()
    {
        var manager = new LocalVariableManager();
        manager.StructLayouts["MyStruct"] = new List<(string Name, int Size)>
        {
            ("X", 1),
            ("Y", 1),
        };

        ushort addr = manager.GetOrAllocateStructLocal(0, "MyStruct");

        Assert.Equal(NESConstants.LocalStackBase, addr);
        Assert.Equal(2, manager.LocalCount); // 2 bytes allocated
        Assert.True(manager.Locals.ContainsKey(0));
        Assert.Equal((int)addr, manager.Locals[0].Address);
    }

    [Fact]
    public void GetOrAllocateStructLocal_ReturnsCachedAddress()
    {
        var manager = new LocalVariableManager();
        manager.StructLayouts["MyStruct"] = new List<(string Name, int Size)>
        {
            ("X", 1),
            ("Y", 1),
        };

        ushort addr1 = manager.GetOrAllocateStructLocal(0, "MyStruct");
        ushort addr2 = manager.GetOrAllocateStructLocal(0, "MyStruct");

        Assert.Equal(addr1, addr2);
        Assert.Equal(2, manager.LocalCount); // Only allocated once
    }

    [Fact]
    public void GetFieldOffset_ReturnsCorrectOffset()
    {
        var manager = new LocalVariableManager();
        manager.StructLayouts["Vec2"] = new List<(string Name, int Size)>
        {
            ("X", 1),
            ("Y", 1),
            ("Z", 2),
        };

        Assert.Equal(0, manager.GetFieldOffset("Vec2", "X"));
        Assert.Equal(1, manager.GetFieldOffset("Vec2", "Y"));
        Assert.Equal(2, manager.GetFieldOffset("Vec2", "Z"));
    }

    [Fact]
    public void GetFieldOffset_ThrowsForUnknownStruct()
    {
        var manager = new LocalVariableManager();

        Assert.Throws<InvalidOperationException>(() =>
            manager.GetFieldOffset("Unknown", "X"));
    }

    [Fact]
    public void GetFieldOffset_ThrowsForUnknownField()
    {
        var manager = new LocalVariableManager();
        manager.StructLayouts["MyStruct"] = new List<(string Name, int Size)>
        {
            ("X", 1),
        };

        Assert.Throws<InvalidOperationException>(() =>
            manager.GetFieldOffset("MyStruct", "Missing"));
    }

    [Fact]
    public void GetStructSize_ReturnsCorrectSize()
    {
        var manager = new LocalVariableManager();
        manager.StructLayouts["Small"] = new List<(string Name, int Size)>
        {
            ("A", 1),
            ("B", 2),
        };

        Assert.Equal(3, manager.GetStructSize("Small"));
    }

    [Fact]
    public void GetStructSize_ReturnsMinimumOfOne()
    {
        var manager = new LocalVariableManager();
        manager.StructLayouts["Empty"] = new List<(string Name, int Size)>();

        Assert.Equal(1, manager.GetStructSize("Empty"));
    }

    [Fact]
    public void ResolveStructType_FindsByFieldName()
    {
        var manager = new LocalVariableManager();
        manager.StructLayouts["Vec2"] = new List<(string Name, int Size)>
        {
            ("X", 1),
            ("Y", 1),
        };

        string resolved = manager.ResolveStructType(0, "X");

        Assert.Equal("Vec2", resolved);
        Assert.True(manager.IsStructLocal(0));
    }

    [Fact]
    public void ResolveStructType_UsesKnownType()
    {
        var manager = new LocalVariableManager();
        manager.StructLayouts["Vec2"] = new List<(string Name, int Size)>
        {
            ("X", 1),
        };
        manager.SetStructType(0, "Vec2");

        string resolved = manager.ResolveStructType(0, "X");

        Assert.Equal("Vec2", resolved);
    }

    [Fact]
    public void ResolveStructType_ThrowsForUnknownField()
    {
        var manager = new LocalVariableManager();

        Assert.Throws<TranspileException>(() =>
            manager.ResolveStructType(0, "Missing"));
    }

    [Fact]
    public void GetOrAllocateStaticField_AllocatesSequentially()
    {
        var manager = new LocalVariableManager();

        ushort addr1 = manager.GetOrAllocateStaticField("field1");
        ushort addr2 = manager.GetOrAllocateStaticField("field2");

        Assert.Equal(NESConstants.LocalStackBase, addr1);
        Assert.Equal(NESConstants.LocalStackBase + 1, addr2);
        Assert.Equal(2, manager.LocalCount);
    }

    [Fact]
    public void GetOrAllocateStaticField_ReturnsCachedAddress()
    {
        var manager = new LocalVariableManager();

        ushort addr1 = manager.GetOrAllocateStaticField("field1");
        ushort addr2 = manager.GetOrAllocateStaticField("field1");

        Assert.Equal(addr1, addr2);
        Assert.Equal(1, manager.LocalCount); // Only allocated once
    }

    [Fact]
    public void NextAddress_ComputedCorrectly()
    {
        var manager = new LocalVariableManager();

        Assert.Equal(NESConstants.LocalStackBase, manager.NextAddress);

        manager.LocalCount = 5;

        Assert.Equal((ushort)(NESConstants.LocalStackBase + 5), manager.NextAddress);
    }

    [Fact]
    public void MultipleAllocations_DontOverlap()
    {
        var manager = new LocalVariableManager();
        manager.StructLayouts["Vec2"] = new List<(string Name, int Size)>
        {
            ("X", 1),
            ("Y", 1),
        };

        // Allocate struct local
        ushort structAddr = manager.GetOrAllocateStructLocal(0, "Vec2");

        // Allocate static field
        ushort staticAddr = manager.GetOrAllocateStaticField("score");

        // They should not overlap
        Assert.Equal(NESConstants.LocalStackBase, structAddr);
        Assert.Equal(NESConstants.LocalStackBase + 2, staticAddr); // struct took 2 bytes
        Assert.Equal(3, manager.LocalCount); // 2 for struct + 1 for static
    }

    [Fact]
    public void IsStructLocal_ReturnsFalseForUnknown()
    {
        var manager = new LocalVariableManager();

        Assert.False(manager.IsStructLocal(42));
    }

    [Fact]
    public void GetStructType_ReturnsNullForUnknown()
    {
        var manager = new LocalVariableManager();

        Assert.Null(manager.GetStructType(42));
    }

    [Fact]
    public void SetStructType_StoresAndRetrieves()
    {
        var manager = new LocalVariableManager();

        manager.SetStructType(1, "Position");

        Assert.Equal("Position", manager.GetStructType(1));
        Assert.True(manager.IsStructLocal(1));
    }

    [Fact]
    public void LocalCount_AtMaxLocalBytes_Succeeds()
    {
        var manager = new LocalVariableManager();
        manager.LocalCount = NESConstants.MaxLocalBytes;
        Assert.Equal(NESConstants.MaxLocalBytes, manager.LocalCount);
    }

    [Fact]
    public void LocalCount_ExceedsMaxLocalBytes_Throws()
    {
        var manager = new LocalVariableManager();
        var ex = Assert.Throws<TranspileException>(() =>
            manager.LocalCount = NESConstants.MaxLocalBytes + 1);
        Assert.Contains("NES RAM", ex.Message);
    }
}
