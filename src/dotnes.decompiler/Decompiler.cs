using System.Text;
using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// Decompiles a .nes ROM file back to C# source code that can be
/// transpiled through dotnes to produce an equivalent ROM.
/// </summary>
class Decompiler
{
    readonly NESRomReader _rom;
    readonly ILogger _logger;
    readonly Dictionary<ushort, string> _symbolTable = new();

    ushort _mainAddress;
    int _dataCounter;

    public Decompiler(NESRomReader rom, ILogger? logger = null)
    {
        _rom = rom;
        _logger = logger ?? new NullLogger();
    }

    /// <summary>
    /// Decompile the ROM and return C# source code.
    /// </summary>
    public string Decompile()
    {
        _logger.WriteLine($"Decompiling ROM: PRG={_rom.PrgBanks} banks, CHR={_rom.ChrBanks} banks, Mapper={_rom.Mapper}");
        _logger.WriteLine($"Vectors: NMI=${_rom.NmiVector:X4}, RESET=${_rom.ResetVector:X4}, IRQ=${_rom.IrqVector:X4}");

        // Phase 1: Build symbol table by assembling the known built-in subroutines
        // and reading addresses from the reference program layout
        BuildSymbolTable();

        // Phase 2: Find and decompile the main program
        var mainCode = DecompileMain();

        // Phase 3: Generate C# source
        return GenerateCSharp(mainCode);
    }

    /// <summary>
    /// Build a symbol table by assembling the known built-in subroutines at $8000,
    /// finding the main address from the initlib JSR target, then scanning the
    /// final built-ins after main to identify runtime support subroutines.
    /// </summary>
    void BuildSymbolTable()
    {
        _logger.WriteLine($"Building symbol table from built-in subroutines...");

        // Build the initial built-in subroutines to get their addresses
        var program = Program6502.CreateWithBuiltIns();
        program.ResolveAddresses();

        var labels = program.GetLabels();
        ushort builtInsEnd = (ushort)(0x8000 + program.TotalSize);

        // Map all initial built-in labels to addresses
        foreach (var kvp in labels)
        {
            if (kvp.Value != 0)
                _symbolTable[kvp.Value] = kvp.Key;
        }

        _logger.WriteLine($"Initial built-ins end at ${builtInsEnd:X4}");

        // Find main address by looking at the initlib block's JSR target in the actual ROM.
        // Initlib contains a JSR to "main" which is the first JSR targeting an address >= builtInsEnd.
        if (labels.TryGetValue("initlib", out ushort initlibAddr))
        {
            int initlibOffset = initlibAddr - 0x8000;
            for (int i = initlibOffset; i < _rom.PrgRom.Length - 2; i++)
            {
                if (_rom.PrgRom[i] == 0x60) break; // RTS = end of initlib
                if (_rom.PrgRom[i] == 0x20) // JSR
                {
                    ushort target = (ushort)(_rom.PrgRom[i + 1] | (_rom.PrgRom[i + 2] << 8));
                    if (target >= builtInsEnd && target < 0xFFFA)
                    {
                        _mainAddress = target;
                        _symbolTable[target] = "main";
                        _logger.WriteLine($"Found main at ${_mainAddress:X4}");
                        break;
                    }
                }
            }
        }

        if (_mainAddress == 0)
        {
            _mainAddress = builtInsEnd;
            _symbolTable[_mainAddress] = "main";
            _logger.WriteLine($"Main address (fallback) at ${_mainAddress:X4}");
        }

        // Scan for the end of main (JMP to self = infinite loop, or first RTS after main)
        ushort mainEnd = FindMainEnd();

        // Identify final built-ins after main by scanning JSR targets from within main
        // and matching byte patterns at those addresses
        IdentifyFinalBuiltIns(mainEnd);
    }

    /// <summary>
    /// Find the end of the main function by scanning for the infinite loop pattern (JMP to self).
    /// </summary>
    ushort FindMainEnd()
    {
        int offset = _mainAddress - 0x8000;
        ushort address = _mainAddress;

        while (offset < _rom.PrgRom.Length - 2)
        {
            byte opcode = _rom.PrgRom[offset];

            // JMP to self = while(true);
            if (opcode == 0x4C)
            {
                ushort target = (ushort)(_rom.PrgRom[offset + 1] | (_rom.PrgRom[offset + 2] << 8));
                if (target == address)
                    return (ushort)(address + 3);
            }

            int size = GetInstructionSize(opcode);
            offset += size;
            address += (ushort)size;
        }

        return address;
    }

    /// <summary>
    /// Identify final built-in subroutines (popa, pusha, popax, etc.) that appear after main.
    /// Uses byte-pattern matching against the first few bytes of known subroutines.
    /// </summary>
    void IdentifyFinalBuiltIns(ushort searchStart)
    {
        // Collect all JSR targets from the entire main block so we know what addresses to identify
        var jsrTargets = new HashSet<ushort>();
        int offset = _mainAddress - 0x8000;
        ushort address = _mainAddress;

        // Scan main for JSR targets
        while (address < searchStart && offset < _rom.PrgRom.Length - 2)
        {
            if (_rom.PrgRom[offset] == 0x20) // JSR
            {
                ushort target = (ushort)(_rom.PrgRom[offset + 1] | (_rom.PrgRom[offset + 2] << 8));
                if (!_symbolTable.ContainsKey(target))
                    jsrTargets.Add(target);
            }
            int size = GetInstructionSize(_rom.PrgRom[offset]);
            offset += size;
            address += (ushort)size;
        }

        // Also scan the area after main for JSR targets (final built-ins may call each other)
        offset = searchStart - 0x8000;
        address = searchStart;
        int scanEnd = Math.Min(_rom.PrgRom.Length, (_rom.PrgBanks * 16384) - 6);
        while (offset < scanEnd - 2)
        {
            if (_rom.PrgRom[offset] == 0x20) // JSR
            {
                ushort target = (ushort)(_rom.PrgRom[offset + 1] | (_rom.PrgRom[offset + 2] << 8));
                if (!_symbolTable.ContainsKey(target))
                    jsrTargets.Add(target);
            }
            byte opcode = _rom.PrgRom[offset];
            int size = GetInstructionSize(opcode);
            offset += size;
            address += (ushort)size;
        }

        // Known byte-pattern signatures for final built-in subroutines.
        // These patterns match the first few bytes of the 6502 code emitted by
        // BuiltInSubroutines.cs. If that file changes, these patterns must be updated.
        var patterns = new (string Name, byte[] Signature)[]
        {
            // pusha: LDY $22; BEQ ...  (A4 22 F0)
            ("pusha", new byte[] { 0xA4, 0x22, 0xF0 }),
            // pushax: PHA; LDA $22; SEC; SBC #$02  (48 A5 22 38 E9 02)
            ("pushax", new byte[] { 0x48, 0xA5, 0x22, 0x38, 0xE9, 0x02 }),
            // popa: LDY #$00; LDA ($22),Y; INC $22  (A0 00 B1 22 E6 22)
            ("popa", new byte[] { 0xA0, 0x00, 0xB1, 0x22, 0xE6, 0x22 }),
            // popax: TAX; DEY; LDA ($22),Y; INC $22  (AA 88 B1 22 E6 22)
            ("popax", new byte[] { 0xAA, 0x88, 0xB1, 0x22, 0xE6, 0x22 }),
            // incsp2: ... INC $22; BNE (standard cc65 incsp2 pattern)
            ("incsp2", new byte[] { 0xA0, 0x01, 0xB1, 0x22 }),
            // incsp1: LDY #$00; BEQ (cc65 tail-share with incsp2)
            ("incsp1", new byte[] { 0xA0, 0x00, 0xF0 }),
            // vram_write subroutine starting with ptr1/ptr2 setup
            ("vram_write", new byte[] { 0x85, 0x2A, 0x86, 0x2B }),
        };

        // Try to match each JSR target against known patterns
        foreach (ushort target in jsrTargets)
        {
            if (_symbolTable.ContainsKey(target))
                continue;

            int targetOffset = target - 0x8000;
            if (targetOffset < 0 || targetOffset >= _rom.PrgRom.Length - 6)
                continue;

            // Try matching at the target address
            foreach (var (name, sig) in patterns)
            {
                if (_symbolTable.ContainsValue(name))
                    continue;

                if (MatchesPattern(targetOffset, sig))
                {
                    _symbolTable[target] = name;
                    _logger.WriteLine($"  Found {name} at ${target:X4}");
                    break;
                }
            }
        }
    }

    bool MatchesPattern(int offset, byte[] pattern)
    {
        if (offset + pattern.Length > _rom.PrgRom.Length)
            return false;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (_rom.PrgRom[offset + i] != pattern[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Decompile the main program block into a list of C# statements.
    /// Recognizes multi-instruction patterns like LDA #arg / JSR pusha / LDA #arg / JSR pal_col.
    /// </summary>
    List<string> DecompileMain()
    {
        var statements = new List<string>();
        int offset = _mainAddress - 0x8000;
        ushort address = _mainAddress;

        _logger.WriteLine($"Decompiling main at ${address:X4} (offset 0x{offset:X4})...");

        // Collect a flat list of decoded instructions first
        var instructions = new List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)>();
        int tempOffset = offset;
        ushort tempAddr = address;

        while (tempOffset < _rom.PrgRom.Length)
        {
            byte opcode = _rom.PrgRom[tempOffset];
            int size = GetInstructionSize(opcode);

            byte? op1 = size >= 2 && tempOffset + 1 < _rom.PrgRom.Length ? _rom.PrgRom[tempOffset + 1] : null;
            byte? op2 = size >= 3 && tempOffset + 2 < _rom.PrgRom.Length ? _rom.PrgRom[tempOffset + 2] : null;

            instructions.Add((tempAddr, opcode, op1, op2));

            // Stop at JMP to self (while(true);)
            if (opcode == 0x4C && op1.HasValue && op2.HasValue)
            {
                ushort target = (ushort)(op1.Value | (op2.Value << 8));
                if (target == tempAddr) break;
            }

            tempOffset += size;
            tempAddr += (ushort)size;
        }

        // Now walk through instructions with look-ahead for pattern matching
        var pushedBytes = new Stack<byte>();   // Track values pushed via pusha (8-bit)
        var pushedWords = new Stack<ushort>(); // Track values pushed via pushax (16-bit)

        for (int i = 0; i < instructions.Count; i++)
        {
            var (instrAddr, opcode, op1, op2) = instructions[i];

            // Pattern: JMP to self = while (true) ;
            if (opcode == 0x4C && op1.HasValue && op2.HasValue)
            {
                ushort target = (ushort)(op1.Value | (op2.Value << 8));
                if (target == instrAddr)
                {
                    statements.Add("while (true) ;");
                    break;
                }
            }

            // Pattern: LDA #imm / JSR pusha → push 8-bit arg for a multi-arg call
            if (opcode == 0xA9 && op1.HasValue && i + 1 < instructions.Count)
            {
                var next = instructions[i + 1];
                if (next.Opcode == 0x20 && IsSubroutine(next, "pusha"))
                {
                    pushedBytes.Push(op1.Value);
                    i++; // skip the JSR pusha
                    continue;
                }
            }

            // Pattern: LDA #lo / LDX #hi / JSR pushax → push 16-bit pointer
            // Pattern: LDA #lo / LDX #hi / JSR <fastcall subroutine> → call with pointer in A:X
            if (opcode == 0xA9 && op1.HasValue && i + 2 < instructions.Count)
            {
                var nextLdx = instructions[i + 1];
                var nextJsr = instructions[i + 2];
                if (nextLdx.Opcode == 0xA2 && nextLdx.Op1.HasValue
                    && nextJsr.Opcode == 0x20 && nextJsr.Op1.HasValue && nextJsr.Op2.HasValue)
                {
                    ushort jsrTarget = (ushort)(nextJsr.Op1.Value | (nextJsr.Op2.Value << 8));
                    if (_symbolTable.TryGetValue(jsrTarget, out var targetName))
                    {
                        if (targetName == "pushax")
                        {
                            ushort value16 = (ushort)(op1.Value | (nextLdx.Op1.Value << 8));
                            pushedWords.Push(value16);
                            i += 2; // skip LDX and JSR pushax
                            continue;
                        }
                        if (targetName != "pusha")
                        {
                            // Fastcall: pointer in A:X (LDA=lo, LDX=hi)
                            byte aVal = op1.Value;
                            byte xVal = nextLdx.Op1.Value;
                            byte? pb = pushedBytes.Count > 0 ? pushedBytes.Pop() : null;
                            ushort? pw = pushedWords.Count > 0 ? pushedWords.Pop() : null;
                            var stmts = DecompileCallXA(targetName, xVal, aVal, pw, pb);
                            statements.AddRange(stmts);
                            i += 2;
                            continue;
                        }
                    }
                }
            }

            // Pattern: LDX #hi / LDA #lo / JSR <subroutine> → call with 16-bit arg in X:A
            if (opcode == 0xA2 && op1.HasValue && i + 2 < instructions.Count)
            {
                var nextLda = instructions[i + 1];
                var nextJsr = instructions[i + 2];
                if (nextLda.Opcode == 0xA9 && nextLda.Op1.HasValue
                    && nextJsr.Opcode == 0x20 && nextJsr.Op1.HasValue && nextJsr.Op2.HasValue)
                {
                    ushort jsrTarget = (ushort)(nextJsr.Op1.Value | (nextJsr.Op2.Value << 8));
                    if (_symbolTable.TryGetValue(jsrTarget, out var name) && name != "pusha" && name != "pushax")
                    {
                        byte xVal = op1.Value;
                        byte aVal = nextLda.Op1.Value;
                        ushort? pw = pushedWords.Count > 0 ? pushedWords.Pop() : null;
                        byte? pb = pushedBytes.Count > 0 ? pushedBytes.Pop() : null;
                        var stmts = DecompileCallXA(name, xVal, aVal, pw, pb);
                        statements.AddRange(stmts);
                        i += 2; // skip LDA and JSR
                        continue;
                    }
                }
            }

            // Pattern: LDA #imm / JSR <subroutine> → call with immediate arg in A
            if (opcode == 0xA9 && op1.HasValue && i + 1 < instructions.Count)
            {
                var next = instructions[i + 1];
                if (next.Opcode == 0x20 && next.Op1.HasValue && next.Op2.HasValue)
                {
                    ushort jsrTarget = (ushort)(next.Op1.Value | (next.Op2.Value << 8));
                    if (_symbolTable.TryGetValue(jsrTarget, out var name))
                    {
                        byte? pushedArg = pushedBytes.Count > 0 ? pushedBytes.Pop() : null;
                        var stmt = DecompileCall(name, op1.Value, pushedArg);
                        if (stmt != null) statements.Add(stmt);
                        i++; // skip the JSR
                        continue;
                    }
                }
            }

            // Pattern: plain JSR (no preceding LDA/LDX)
            if (opcode == 0x20 && op1.HasValue && op2.HasValue)
            {
                ushort jsrTarget = (ushort)(op1.Value | (op2.Value << 8));
                if (_symbolTable.TryGetValue(jsrTarget, out var name))
                {
                    var stmt = DecompileCall(name, null, null);
                    if (stmt != null) statements.Add(stmt);
                }
                continue;
            }
        }

        // NES programs must end with an infinite loop; ensure one is present
        if (statements.Count == 0 || statements[^1] != "while (true) ;")
            statements.Add("while (true) ;");

        return statements;
    }

    bool IsSubroutine((ushort Address, byte Opcode, byte? Op1, byte? Op2) instr, string name)
    {
        if (instr.Opcode != 0x20 || !instr.Op1.HasValue || !instr.Op2.HasValue) return false;
        ushort target = (ushort)(instr.Op1.Value | (instr.Op2.Value << 8));
        return _symbolTable.TryGetValue(target, out var n) && n == name;
    }

    /// <summary>
    /// Decompile a call where A contains a value (the last argument),
    /// and optionally a pushed value was the first argument.
    /// </summary>
    static string? DecompileCall(string name, byte? aValue, byte? pushedArg = null)
    {
        return name switch
        {
            "pal_col" => $"pal_col({pushedArg?.ToString() ?? "0"}, 0x{aValue ?? 0:X2});",
            "pal_bright" => $"pal_bright({aValue?.ToString() ?? "4"});",
            "pal_spr_bright" => $"pal_spr_bright({aValue?.ToString() ?? "4"});",
            "pal_bg_bright" => $"pal_bg_bright({aValue?.ToString() ?? "4"});",
            "ppu_on_all" => "ppu_on_all();",
            "ppu_on_bg" => "ppu_on_bg();",
            "ppu_on_spr" => "ppu_on_spr();",
            "ppu_off" => "ppu_off();",
            "ppu_wait_nmi" => "ppu_wait_nmi();",
            "ppu_wait_frame" => "ppu_wait_frame();",
            "ppu_mask" => $"ppu_mask(0x{aValue ?? 0:X2});",
            "oam_clear" => "oam_clear();",
            "oam_size" => $"oam_size({aValue?.ToString() ?? "0"});",
            "oam_hide_rest" => $"oam_hide_rest({aValue?.ToString() ?? "0"});",
            "vram_put" => $"vram_put(0x{aValue ?? 0:X2});",
            "vram_inc" => $"vram_inc(0x{aValue ?? 0:X2});",
            "bank_spr" => $"bank_spr({aValue?.ToString() ?? "0"});",
            "bank_bg" => $"bank_bg({aValue?.ToString() ?? "0"});",
            "delay" => $"delay({aValue?.ToString() ?? "1"});",
            "rand8" or "rand" => "rand8();",
            "set_rand" => $"set_rand({aValue?.ToString() ?? "0"});",
            "pad_poll" => $"pad_poll({aValue?.ToString() ?? "0"});",
            "waitvsync" => "waitvsync();",
            "pal_clear" => "pal_clear();",
            "nesclock" => "nesclock();",
            // Internal runtime support - skip
            "pusha" or "pushax" or "popa" or "popax"
                or "incsp1" or "incsp2" or "addysp" or "decsp4"
                or "zerobss" or "copydata" or "initlib" or "donelib" => null,
            _ => $"// {name}({(aValue.HasValue ? $"0x{aValue:X2}" : "")});",
        };
    }

    /// <summary>
    /// Decompile a call where X:A contain a 16-bit value (hi:lo).
    /// Used for vram_adr(NTADR_A(x,y)), vram_write(data), pal_bg/pal_spr, vram_fill, etc.
    /// Returns a list of statements (may include byte array declarations before the call).
    /// </summary>
    List<string> DecompileCallXA(string name, byte xValue, byte aValue, ushort? pushedPtr, byte? pushedByte = null)
    {
        ushort value16 = (ushort)((xValue << 8) | aValue);

        return name switch
        {
            "vram_adr" => new List<string> { FormatVramAdr(aValue, xValue) },
            "vram_write" => FormatVramWrite(value16, pushedPtr),
            "pal_bg" => FormatPaletteCall("pal_bg", "paletteBg", value16, 16),
            "pal_spr" => FormatPaletteCall("pal_spr", "paletteSpr", value16, 16),
            "pal_all" => FormatPaletteCall("pal_all", "paletteAll", value16, 32),
            "vram_fill" => new List<string> { $"vram_fill(0x{pushedByte ?? 0:X2}, {value16});" },
            "scroll" => new List<string> { $"scroll({aValue}, {xValue});" },
            // Internal runtime support - skip
            "pusha" or "pushax" or "popa" or "popax"
                or "incsp1" or "incsp2" or "addysp" or "decsp4"
                or "zerobss" or "copydata" or "initlib" or "donelib" => new List<string>(),
            _ => new List<string> { $"// {name}(0x{value16:X4});" },
        };
    }

    /// <summary>
    /// Format vram_write call. If a data pointer was pushed, try to read the data
    /// from the ROM. For printable ASCII, emit a string literal; otherwise emit a byte array.
    /// </summary>
    List<string> FormatVramWrite(ushort length, ushort? dataPointer)
    {
        if (dataPointer.HasValue && length > 0)
        {
            int offset = dataPointer.Value - 0x8000;
            if (offset >= 0 && offset + length <= _rom.PrgRom.Length)
            {
                // Try string first (all printable ASCII)
                bool allPrintable = true;
                for (int i = 0; i < length; i++)
                {
                    byte b = _rom.PrgRom[offset + i];
                    if (b < 0x20 || b >= 0x7F) { allPrintable = false; break; }
                }
                if (allPrintable)
                {
                    var chars = new char[length];
                    for (int i = 0; i < length; i++)
                        chars[i] = (char)_rom.PrgRom[offset + i];
                    string escaped = new string(chars).Replace("\\", "\\\\").Replace("\"", "\\\"");
                    return new List<string> { $"vram_write(\"{escaped}\");" };
                }

                // Non-ASCII: emit byte array variable + call
                string varName = $"data{_dataCounter++}";
                return new List<string>
                {
                    FormatByteArrayDeclaration(offset, length, varName),
                    $"vram_write({varName});"
                };
            }
        }
        return new List<string> { $"vram_write(/* {length} bytes */);" };
    }

    /// <summary>
    /// Format a vram_adr call, trying to reconstruct NTADR_A macro usage.
    /// </summary>
    static string FormatVramAdr(byte lo, byte hi)
    {
        ushort addr = (ushort)((hi << 8) | lo);
        // Try to decompose as NTADR_A(x, y) = 0x2000 + y*32 + x
        if (addr >= 0x2000 && addr < 0x2400)
        {
            int offset = addr - 0x2000;
            int y = offset / 32;
            int x = offset % 32;
            return $"vram_adr(NTADR_A({x}, {y}));";
        }
        return $"vram_adr(0x{addr:X4});";
    }

    /// <summary>
    /// Format a palette call (pal_bg, pal_spr, pal_all) by extracting palette data from ROM.
    /// </summary>
    List<string> FormatPaletteCall(string funcName, string varName, ushort pointer, int size)
    {
        int offset = pointer - 0x8000;
        if (offset >= 0 && offset + size <= _rom.PrgRom.Length)
        {
            return new List<string>
            {
                FormatByteArrayDeclaration(offset, size, varName),
                $"{funcName}({varName});"
            };
        }
        return new List<string> { $"// {funcName}(/* data at ${pointer:X4} */);" };
    }

    /// <summary>
    /// Format a byte array declaration from ROM data.
    /// Small arrays (≤16 bytes) are single-line; larger arrays wrap at 16 bytes per line.
    /// </summary>
    string FormatByteArrayDeclaration(int romOffset, int length, string varName)
    {
        var sb = new StringBuilder();
        if (length <= 16)
        {
            sb.Append($"byte[] {varName} = new byte[] {{ ");
            for (int i = 0; i < length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"0x{_rom.PrgRom[romOffset + i]:X2}");
            }
            sb.Append(" };");
        }
        else
        {
            sb.AppendLine($"byte[] {varName} = new byte[]");
            sb.Append('{');
            for (int i = 0; i < length; i++)
            {
                if (i % 16 == 0)
                {
                    if (i > 0) sb.Append(',');
                    sb.AppendLine();
                    sb.Append("    ");
                }
                else
                {
                    sb.Append(", ");
                }
                sb.Append($"0x{_rom.PrgRom[romOffset + i]:X2}");
            }
            sb.AppendLine();
            sb.Append("};");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Generate the final C# source code from decompiled statements.
    /// </summary>
    string GenerateCSharp(List<string> statements)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Decompiled from .nes ROM by dotnes decompiler");
        sb.AppendLine();

        foreach (var statement in statements)
        {
            sb.AppendLine(statement);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generate a .csproj file for the decompiled project.
    /// </summary>
    public string GenerateCsproj(string projectName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <OutputType>Exe</OutputType>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <Nullable>enable</Nullable>");

        if (_rom.Mapper != 0)
            sb.AppendLine($"    <NESMapper>{_rom.Mapper}</NESMapper>");
        if (_rom.PrgBanks != 2)
            sb.AppendLine($"    <NESPrgBanks>{_rom.PrgBanks}</NESPrgBanks>");
        if (_rom.ChrBanks != 1)
            sb.AppendLine($"    <NESChrBanks>{_rom.ChrBanks}</NESChrBanks>");
        if (_rom.VerticalMirroring)
            sb.AppendLine("    <NESVerticalMirroring>true</NESVerticalMirroring>");

        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <Using Include=\"NES\" />");
        sb.AppendLine("    <PackageReference Include=\"dotnes\" Version=\"*\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("</Project>");

        return sb.ToString();
    }

    /// <summary>
    /// Get the size of a 6502 instruction by its opcode byte.
    /// </summary>
    static int GetInstructionSize(byte opcode)
    {
        // Implied/Accumulator instructions (1 byte)
        if (opcode is 0x00 or 0x08 or 0x0A or 0x18 or 0x28 or 0x2A or 0x38
            or 0x40 or 0x48 or 0x4A or 0x58 or 0x60 or 0x68 or 0x6A
            or 0x78 or 0x88 or 0x8A or 0x98 or 0x9A or 0xA8 or 0xAA
            or 0xB8 or 0xBA or 0xC8 or 0xCA or 0xD8 or 0xE8 or 0xEA
            or 0xF8)
            return 1;

        // Absolute + Absolute,X + Absolute,Y + Indirect (3 bytes)
        if (opcode is 0x0D or 0x0E or 0x19 or 0x1D or 0x1E or 0x20
            or 0x2C or 0x2D or 0x2E or 0x39 or 0x3D or 0x3E
            or 0x4C or 0x4D or 0x4E or 0x59 or 0x5D or 0x5E
            or 0x6C or 0x6D or 0x6E or 0x79 or 0x7D or 0x7E
            or 0x8C or 0x8D or 0x8E or 0x99 or 0x9D
            or 0xAC or 0xAD or 0xAE or 0xB9 or 0xBC or 0xBD or 0xBE
            or 0xCC or 0xCD or 0xCE or 0xD9 or 0xDD or 0xDE
            or 0xEC or 0xED or 0xEE or 0xF9 or 0xFD or 0xFE)
            return 3;

        // All other valid opcodes are 2 bytes (immediate, zero page, relative, indexed indirect, indirect indexed)
        return 2;
    }
}
