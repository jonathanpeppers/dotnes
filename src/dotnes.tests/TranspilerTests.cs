using System.Text;

namespace dotnes.tests;

public class TranspilerTests
{
    const string HelloIL =
@"ILInstruction { OpCode = Ldc_i4_0, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col }
ILInstruction { OpCode = Ldc_i4_1, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 20, String =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 32, String =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col }
ILInstruction { OpCode = Ldc_i4_3, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 48, String =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Call, Integer = , String = NTADR_A }
ILInstruction { OpCode = Call, Integer = , String = vram_adr }
ILInstruction { OpCode = Ldstr, Integer = , String = HELLO, .NET! }
ILInstruction { OpCode = Call, Integer = , String = vram_write }
ILInstruction { OpCode = Call, Integer = , String = ppu_on_all }
ILInstruction { OpCode = Br_s, Integer = 254, String =  }";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadStaticVoidMain(bool debug)
    {
        var name = debug ? "debug" : "release";
        using var hello_dll = Utilities.GetResource($"hello.{name}.dll");
        using var il = new Transpiler(hello_dll);
        var builder = new StringBuilder();
        foreach (var instruction in il.ReadStaticVoidMain())
        {
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(instruction.ToString());
        }

        Assert.Equal(HelloIL, builder.ToString());
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
