using System.Text;
using dotnes.ObjectModel;
using NES;

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
    readonly List<(ushort Address, int Size)> _arrayDeclarations = new();
    readonly Dictionary<ushort, string> _localVariables = new();
    readonly List<ForLoopInfo> _forLoops = new();
    readonly HashSet<ushort> _forLoopLocals = new();
    readonly List<(ushort PpuAddress, byte[] Data)> _chrRamTileData = new();

    ushort _mainAddress;
    ushort _mainEnd;
    int _dataCounter;
    ushort? _lastVramAdrTarget;
    byte? _ppuAddrHigh; // Tracks first byte written to PPU_ADDR ($2006) for CHR RAM detection

    record ForLoopInfo(
        ushort LocalAddress,     // counter variable address ($03xx)
        byte InitValue,          // initial value (usually 0)
        byte Limit,              // upper bound (N in i < N)
        int InitIndex,           // index of LDA #imm (first init instruction)
        int BodyStartIndex,      // index of first body instruction
        int FooterStartIndex,    // index of first footer instruction
        int AfterLoopIndex       // index of first instruction after BCC
    );

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

        // Find main address by reading the JMP target from the detectNTSC block.
        // The startup sequence ends with detectNTSC which has a JMP to main as its last instruction.
        if (labels.TryGetValue("detectNTSC", out ushort detectAddr))
        {
            int detectOffset = detectAddr - 0x8000;
            int blockSize = program.Blocks.FirstOrDefault(b => b.Label == "detectNTSC")?.Size ?? 0;

            // Scan the detectNTSC block for JMP (0x4C) targeting an address >= builtInsEnd
            if (blockSize > 0)
            {
                int blockEnd = detectOffset + blockSize;
                for (int i = detectOffset; i < blockEnd && i < _rom.PrgRom.Length - 2; i++)
                {
                    if (_rom.PrgRom[i] == 0x4C) // JMP absolute
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
        }

        if (_mainAddress == 0)
        {
            _mainAddress = builtInsEnd;
            _symbolTable[_mainAddress] = "main";
            _logger.WriteLine($"Main address (fallback) at ${_mainAddress:X4}");
        }

        // Scan for the end of main (JMP to self, backward JMP game loop, or first RTS after main)
        _mainEnd = FindMainEnd();

        // Identify final built-ins after main by scanning JSR targets from within main
        // and matching byte patterns at those addresses
        IdentifyFinalBuiltIns(_mainEnd);
    }

    /// <summary>
    /// Find the end of the main function by scanning for infinite loop patterns:
    /// 1. JMP to self (while(true);) - simple infinite loop
    /// 2. Backward JMP to an address within main (while(true){body}) - game loop
    ///    detected as the last backward JMP before the first RTS instruction
    /// </summary>
    ushort FindMainEnd()
    {
        int offset = _mainAddress - 0x8000;
        ushort address = _mainAddress;
        ushort lastBackwardJmpEnd = 0;

        while (offset < _rom.PrgRom.Length - 2)
        {
            byte opcode = _rom.PrgRom[offset];

            if (opcode == 0x4C) // JMP absolute
            {
                ushort target = (ushort)(_rom.PrgRom[offset + 1] | (_rom.PrgRom[offset + 2] << 8));

                // JMP to self = while(true);
                if (target == address)
                    return (ushort)(address + 3);

                // Backward JMP within main = potential while(true){body} game loop
                if (target >= _mainAddress && target < address)
                    lastBackwardJmpEnd = (ushort)(address + 3);
            }

            // RTS marks the end of a subroutine. If we've seen a backward JMP,
            // we've passed the game loop and entered a user function.
            if (opcode == 0x60 && lastBackwardJmpEnd != 0) // RTS
            {
                _logger.WriteLine($"Found game loop end at ${lastBackwardJmpEnd - 3:X4} (backward JMP before RTS at ${address:X4})");
                return lastBackwardJmpEnd;
            }

            int size = GetInstructionSize(opcode);
            offset += size;
            address += (ushort)size;
        }

        // If we found a backward JMP but no RTS, still use it
        if (lastBackwardJmpEnd != 0)
            return lastBackwardJmpEnd;

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
            // set_rand: STA RAND_SEED ($3C); RTS  (85 3C 60)
            ("set_rand", new byte[] { 0x85, 0x3C, 0x60 }),
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

            // Stop if we've reached the detected end of main
            // (_mainEnd is always >= $8000 when set, so 0 is a safe sentinel for "not set")
            if (_mainEnd != 0 && tempAddr >= _mainEnd) break;
        }

        // Detect byte[] array declarations from indexed addressing patterns
        DetectArrayDeclarations(instructions);

        // Detect local variable usage (non-indexed STA/LDA to $0325+ not claimed by arrays)
        DetectLocalVariables(instructions);

        // Detect for-loop structures (init/footer patterns)
        DetectForLoops(instructions);

        if (instructions.Count > 0)
            _logger.WriteLine($"Collected {instructions.Count} instructions (${_mainAddress:X4}-${instructions[^1].Address:X4})");

        // Build lookup tables for for-loop init and footer indices
        var forLoopByInit = new Dictionary<int, ForLoopInfo>();
        var forLoopByFooter = new Dictionary<int, ForLoopInfo>();
        foreach (var fl in _forLoops)
        {
            forLoopByInit[fl.InitIndex] = fl;
            forLoopByFooter[fl.FooterStartIndex] = fl;
        }

        // Now walk through instructions with look-ahead for pattern matching
        var pushedBytes = new Stack<byte>();   // Track values pushed via pusha (8-bit)
        var pushedWords = new Stack<ushort>(); // Track values pushed via pushax (16-bit)
        byte? lastImmediateA = null;           // Track last LDA #imm value for consecutive poke detection
        var unknownInstructions = new List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)>();
        string? lastLoadedVarName = null;      // Track name of last local variable loaded into A
        byte? lastCmpValue = null;             // Track value from last CMP #imm instruction
        var pendingCloseBraces = new Stack<ushort>(); // Target addresses where if-blocks end

        for (int i = 0; i < instructions.Count; i++)
        {
            // Check if this is a for-loop init (LDA #imm / STA $local)
            if (forLoopByInit.TryGetValue(i, out var loopInit))
            {
                var varName = _localVariables[loopInit.LocalAddress];
                statements.Add($"for (byte {varName} = {loopInit.InitValue}; {varName} < {loopInit.Limit}; {varName}++)");
                statements.Add("{");
                i = loopInit.BodyStartIndex - 1; // -1 because the for loop will i++
                lastImmediateA = null;
                lastLoadedVarName = null;
                lastCmpValue = null;
                continue;
            }

            // Check if this is a for-loop footer (LDA $local / CLC / ADC / STA / CMP / BCC)
            if (forLoopByFooter.TryGetValue(i, out var loopFooter))
            {
                statements.Add("}");
                i = loopFooter.AfterLoopIndex - 1; // -1 because the for loop will i++
                lastImmediateA = null;
                lastLoadedVarName = null;
                lastCmpValue = null;
                continue;
            }

            var (instrAddr, opcode, op1, op2) = instructions[i];

            // Emit closing braces for any if-blocks that end at this address
            while (pendingCloseBraces.Count > 0 && pendingCloseBraces.Peek() == instrAddr)
            {
                pendingCloseBraces.Pop();
                statements.Add("}");
            }

            // Pattern: JMP to self = while (true) ;
            if (opcode == 0x4C && op1.HasValue && op2.HasValue)
            {
                ushort target = (ushort)(op1.Value | (op2.Value << 8));
                if (target == instrAddr)
                {
                    FlushUnknownInstructions(unknownInstructions, statements);
                    statements.Add("while (true) ;");
                    break;
                }
                // Last instruction is a backward JMP within main = while (true) { body } game loop
                if (i == instructions.Count - 1 && target >= _mainAddress && target < instrAddr)
                {
                    FlushUnknownInstructions(unknownInstructions, statements);
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
                    lastImmediateA = null; // JSR modifies A
                    lastLoadedVarName = null;
                    lastCmpValue = null;
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
                            lastImmediateA = null; // JSR modifies A
                            lastLoadedVarName = null;
                            lastCmpValue = null;
                            i += 2; // skip LDX and JSR pushax
                            continue;
                        }
                        if (targetName != "pusha")
                        {
                            // Fastcall: pointer in A:X (LDA=lo, LDX=hi)
                            // Don't pop stacks — fastcall targets receive args in registers, not on cc65 stack
                            byte aVal = op1.Value;
                            byte xVal = nextLdx.Op1.Value;
                            var stmts = DecompileCallXA(targetName, xVal, aVal, null, null);
                            FlushUnknownInstructions(unknownInstructions, statements);
                            statements.AddRange(stmts);
                            lastImmediateA = null; // JSR modifies A
                            lastLoadedVarName = null;
                            lastCmpValue = null;
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
                        FlushUnknownInstructions(unknownInstructions, statements);
                        statements.AddRange(stmts);
                        lastImmediateA = null; // JSR modifies A
                        lastLoadedVarName = null;
                        lastCmpValue = null;
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
                        if (stmt != null)
                        {
                            FlushUnknownInstructions(unknownInstructions, statements);
                            statements.Add(stmt);
                        }
                        lastImmediateA = null; // JSR modifies A
                        lastLoadedVarName = null;
                        lastCmpValue = null;
                        i++; // skip the JSR
                        continue;
                    }
                }
            }

            // Pattern: LDA #imm / STA $local → local variable assignment
            if (opcode == 0xA9 && op1.HasValue && i + 1 < instructions.Count)
            {
                var next = instructions[i + 1];
                if (next.Opcode == 0x8D && next.Op1.HasValue && next.Op2.HasValue)
                {
                    ushort addr = (ushort)(next.Op1.Value | (next.Op2.Value << 8));
                    if (_localVariables.TryGetValue(addr, out var varName))
                    {
                        FlushUnknownInstructions(unknownInstructions, statements);
                        statements.Add($"{varName} = 0x{op1.Value:X2};");
                        lastImmediateA = op1.Value;
                        lastLoadedVarName = null;
                        lastCmpValue = null;
                        i++; // skip the STA
                        continue;
                    }
                }
            }

            // Pattern: STA $local (consecutive — A still holds last immediate value)
            if (opcode == 0x8D && op1.HasValue && op2.HasValue && lastImmediateA.HasValue)
            {
                ushort addr = (ushort)(op1.Value | (op2.Value << 8));
                if (_localVariables.TryGetValue(addr, out var varName))
                {
                    FlushUnknownInstructions(unknownInstructions, statements);
                    statements.Add($"{varName} = 0x{lastImmediateA.Value:X2};");
                    continue;
                }
            }

            // Pattern: LDA $local → load local variable into A (track for use in subsequent calls/branches)
            if (opcode == 0xAD && op1.HasValue && op2.HasValue)
            {
                ushort addr = (ushort)(op1.Value | (op2.Value << 8));
                if (_localVariables.TryGetValue(addr, out var loadedName))
                {
                    // Don't emit a statement — the value in A will be consumed by the next JSR, STA, or branch
                    lastLoadedVarName = loadedName;
                    lastImmediateA = null;
                    lastCmpValue = null;
                    continue;
                }
            }

            // Pattern: LDA #imm / STA $abs → poke(addr, val)
            if (opcode == 0xA9 && op1.HasValue && i + 1 < instructions.Count)
            {
                var next = instructions[i + 1];
                if (next.Opcode == 0x8D && next.Op1.HasValue && next.Op2.HasValue)
                {
                    ushort addr = (ushort)(next.Op1.Value | (next.Op2.Value << 8));
                    if (!_symbolTable.ContainsKey(addr))
                    {
                        FlushUnknownInstructions(unknownInstructions, statements);
                        TrackPpuAddrWrite(addr, op1.Value);
                        statements.Add(FormatPoke(addr, op1.Value));
                        lastImmediateA = op1.Value;
                        i++; // skip the STA
                        continue;
                    }
                }
            }

            // Pattern: STA $abs (consecutive poke — A still holds last immediate value)
            if (opcode == 0x8D && op1.HasValue && op2.HasValue && lastImmediateA.HasValue)
            {
                ushort addr = (ushort)(op1.Value | (op2.Value << 8));
                if (!_symbolTable.ContainsKey(addr))
                {
                    FlushUnknownInstructions(unknownInstructions, statements);
                    TrackPpuAddrWrite(addr, lastImmediateA.Value);
                    statements.Add(FormatPoke(addr, lastImmediateA.Value));
                    continue;
                }
            }

            // Pattern: LDA $abs → peek(addr) for non-symbol-table absolute addresses
            if (opcode == 0xAD && op1.HasValue && op2.HasValue)
            {
                ushort addr = (ushort)(op1.Value | (op2.Value << 8));
                if (!_symbolTable.ContainsKey(addr))
                {
                    FlushUnknownInstructions(unknownInstructions, statements);
                    statements.Add(FormatPeek(addr));
                    lastImmediateA = null; // A now holds peek result, not an immediate
                    lastLoadedVarName = null;
                    lastCmpValue = null;
                    continue;
                }
            }

            // Pattern: CMP #imm → track comparison value for subsequent branch
            if (opcode == 0xC9 && op1.HasValue)
            {
                lastCmpValue = op1.Value;
                continue;
            }

            // Pattern: forward BEQ/BNE/BCS/BCC → if block
            if (opcode is 0xF0 or 0xD0 or 0xB0 or 0x90 && op1.HasValue && lastLoadedVarName != null)
            {
                sbyte branchOffset = (sbyte)op1.Value;
                ushort target = (ushort)(instrAddr + 2 + branchOffset);

                if (branchOffset > 0) // Forward branch = skip body when condition is met
                {
                    string condition;
                    if (lastCmpValue.HasValue)
                    {
                        // After CMP #N: flags reflect A - N
                        condition = opcode switch
                        {
                            0xF0 => $"{lastLoadedVarName} != {lastCmpValue.Value}",  // BEQ skip → body runs when not equal
                            0xD0 => $"{lastLoadedVarName} == {lastCmpValue.Value}",  // BNE skip → body runs when equal
                            0xB0 => $"{lastLoadedVarName} < {lastCmpValue.Value}",   // BCS skip → body runs when < N
                            0x90 => $"{lastLoadedVarName} >= {lastCmpValue.Value}",  // BCC skip → body runs when >= N
                            _ => "/* unknown */"
                        };
                    }
                    else
                    {
                        // After LDA: flags reflect loaded value
                        condition = opcode switch
                        {
                            0xF0 => $"{lastLoadedVarName} != 0",  // BEQ skip → body runs when not zero
                            0xD0 => $"{lastLoadedVarName} == 0",  // BNE skip → body runs when zero
                            _ => "/* unknown */"
                        };
                    }

                    statements.Add($"if ({condition})");
                    statements.Add("{");
                    pendingCloseBraces.Push(target);

                    lastLoadedVarName = null;
                    lastCmpValue = null;
                    lastImmediateA = null;
                    continue;
                }
            }

            // Pattern: plain JSR (no preceding LDA/LDX)
            if (opcode == 0x20 && op1.HasValue && op2.HasValue)
            {
                ushort jsrTarget = (ushort)(op1.Value | (op2.Value << 8));
                if (_symbolTable.TryGetValue(jsrTarget, out var name))
                {
                    var stmt = DecompileCall(name, null, null);
                    if (stmt != null)
                    {
                        FlushUnknownInstructions(unknownInstructions, statements);
                        statements.Add(stmt);
                    }
                }
                lastImmediateA = null; // JSR may modify A
                lastLoadedVarName = null;
                lastCmpValue = null;
                continue;
            }

            // Unrecognized instruction — accumulate for comment emission
            unknownInstructions.Add((instrAddr, opcode, op1, op2));
            lastImmediateA = null;
            lastLoadedVarName = null;
            lastCmpValue = null;
        }

        // Close any remaining open if-blocks (may happen when branch target is past the end of main)
        while (pendingCloseBraces.Count > 0)
        {
            var addr = pendingCloseBraces.Pop();
            _logger.WriteLine($"  Warning: unclosed if-block at ${addr:X4} — target may be outside main");
            statements.Add("}");
        }

        // Flush any remaining unknown instructions before the final while(true)
        FlushUnknownInstructions(unknownInstructions, statements);

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
    /// Detect byte[] array declarations by scanning for indexed addressing modes
    /// (Absolute,X and Absolute,Y) targeting the local variable area ($0325-$07FF).
    /// Arrays are identified as base addresses used with indexed access, and their
    /// sizes are inferred from the gaps between consecutive bases.
    /// </summary>
    void DetectArrayDeclarations(List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> instructions)
    {
        // NES RAM is $0000-$07FF. Local variables start at $0325.
        // Addresses >= $0800 are PPU/APU registers or ROM, not local arrays.
        const ushort MaxLocalAddress = 0x0800;

        var indexedBases = new HashSet<ushort>();
        var nonIndexedLocals = new HashSet<ushort>();

        foreach (var (_, opcode, op1, op2) in instructions)
        {
            if (!op1.HasValue || !op2.HasValue) continue;

            // Only 3-byte instructions can address the local variable area
            if (GetInstructionSize(opcode) != 3) continue;

            ushort operandAddr = (ushort)(op1.Value | (op2.Value << 8));
            if (operandAddr < NESConstants.LocalStackBase || operandAddr >= MaxLocalAddress) continue;

            if (IsIndexedOpcode(opcode))
                indexedBases.Add(operandAddr);
            else
                nonIndexedLocals.Add(operandAddr);
        }

        if (indexedBases.Count == 0) return;

        var sortedBases = indexedBases.OrderBy(a => a).ToList();

        for (int i = 0; i < sortedBases.Count; i++)
        {
            int size;
            if (i + 1 < sortedBases.Count)
            {
                // Size = gap to next array base
                size = sortedBases[i + 1] - sortedBases[i];
            }
            else
            {
                // Last array: find the lowest non-indexed address above the last base
                ushort upperBound = 0;
                foreach (var addr in nonIndexedLocals)
                {
                    if (addr > sortedBases[i] && (upperBound == 0 || addr < upperBound))
                        upperBound = addr;
                }

                size = upperBound > 0 ? upperBound - sortedBases[i] : 1;
            }

            _arrayDeclarations.Add((sortedBases[i], size));
            _logger.WriteLine($"  Detected array at ${sortedBases[i]:X4}, size {size}");
        }
    }

    /// <summary>
    /// Detects local variables by finding non-indexed STA/LDA instructions targeting
    /// addresses in the $0325+ range that aren't claimed by array declarations.
    /// </summary>
    void DetectLocalVariables(List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> instructions)
    {
        const ushort MaxLocalAddress = 0x0800;

        // Build set of addresses claimed by arrays
        var arrayAddresses = new HashSet<ushort>();
        foreach (var (addr, size) in _arrayDeclarations)
        {
            for (int j = 0; j < size; j++)
                arrayAddresses.Add((ushort)(addr + j));
        }

        // Find non-indexed accesses to local variable area
        var localAddresses = new SortedSet<ushort>();
        foreach (var (_, opcode, op1, op2) in instructions)
        {
            if (!op1.HasValue || !op2.HasValue) continue;
            if (GetInstructionSize(opcode) != 3) continue;
            if (IsIndexedOpcode(opcode)) continue;

            // STA absolute (0x8D) or LDA absolute (0xAD)
            if (opcode is not (0x8D or 0xAD)) continue;

            ushort operandAddr = (ushort)(op1.Value | (op2.Value << 8));
            if (operandAddr < NESConstants.LocalStackBase || operandAddr >= MaxLocalAddress) continue;
            if (arrayAddresses.Contains(operandAddr)) continue;

            localAddresses.Add(operandAddr);
        }

        // Assign names: var_0325, var_0326, etc.
        foreach (var addr in localAddresses)
        {
            var name = $"var_{addr:X4}";
            _localVariables[addr] = name;
            _logger.WriteLine($"  Detected local variable '{name}' at ${addr:X4}");
        }
    }

    /// <summary>
    /// Detect for-loop structures by scanning for the condition/branch footer pattern
    /// and matching them to their initialization (LDA #imm / STA $local / JMP forward).
    ///
    /// Pattern 1 (INC): INC $local / LDA $local / CMP #$NN / BCC backward
    /// Pattern 2 (ADC): LDA $local / CLC / ADC #$01 / STA $local / CMP #$NN / BCC backward
    /// </summary>
    void DetectForLoops(List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> instructions)
    {
        // Build address→index lookup for resolving branch targets
        var addrToIndex = new Dictionary<ushort, int>();
        for (int k = 0; k < instructions.Count; k++)
            addrToIndex[instructions[k].Address] = k;

        // Track claimed init indices to prevent overlapping matches
        var claimedInits = new HashSet<int>();

        for (int j = 0; j < instructions.Count; j++)
        {
            // Try to match a for-loop footer starting at index j
            ushort localAddr;
            byte limit;
            int footerStart, afterLoopIndex;
            ushort bccTarget;
            int conditionCheckIndex; // index of the LDA $local in the condition check

            if (TryMatchIncFooter(instructions, j, out localAddr, out limit, out bccTarget, out afterLoopIndex))
            {
                footerStart = j;
                conditionCheckIndex = j + 1; // LDA is after INC
            }
            else if (TryMatchAdcFooter(instructions, j, out localAddr, out limit, out bccTarget, out afterLoopIndex))
            {
                footerStart = j;
                conditionCheckIndex = j; // LDA is at start of ADC footer
            }
            else
            {
                continue;
            }

            if (!_localVariables.ContainsKey(localAddr)) continue;

            // Find the body start index from the BCC target address
            if (!addrToIndex.TryGetValue(bccTarget, out int bodyStartIndex)) continue;

            // Find the initialization: LDA #imm / STA $local / JMP forward
            // The JMP targets the condition check (LDA $local in the footer)
            int initIndex = -1;
            byte initValue = 0;
            if (bodyStartIndex >= 3)
            {
                var ldaInit = instructions[bodyStartIndex - 3];
                var staInit = instructions[bodyStartIndex - 2];
                var jmpInit = instructions[bodyStartIndex - 1];

                if (ldaInit.Opcode == 0xA9 // LDA immediate
                    && staInit.Opcode == 0x8D // STA absolute
                    && jmpInit.Opcode == 0x4C) // JMP absolute
                {
                    ushort staInitAddr = (ushort)(staInit.Op1!.Value | (staInit.Op2!.Value << 8));
                    ushort jmpTarget = (ushort)(jmpInit.Op1!.Value | (jmpInit.Op2!.Value << 8));
                    // Verify STA writes to the loop counter and JMP targets the condition check (LDA $local)
                    if (staInitAddr == localAddr && jmpTarget == instructions[conditionCheckIndex].Address)
                    {
                        initIndex = bodyStartIndex - 3;
                        initValue = ldaInit.Op1!.Value;
                    }
                }
            }
            // Also try without JMP: LDA #imm / STA $local (direct fall-through to body)
            if (initIndex < 0 && bodyStartIndex >= 2)
            {
                var ldaInit = instructions[bodyStartIndex - 2];
                var staInit = instructions[bodyStartIndex - 1];

                if (ldaInit.Opcode == 0xA9 // LDA immediate
                    && staInit.Opcode == 0x8D) // STA absolute
                {
                    ushort staInitAddr = (ushort)(staInit.Op1!.Value | (staInit.Op2!.Value << 8));
                    if (staInitAddr == localAddr)
                    {
                        byte candidateInit = ldaInit.Op1!.Value;
                        // For fall-through init, only treat this as a for-loop initializer when
                        // the initial value is strictly less than the loop limit, so that the
                        // decompiled `for (...; var < limit; ...)` preserves semantics.
                        if (candidateInit < limit)
                        {
                            initIndex = bodyStartIndex - 2;
                            initValue = candidateInit;
                        }
                    }
                }
            }
            if (initIndex < 0) continue;
            if (claimedInits.Contains(initIndex)) continue;

            _forLoops.Add(new ForLoopInfo(localAddr, initValue, limit, initIndex, bodyStartIndex, footerStart, afterLoopIndex));
            _forLoopLocals.Add(localAddr);
            claimedInits.Add(initIndex);
            _logger.WriteLine($"  Detected for loop: {_localVariables[localAddr]} = {initValue} to {limit} (init@{initIndex}, body@{bodyStartIndex}, footer@{footerStart})");
        }
    }

    /// <summary>
    /// Try to match the INC-based footer pattern: INC $local / LDA $local / CMP #NN / BCC backward
    /// </summary>
    static bool TryMatchIncFooter(
        List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> instructions, int j,
        out ushort localAddr, out byte limit, out ushort bccTarget, out int afterLoopIndex)
    {
        localAddr = 0; limit = 0; bccTarget = 0; afterLoopIndex = 0;
        if (j + 3 >= instructions.Count) return false;

        if (instructions[j].Opcode != 0xEE) return false;     // INC absolute
        if (instructions[j + 1].Opcode != 0xAD) return false;  // LDA absolute
        if (instructions[j + 2].Opcode != 0xC9) return false;  // CMP immediate
        if (instructions[j + 3].Opcode != 0x90) return false;  // BCC

        ushort incAddr = (ushort)(instructions[j].Op1!.Value | (instructions[j].Op2!.Value << 8));
        ushort ldaAddr = (ushort)(instructions[j + 1].Op1!.Value | (instructions[j + 1].Op2!.Value << 8));
        if (incAddr != ldaAddr) return false;

        localAddr = incAddr;
        limit = instructions[j + 2].Op1!.Value;

        // Resolve BCC target — must be a backward branch
        // BCC is 2 bytes (opcode + relative offset), so target = address + 2 + signed_offset
        ushort bccInstrAddr = instructions[j + 3].Address;
        sbyte bccOffset = (sbyte)instructions[j + 3].Op1!.Value;
        bccTarget = (ushort)(bccInstrAddr + 2 + bccOffset);
        if (bccTarget >= bccInstrAddr) return false;

        afterLoopIndex = j + 4;
        return true;
    }

    /// <summary>
    /// Try to match the ADC-based footer pattern: LDA $local / CLC / ADC #$01 / STA $local / CMP #NN / BCC backward
    /// </summary>
    static bool TryMatchAdcFooter(
        List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> instructions, int j,
        out ushort localAddr, out byte limit, out ushort bccTarget, out int afterLoopIndex)
    {
        localAddr = 0; limit = 0; bccTarget = 0; afterLoopIndex = 0;
        if (j + 5 >= instructions.Count) return false;

        if (instructions[j].Opcode != 0xAD) return false;      // LDA absolute
        if (instructions[j + 1].Opcode != 0x18) return false;   // CLC
        if (instructions[j + 2].Opcode != 0x69 || instructions[j + 2].Op1 != 0x01) return false; // ADC #$01
        if (instructions[j + 3].Opcode != 0x8D) return false;   // STA absolute
        if (instructions[j + 4].Opcode != 0xC9) return false;   // CMP immediate
        if (instructions[j + 5].Opcode != 0x90) return false;   // BCC

        ushort ldaAddr = (ushort)(instructions[j].Op1!.Value | (instructions[j].Op2!.Value << 8));
        ushort staAddr = (ushort)(instructions[j + 3].Op1!.Value | (instructions[j + 3].Op2!.Value << 8));
        if (ldaAddr != staAddr) return false;

        localAddr = ldaAddr;
        limit = instructions[j + 4].Op1!.Value;

        // Resolve BCC target — must be a backward branch
        // BCC is 2 bytes (opcode + relative offset), so target = address + 2 + signed_offset
        ushort bccInstrAddr = instructions[j + 5].Address;
        sbyte bccOffset = (sbyte)instructions[j + 5].Op1!.Value;
        bccTarget = (ushort)(bccInstrAddr + 2 + bccOffset);
        if (bccTarget >= bccInstrAddr) return false;

        afterLoopIndex = j + 6;
        return true;
    }

    /// <summary>
    /// Returns true if the opcode uses Absolute,X or Absolute,Y addressing mode.
    /// </summary>
    static bool IsIndexedOpcode(byte opcode) => opcode is
        // Absolute,X: ORA, ASL, AND, ROL, EOR, LSR, ADC, ROR, STA, LDY, LDA, CMP, DEC, SBC, INC
        0x1D or 0x1E or 0x3D or 0x3E or 0x5D or 0x5E
        or 0x7D or 0x7E or 0x9D or 0xBC or 0xBD
        or 0xDD or 0xDE or 0xFD or 0xFE
        // Absolute,Y: ORA, AND, EOR, ADC, STA, LDA, LDX, CMP, SBC
        or 0x19 or 0x39 or 0x59 or 0x79 or 0x99
        or 0xB9 or 0xBE or 0xD9 or 0xF9;

    /// <summary>
    /// Decompile a call where A contains a value (the last argument),
    /// and optionally a pushed value was the first argument.
    /// </summary>
    string? DecompileCall(string name, byte? aValue, byte? pushedArg = null)
    {
        // Track single-byte vram_adr calls (transpiler omits LDX #$00 for zero addresses)
        if (name == "vram_adr" && aValue.HasValue)
        {
            _lastVramAdrTarget = aValue.Value;
            return $"vram_adr(0x{aValue.Value:X4});";
        }

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

        // Track vram_adr targets for CHR RAM tile extraction
        if (name == "vram_adr")
            _lastVramAdrTarget = value16;
        else if (name == "vram_write")
            TrackChrRamUpload(value16, pushedPtr);

        return name switch
        {
            "vram_adr" => new List<string> { FormatVramAdr(aValue, xValue) },
            "vram_write" => FormatVramWrite(value16, pushedPtr),
            "pal_bg" => FormatPaletteCall("pal_bg", $"palette{_dataCounter++}", value16, 16),
            "pal_spr" => FormatPaletteCall("pal_spr", $"palette{_dataCounter++}", value16, 16),
            "pal_all" => FormatPaletteCall("pal_all", $"palette{_dataCounter++}", value16, 32),
            "vram_fill" => new List<string> { pushedByte.HasValue
                ? $"vram_fill(0x{pushedByte.Value:X2}, {value16});"
                : $"// vram_fill(?, {value16}); // fill value not recovered" },
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
    /// Track vram_write calls that upload tile data to CHR RAM pattern tables ($0000-$1FFF).
    /// Called before FormatVramWrite to record data for .s file extraction.
    /// </summary>
    void TrackChrRamUpload(ushort length, ushort? dataPointer)
    {
        if (_rom.ChrBanks > 0 || !_lastVramAdrTarget.HasValue || !dataPointer.HasValue || length == 0)
            return;

        ushort ppuAddr = _lastVramAdrTarget.Value;
        _lastVramAdrTarget = null;

        // Only track writes to CHR pattern tables ($0000-$1FFF)
        if (ppuAddr >= 0x2000)
            return;

        int offset = dataPointer.Value - 0x8000;
        // NROM-128 (16KB PRG): $C000-$FFFF mirrors $8000-$BFFF
        if (_rom.PrgRom.Length == 0x4000)
            offset %= 0x4000;
        if (offset >= 0 && offset + length <= _rom.PrgRom.Length)
        {
            var data = new byte[length];
            Array.Copy(_rom.PrgRom, offset, data, 0, length);
            _chrRamTileData.Add((ppuAddr, data));
            _logger.WriteLine($"CHR RAM upload: {length} bytes to PPU ${ppuAddr:X4} from PRG ${dataPointer.Value:X4}");
        }
    }

    /// <summary>
    /// Returns tile data extracted from vram_write calls to CHR RAM pattern tables.
    /// Each entry contains the PPU target address and the raw tile data.
    /// </summary>
    public IReadOnlyList<(ushort PpuAddress, byte[] Data)> GetChrRamTileData() => _chrRamTileData;

    /// <summary>
    /// Track poke() writes to PPU_ADDR ($2006) for CHR RAM detection.
    /// Two consecutive writes set the full 16-bit PPU address (high byte first).
    /// </summary>
    void TrackPpuAddrWrite(ushort addr, byte value)
    {
        if (addr != NESLib.PPU_ADDR)
        {
            _ppuAddrHigh = null;
            return;
        }

        if (!_ppuAddrHigh.HasValue)
        {
            _ppuAddrHigh = value; // First write = high byte
        }
        else
        {
            _lastVramAdrTarget = (ushort)((_ppuAddrHigh.Value << 8) | value);
            _ppuAddrHigh = null;
        }
    }

    /// <summary>
    /// Format a palette call (pal_bg, pal_spr, pal_all) by extracting palette data from ROM.
    /// </summary>
    List<string> FormatPaletteCall(string funcName, string varName, ushort pointer, int size)
    {
        int offset = pointer - 0x8000;
        // NROM-128 (16KB PRG): $C000-$FFFF mirrors $8000-$BFFF
        if (_rom.PrgRom.Length == 0x4000)
            offset %= 0x4000;
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
    /// Map of known NES hardware register addresses to their NESLib constant names.
    /// </summary>
    static readonly Dictionary<ushort, string> KnownAddresses = new()
    {
        // PPU registers
        { NESLib.PPU_CTRL, nameof(NESLib.PPU_CTRL) },
        { NESLib.PPU_MASK, nameof(NESLib.PPU_MASK) },
        { NESLib.PPU_STATUS, nameof(NESLib.PPU_STATUS) },
        { NESLib.PPU_SCROLL, nameof(NESLib.PPU_SCROLL) },
        { NESLib.PPU_ADDR, nameof(NESLib.PPU_ADDR) },
        { NESLib.PPU_DATA, nameof(NESLib.PPU_DATA) },
        // APU registers
        { NESLib.APU_PULSE1_CTRL, nameof(NESLib.APU_PULSE1_CTRL) },
        { NESLib.APU_PULSE1_SWEEP, nameof(NESLib.APU_PULSE1_SWEEP) },
        { NESLib.APU_PULSE1_TIMER_LO, nameof(NESLib.APU_PULSE1_TIMER_LO) },
        { NESLib.APU_PULSE1_TIMER_HI, nameof(NESLib.APU_PULSE1_TIMER_HI) },
        { NESLib.APU_PULSE2_CTRL, nameof(NESLib.APU_PULSE2_CTRL) },
        { NESLib.APU_PULSE2_SWEEP, nameof(NESLib.APU_PULSE2_SWEEP) },
        { NESLib.APU_PULSE2_TIMER_LO, nameof(NESLib.APU_PULSE2_TIMER_LO) },
        { NESLib.APU_PULSE2_TIMER_HI, nameof(NESLib.APU_PULSE2_TIMER_HI) },
        { NESLib.APU_TRIANGLE_CTRL, nameof(NESLib.APU_TRIANGLE_CTRL) },
        { NESLib.APU_TRIANGLE_TIMER_LO, nameof(NESLib.APU_TRIANGLE_TIMER_LO) },
        { NESLib.APU_TRIANGLE_TIMER_HI, nameof(NESLib.APU_TRIANGLE_TIMER_HI) },
        { NESLib.APU_NOISE_CTRL, nameof(NESLib.APU_NOISE_CTRL) },
        { NESLib.APU_NOISE_PERIOD, nameof(NESLib.APU_NOISE_PERIOD) },
        { NESLib.APU_NOISE_LENGTH, nameof(NESLib.APU_NOISE_LENGTH) },
        { NESLib.APU_STATUS, nameof(NESLib.APU_STATUS) },
    };

    /// <summary>
    /// Format an absolute address as a NESLib constant name or hex literal.
    /// </summary>
    static string FormatAddress(ushort addr)
    {
        if (KnownAddresses.TryGetValue(addr, out var name))
            return name;
        return $"0x{addr:X4}";
    }

    /// <summary>
    /// Format a poke() statement with a named or hex address and hex value.
    /// </summary>
    static string FormatPoke(ushort addr, byte value) =>
        $"poke({FormatAddress(addr)}, 0x{value:X2});";

    /// <summary>
    /// Format a peek() statement with a named or hex address.
    /// </summary>
    static string FormatPeek(ushort addr) =>
        $"peek({FormatAddress(addr)});";

    /// <summary>
    /// Generate the final C# source code from decompiled statements.
    /// </summary>
    string GenerateCSharp(List<string> statements)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Decompiled from .nes ROM by dotnes decompiler");
        sb.AppendLine();

        // Emit byte[] array declarations
        if (_arrayDeclarations.Count > 0)
        {
            foreach (var (addr, size) in _arrayDeclarations)
            {
                sb.AppendLine($"byte[] array_{addr:X4} = new byte[{size}];");
            }
            sb.AppendLine();
        }

        // Emit local variable declarations (excluding for-loop counter variables)
        var nonLoopLocals = _localVariables
            .Where(kvp => !_forLoopLocals.Contains(kvp.Key))
            .OrderBy(kvp => kvp.Key)
            .ToList();
        if (nonLoopLocals.Count > 0)
        {
            foreach (var (addr, name) in nonLoopLocals)
            {
                sb.AppendLine($"byte {name} = 0;");
            }
            sb.AppendLine();
        }

        int indent = 0;
        foreach (var statement in statements)
        {
            if (statement == "}")
                indent = Math.Max(0, indent - 1);

            string prefix = indent > 0 ? new string(' ', indent * 4) : "";
            sb.AppendLine(prefix + statement);

            if (statement == "{")
                indent++;
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

    /// <summary>
    /// Flush accumulated unknown instructions as comment lines in the output.
    /// Splits into separate blocks when addresses are not contiguous (i.e., a recognized-
    /// but-not-emitted instruction was between two unknowns).
    /// </summary>
    static void FlushUnknownInstructions(
        List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> unknowns,
        List<string> statements)
    {
        if (unknowns.Count == 0)
            return;

        int groupStart = 0;
        for (int i = 1; i <= unknowns.Count; i++)
        {
            // Split when the next unknown isn't contiguous with the previous one
            if (i < unknowns.Count)
            {
                var prev = unknowns[i - 1];
                ushort expectedNext = (ushort)(prev.Address + GetInstructionSize(prev.Opcode));
                if (unknowns[i].Address != expectedNext)
                {
                    EmitUnknownBlock(unknowns, groupStart, i, statements);
                    groupStart = i;
                }
            }
        }

        EmitUnknownBlock(unknowns, groupStart, unknowns.Count, statements);
        unknowns.Clear();
    }

    static void EmitUnknownBlock(
        List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> unknowns,
        int start, int end, List<string> statements)
    {
        ushort startAddr = unknowns[start].Address;
        var last = unknowns[end - 1];
        ushort endAddr = (ushort)(last.Address + GetInstructionSize(last.Opcode) - 1);

        if (startAddr == endAddr)
            statements.Add($"// Unknown 6502 assembly at ${startAddr:X4}:");
        else
            statements.Add($"// Unknown 6502 assembly at ${startAddr:X4}-${endAddr:X4}:");

        for (int i = start; i < end; i++)
        {
            var (addr, opcode, op1, op2) = unknowns[i];
            statements.Add($"//   {FormatDisassembly(addr, opcode, op1, op2)}");
        }
    }

    /// <summary>
    /// Format a single 6502 instruction as a disassembly string (e.g., "LDA #$01", "STA $2005").
    /// </summary>
    static string FormatDisassembly(ushort address, byte opcode, byte? op1, byte? op2)
    {
        string mnemonic = GetMnemonic(opcode);
        int size = GetInstructionSize(opcode);

        if (size == 1)
            return mnemonic;

        if (size == 2 && op1.HasValue)
        {
            // Determine addressing mode from opcode
            return opcode switch
            {
                // Immediate: #$xx
                0x09 or 0x29 or 0x49 or 0x69 or 0xA0 or 0xA2 or 0xA9
                    or 0xC0 or 0xC9 or 0xE0 or 0xE9
                    => $"{mnemonic} #${op1.Value:X2}",

                // Relative: branch instructions
                0x10 or 0x30 or 0x50 or 0x70 or 0x90 or 0xB0 or 0xD0 or 0xF0
                    => $"{mnemonic} ${(ushort)(address + 2 + (sbyte)op1.Value):X4}",

                // Zero Page,X
                0x15 or 0x16 or 0x35 or 0x36 or 0x55 or 0x56 or 0x75 or 0x76
                    or 0x94 or 0x95 or 0xB4 or 0xB5 or 0xD5 or 0xD6 or 0xF5 or 0xF6
                    => $"{mnemonic} ${op1.Value:X2},X",

                // Zero Page,Y
                0x96 or 0xB6
                    => $"{mnemonic} ${op1.Value:X2},Y",

                // Indexed Indirect (X)
                0x01 or 0x21 or 0x41 or 0x61 or 0x81 or 0xA1 or 0xC1 or 0xE1
                    => $"{mnemonic} (${op1.Value:X2},X)",

                // Indirect Indexed (Y)
                0x11 or 0x31 or 0x51 or 0x71 or 0x91 or 0xB1 or 0xD1 or 0xF1
                    => $"{mnemonic} (${op1.Value:X2}),Y",

                // Default: Zero Page
                _ => $"{mnemonic} ${op1.Value:X2}",
            };
        }

        if (size == 3 && op1.HasValue && op2.HasValue)
        {
            ushort addr = (ushort)(op1.Value | (op2.Value << 8));

            return opcode switch
            {
                // Indirect (JMP only)
                0x6C => $"{mnemonic} (${addr:X4})",

                // Absolute,X
                0x1D or 0x1E or 0x3D or 0x3E or 0x5D or 0x5E or 0x7D or 0x7E
                    or 0x9D or 0xBC or 0xBD or 0xDD or 0xDE or 0xFD or 0xFE
                    => $"{mnemonic} ${addr:X4},X",

                // Absolute,Y
                0x19 or 0x39 or 0x59 or 0x79 or 0x99 or 0xB9 or 0xBE or 0xD9 or 0xF9
                    => $"{mnemonic} ${addr:X4},Y",

                // Default: Absolute
                _ => $"{mnemonic} ${addr:X4}",
            };
        }

        return mnemonic;
    }

    /// <summary>
    /// Get the mnemonic name for a 6502 opcode byte.
    /// </summary>
    static string GetMnemonic(byte opcode) => opcode switch
    {
        0x00 => "BRK", 0x01 => "ORA", 0x05 => "ORA", 0x06 => "ASL", 0x08 => "PHP", 0x09 => "ORA", 0x0A => "ASL",
        0x0D => "ORA", 0x0E => "ASL",
        0x10 => "BPL", 0x11 => "ORA", 0x15 => "ORA", 0x16 => "ASL", 0x18 => "CLC", 0x19 => "ORA", 0x1D => "ORA",
        0x1E => "ASL",
        0x20 => "JSR", 0x21 => "AND", 0x24 => "BIT", 0x25 => "AND", 0x26 => "ROL", 0x28 => "PLP", 0x29 => "AND",
        0x2A => "ROL", 0x2C => "BIT", 0x2D => "AND", 0x2E => "ROL",
        0x30 => "BMI", 0x31 => "AND", 0x35 => "AND", 0x36 => "ROL", 0x38 => "SEC", 0x39 => "AND", 0x3D => "AND",
        0x3E => "ROL",
        0x40 => "RTI", 0x41 => "EOR", 0x45 => "EOR", 0x46 => "LSR", 0x48 => "PHA", 0x49 => "EOR", 0x4A => "LSR",
        0x4C => "JMP", 0x4D => "EOR", 0x4E => "LSR",
        0x50 => "BVC", 0x51 => "EOR", 0x55 => "EOR", 0x56 => "LSR", 0x58 => "CLI", 0x59 => "EOR", 0x5D => "EOR",
        0x5E => "LSR",
        0x60 => "RTS", 0x61 => "ADC", 0x65 => "ADC", 0x66 => "ROR", 0x68 => "PLA", 0x69 => "ADC", 0x6A => "ROR",
        0x6C => "JMP", 0x6D => "ADC", 0x6E => "ROR",
        0x70 => "BVS", 0x71 => "ADC", 0x75 => "ADC", 0x76 => "ROR", 0x78 => "SEI", 0x79 => "ADC", 0x7D => "ADC",
        0x7E => "ROR",
        0x81 => "STA", 0x84 => "STY", 0x85 => "STA", 0x86 => "STX", 0x88 => "DEY", 0x8A => "TXA", 0x8C => "STY",
        0x8D => "STA", 0x8E => "STX",
        0x90 => "BCC", 0x91 => "STA", 0x94 => "STY", 0x95 => "STA", 0x96 => "STX", 0x98 => "TYA", 0x99 => "STA",
        0x9A => "TXS", 0x9D => "STA",
        0xA0 => "LDY", 0xA1 => "LDA", 0xA2 => "LDX", 0xA4 => "LDY", 0xA5 => "LDA", 0xA6 => "LDX", 0xA8 => "TAY",
        0xA9 => "LDA", 0xAA => "TAX", 0xAC => "LDY", 0xAD => "LDA", 0xAE => "LDX",
        0xB0 => "BCS", 0xB1 => "LDA", 0xB4 => "LDY", 0xB5 => "LDA", 0xB6 => "LDX", 0xB8 => "CLV", 0xB9 => "LDA",
        0xBA => "TSX", 0xBC => "LDY", 0xBD => "LDA", 0xBE => "LDX",
        0xC0 => "CPY", 0xC1 => "CMP", 0xC4 => "CPY", 0xC5 => "CMP", 0xC6 => "DEC", 0xC8 => "INY", 0xC9 => "CMP",
        0xCA => "DEX", 0xCC => "CPY", 0xCD => "CMP", 0xCE => "DEC",
        0xD0 => "BNE", 0xD1 => "CMP", 0xD5 => "CMP", 0xD6 => "DEC", 0xD8 => "CLD", 0xD9 => "CMP", 0xDD => "CMP",
        0xDE => "DEC",
        0xE0 => "CPX", 0xE1 => "SBC", 0xE4 => "CPX", 0xE5 => "SBC", 0xE6 => "INC", 0xE8 => "INX", 0xE9 => "SBC",
        0xEA => "NOP", 0xEC => "CPX", 0xED => "SBC", 0xEE => "INC",
        0xF0 => "BEQ", 0xF1 => "SBC", 0xF5 => "SBC", 0xF6 => "INC", 0xF8 => "SED", 0xF9 => "SBC", 0xFD => "SBC",
        0xFE => "INC",
        _ => $"???({opcode:X2})",
    };
}
