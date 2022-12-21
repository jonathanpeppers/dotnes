using System.Text;

namespace dotnes.tests;

public class TranspilerTests
{
    const string HelloIL =
@"ILInstruction { OpCode = Ldc_i4_0, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col, Bytes =  }
ILInstruction { OpCode = Ldc_i4_1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 20, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col, Bytes =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 32, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col, Bytes =  }
ILInstruction { OpCode = Ldc_i4_3, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 48, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col, Bytes =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = NTADR_A, Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = vram_adr, Bytes =  }
ILInstruction { OpCode = Ldstr, Integer = , String = HELLO, .NET!, Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = vram_write, Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = ppu_on_all, Bytes =  }
ILInstruction { OpCode = Br_s, Integer = 254, String = , Bytes =  }";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadStaticVoidMain_hello(bool debug)
    {
        AssertIL(debug, "hello", HelloIL);
    }

    const string AttributeTableIL =
@"ILInstruction { OpCode = Ldc_i4_s, Integer = 64, String = , Bytes =  }
ILInstruction { OpCode = Newarr, Integer = 16777235, String = , Bytes =  }
ILInstruction { OpCode = Dup, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldtoken, Integer = , String = , Bytes = System.Collections.Immutable.ImmutableArray`1[System.Byte] }
ILInstruction { OpCode = Stloc_0, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 16, String = , Bytes =  }
ILInstruction { OpCode = Newarr, Integer = 16777235, String = , Bytes =  }
ILInstruction { OpCode = Dup, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldtoken, Integer = , String = , Bytes = System.Collections.Immutable.ImmutableArray`1[System.Byte] }
ILInstruction { OpCode = Call, Integer = , String = pal_bg, Bytes =  }
ILInstruction { OpCode = Ldc_i4, Integer = 8192, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = vram_adr, Bytes =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 22, String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4, Integer = 960, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = vram_fill, Bytes =  }
ILInstruction { OpCode = Ldloc_0, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = vram_write, Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = ppu_on_all, Bytes =  }
ILInstruction { OpCode = Br_s, Integer = 254, String = , Bytes =  }";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadStaticVoidMain_attributetable(bool debug)
    {
        AssertIL(debug, "attributetable", AttributeTableIL);
    }

    const string SpritesIL =
@"ILInstruction { OpCode = Ldc_i4_s, Integer = 32, String = , Bytes =  }
ILInstruction { OpCode = Newarr, Integer = 16777235, String = , Bytes =  }
ILInstruction { OpCode = Dup, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldtoken, Integer = , String = , Bytes = System.Collections.Immutable.ImmutableArray`1[System.Byte] }
ILInstruction { OpCode = Stloc_0, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 64, String = , Bytes =  }
ILInstruction { OpCode = Newarr, Integer = 16777235, String = , Bytes =  }
ILInstruction { OpCode = Stloc_1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 64, String = , Bytes =  }
ILInstruction { OpCode = Newarr, Integer = 16777235, String = , Bytes =  }
ILInstruction { OpCode = Stloc_2, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 64, String = , Bytes =  }
ILInstruction { OpCode = Newarr, Integer = 16777235, String = , Bytes =  }
ILInstruction { OpCode = Stloc_3, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 64, String = , Bytes =  }
ILInstruction { OpCode = Newarr, Integer = 16777235, String = , Bytes =  }
ILInstruction { OpCode = Stloc_s, Integer = 4, String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_0, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Stloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Br_s, Integer = 54, String = , Bytes =  }
ILInstruction { OpCode = Ldloc_1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = rand, Bytes =  }
ILInstruction { OpCode = Stelem_i1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_2, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = rand, Bytes =  }
ILInstruction { OpCode = Stelem_i1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_3, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = rand, Bytes =  }
ILInstruction { OpCode = Ldc_i4_7, Integer = , String = , Bytes =  }
ILInstruction { OpCode = And, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_3, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Sub, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Conv_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Stelem_i1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 4, String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = rand, Bytes =  }
ILInstruction { OpCode = Ldc_i4_7, Integer = , String = , Bytes =  }
ILInstruction { OpCode = And, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_3, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Sub, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Conv_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Stelem_i1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Add, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Conv_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Stloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 64, String = , Bytes =  }
ILInstruction { OpCode = Blt_s, Integer = 196, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = oam_clear, Bytes =  }
ILInstruction { OpCode = Ldloc_0, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = pal_all, Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = ppu_on_all, Bytes =  }
ILInstruction { OpCode = Ldc_i4_0, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Stloc_s, Integer = 6, String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_0, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Stloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Br_s, Integer = 63, String = , Bytes =  }
ILInstruction { OpCode = Ldloc_1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldelem_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_2, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldelem_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 6, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = oam_spr, Bytes =  }
ILInstruction { OpCode = Stloc_s, Integer = 6, String = , Bytes =  }
ILInstruction { OpCode = Ldloc_1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldelema, Integer = 16777235, String = , Bytes =  }
ILInstruction { OpCode = Dup, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldind_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_3, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldelem_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Add, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Conv_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Stind_i1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_2, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldelema, Integer = 16777235, String = , Bytes =  }
ILInstruction { OpCode = Dup, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldind_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 4, String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldelem_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Add, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Conv_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Stind_i1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Add, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Conv_u1, Integer = , String = , Bytes =  }
ILInstruction { OpCode = Stloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 5, String = , Bytes =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 64, String = , Bytes =  }
ILInstruction { OpCode = Blt_s, Integer = 187, String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 6, String = , Bytes =  }
ILInstruction { OpCode = Brfalse_s, Integer = 7, String = , Bytes =  }
ILInstruction { OpCode = Ldloc_s, Integer = 6, String = , Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = oam_hide_rest, Bytes =  }
ILInstruction { OpCode = Call, Integer = , String = ppu_wait_frame, Bytes =  }
ILInstruction { OpCode = Br_s, Integer = 161, String = , Bytes =  }";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadStaticVoidMain_sprites(bool debug)
    {
        AssertIL(debug, "sprites", SpritesIL);
    }

    static void AssertIL(bool debug, string name, string expected)
    {
        var suffix = debug ? "debug" : "release";
        var dll = Utilities.GetResource($"{name}.{suffix}.dll");
        var transpiler = new Transpiler(dll);
        var builder = new StringBuilder();
        foreach (var instruction in transpiler.ReadStaticVoidMain())
        {
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(instruction.ToString());
        }

        Assert.Equal(expected, builder.ToString());
    }

    [Theory]
    [InlineData("attributetable", true)]
    [InlineData("attributetable", false)]
    [InlineData("hello", true)]
    [InlineData("hello", false)]
    public void Write(string name, bool debug)
    {
        var configuration = debug ? "debug" : "release";
        using var rom = Utilities.GetResource($"{name}.nes");
        var expected = new byte[rom.Length];
        rom.Read(expected, 0, expected.Length);

        using var dll = Utilities.GetResource($"{name}.{configuration}.dll");
        using var il = new Transpiler(dll);
        using var ms = new MemoryStream();
        il.Write(ms);
        
        AssertEx.Equal(expected, ms.ToArray());
    }
}
