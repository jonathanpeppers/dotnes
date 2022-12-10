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
    [InlineData(true)]
    [InlineData(false)]
    public void Write(bool debug)
    {
        var name = debug ? "debug" : "release";
        using var hello_nes = Utilities.GetResource("hello.nes");
        using var hello_dll = Utilities.GetResource($"hello.{name}.dll");
        var expected = new byte[hello_nes.Length];
        hello_nes.Read(expected, 0, expected.Length);

        using var ms = new MemoryStream();
        using var il = new Transpiler(hello_dll);
        il.Write(ms);
        
        AssertEx.Equal(expected, ms.ToArray());
    }
}
