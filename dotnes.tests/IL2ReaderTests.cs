using System.Text;

namespace dotnes.tests;

public class IL2ReaderTests
{
    readonly string path;

    public IL2ReaderTests()
    {
        var dir = Path.GetDirectoryName(GetType().Assembly.Location)!;
        path = Path.Combine(dir, "dotnes.sample.dll");
    }

    [Fact]
    public void StaticVoidMain()
    {
        using var il = new ILReader(path);
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
ILInstruction { OpCode = Ldstr, Integer = , String = HELLO, WORLD! }
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
}
