using System.Text;

namespace dotnes.tests;

public class TranspilerTests
{
    static readonly Dictionary<string, string> iltext = new()
    {
        { "hello",
@"ILInstruction { OpCode = Ldc_i4_0, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::pal_col }
ILInstruction { OpCode = Ldc_i4_1, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 20, String =  }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::pal_col }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 32, String =  }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::pal_col }
ILInstruction { OpCode = Ldc_i4_3, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_s, Integer = 48, String =  }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::pal_col }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::NTADR_A }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::vram_adr }
ILInstruction { OpCode = Ldstr, Integer = , String = HELLO, .NET! }
ILInstruction { OpCode = Ldc_i4_s, Integer = 13, String =  }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::vram_write }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::ppu_on_all }
ILInstruction { OpCode = Br_s, Integer = 254, String =  }"
        },
        { "hello.sub",
@"ILInstruction { OpCode = Call, Integer = , String = Program::<<Main>$>g__setup_pallette|0_0 }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Ldc_i4_2, Integer = , String =  }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::NTADR_A }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::vram_adr }
ILInstruction { OpCode = Ldstr, Integer = , String = HELLO, .NET! }
ILInstruction { OpCode = Ldc_i4_s, Integer = 13, String =  }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::vram_write }
ILInstruction { OpCode = Call, Integer = , String = NES.NESLib::ppu_on_all }
ILInstruction { OpCode = Br_s, Integer = 254, String =  }"
        }
    };

    [Theory]
    [InlineData("hello.debug.dll", "hello")]
    [InlineData("hello.release.dll", "hello")]
    [InlineData("hello.sub.dll", "hello.sub")]
    public void ReadStaticVoidMain(string dll, string key)
    {
        using var dll_stream = Utilities.GetResource(dll);
        using var il = new Transpiler(dll_stream);
        var builder = new StringBuilder();
        foreach (var instruction in il.ReadStaticVoidMain())
        {
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(instruction.ToString());
        }

        Assert.Equal(iltext[key], builder.ToString());
    }

    [Theory]
    [InlineData("hello.debug.dll", "hello.nes")]
    [InlineData("hello.release.dll", "hello.nes")]
    [InlineData("hello.debug.dll", "hello.sub.nes")]
    [InlineData("hello.sub.dll", "hello.sub.nes")]
    public void Write(string dll, string rom)
    {
        using var rom_stream = Utilities.GetResource(rom);
        using var dll_stream = Utilities.GetResource(dll);
        var expected = new byte[rom_stream.Length];
        rom_stream.Read(expected, 0, expected.Length);

        using var ms = new MemoryStream();
        using var il = new Transpiler(dll_stream);
        il.Write(ms);

        AssertEx.Equal(expected, ms.ToArray());
    }
}
