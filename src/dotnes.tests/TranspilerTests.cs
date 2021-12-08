using System.Text;

namespace dotnes.tests;

public class TranspilerTests
{
    [Fact]
    public void ReadStaticVoidMain()
    {
        using var hello_dll = GetType().Assembly.GetManifestResourceStream("hello.dll");
        if (hello_dll == null)
            throw new Exception("Cannot load hello.dll!");
        using var il = new Transpiler(hello_dll);
        var builder = new StringBuilder();
        foreach (var instruction in il.ReadStaticVoidMain())
        {
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(instruction.ToString());
        }

        Assert.Equal(
@"ILInstruction { OpCode = Ldc_i4_0, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col }
ILInstruction { OpCode = Nop, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_1, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 20, String =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col }
ILInstruction { OpCode = Nop, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 32, String =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col }
ILInstruction { OpCode = Nop, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_3, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 48, String =  }
ILInstruction { OpCode = Call, Integer = , String = pal_col }
ILInstruction { OpCode = Nop, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Call, Integer = , String = NTADR_A }
ILInstruction { OpCode = Call, Integer = , String = vram_adr }
ILInstruction { OpCode = Nop, Integer = , String =  }
ILInstruction { OpCode = Ldstr, Integer = , String = HELLO, .NET! }
ILInstruction { OpCode = Ldc_i4_s, Integer = 13, String =  }
ILInstruction { OpCode = Call, Integer = , String = vram_write }
ILInstruction { OpCode = Nop, Integer = , String =  }
ILInstruction { OpCode = Call, Integer = , String = ppu_on_all }
ILInstruction { OpCode = Nop, Integer = , String =  }
ILInstruction { OpCode = Br_s, Integer = 1, String =  }
ILInstruction { OpCode = Nop, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_1, Integer = , String =  }
ILInstruction { OpCode = Stloc_0, Integer = , String =  }
ILInstruction { OpCode = Br_s, Integer = 251, String =  }", builder.ToString());
    }

    [Fact]
    public void Write()
    {
        using var hello_nes = GetType().Assembly.GetManifestResourceStream("hello.nes");
        if (hello_nes == null)
            throw new Exception("Cannot load hello.nes!");
        using var hello_dll = GetType().Assembly.GetManifestResourceStream("hello.dll");
        if (hello_dll == null)
            throw new Exception("Cannot load hello.dll!");
        var expected = new byte[hello_nes.Length];
        hello_nes.Read(expected, 0, expected.Length);

        using var ms = new MemoryStream();
        using var il = new Transpiler(hello_dll);
        il.Write(ms);
        
        AssertEx.Equal(expected, ms.ToArray());
    }
}
