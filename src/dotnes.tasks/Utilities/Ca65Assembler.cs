using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// ca65-compatible assembler. Parses .s assembly files and produces
/// Block objects compatible with the dotnes transpiler's ROM builder.
/// 
/// Supported ca65 features:
/// - All 6502 instructions with all addressing modes
/// - Labels (global and @local scoped to nearest global)
/// - .byte/.word data directives with expressions
/// - .res N (reserve bytes)
/// - .segment "NAME"
/// - .import/.export
/// - .define name value
/// - constant = value assignments
/// - .if(expr)/.else/.endif conditional assembly
/// - Expression evaluation: hex, decimal, binary, arithmetic, .lobyte/.hibyte, &lt;/&gt;
/// </summary>
public class Ca65Assembler
{
    // Symbol tables
    readonly Dictionary<string, int> _constants = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _defines = new(StringComparer.Ordinal);
    readonly HashSet<string> _exports = new(StringComparer.Ordinal);
    readonly HashSet<string> _imports = new(StringComparer.Ordinal);

    // Segment tracking
    string _currentSegment = "CODE";
    int _zeroPageOffset = 0;

    // Labels: name → (segment, offset within segment)
    readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
    string? _lastGlobalLabel;

    // Conditional assembly
    readonly Stack<(bool active, bool hadTrue)> _conditionalStack = new();

    // Output: assembled blocks ready for Program6502
    readonly List<Block> _codeBlocks = new();
    readonly List<Block> _dataBlocks = new();
    string? _pendingInlineLabel;

    // Pass 1 intermediate: raw lines grouped by segment
    readonly List<AssemblyLine> _lines = new();

    /// <summary>
    /// Labels exported from this assembly file
    /// </summary>
    public IReadOnlyCollection<string> Exports => _exports;

    /// <summary>
    /// Symbols imported by this assembly file (need external resolution)
    /// </summary>
    public IReadOnlyCollection<string> Imports => _imports;

    /// <summary>
    /// Assembles a .s file and returns the resulting blocks for inclusion in a ROM.
    /// </summary>
    public List<Block> Assemble(TextReader reader)
    {
        // Pass 1: Parse all lines, evaluate conditionals, compute label offsets
        Pass1(reader);

        // Pass 2: Emit instructions and data with resolved labels
        Pass2();

        var result = new List<Block>();
        result.AddRange(_codeBlocks);
        result.AddRange(_dataBlocks);
        return result;
    }

    /// <summary>
    /// Assembles from a string (convenience for testing)
    /// </summary>
    public List<Block> Assemble(string source)
    {
        using var reader = new StringReader(source);
        return Assemble(reader);
    }

    #region Pass 1: Parse and compute sizes

    void Pass1(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            ProcessLine(line);
        }
    }

    void ProcessLine(string rawLine)
    {
        // Strip comments (but not inside strings)
        var line = StripComment(rawLine).Trim();
        if (string.IsNullOrEmpty(line)) return;

        // Check conditional assembly state
        if (IsConditionalDirective(line))
        {
            HandleConditional(line);
            return;
        }
        if (!IsActive()) return;

        // Assignment: SYMBOL = expression
        int eqIdx = FindAssignment(line);
        if (eqIdx > 0)
        {
            HandleAssignment(line, eqIdx);
            return;
        }

        // Directives
        if (line.Length > 0 && line[0] == '.')
        {
            HandleDirective(line);
            return;
        }

        // Label: "name:" (possibly followed by more on the same line)
        if (TryParseLabel(line, out string? label, out string? rest))
        {
            RegisterLabel(label!);
            if (!string.IsNullOrWhiteSpace(rest))
                ProcessLine(rest!);
            return;
        }

        // 6502 instruction
        _lines.Add(new AssemblyLine(_currentSegment, AssemblyLineKind.Instruction, line, _lastGlobalLabel));
    }

    static string StripComment(string line)
    {
        // Find first ; not inside a string
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == ';') return line.Substring(0, i);
            if (line[i] == '"')
            {
                i++;
                while (i < line.Length && line[i] != '"') i++;
            }
        }
        return line;
    }

    static int FindAssignment(string line)
    {
        // Look for "NAME = expr" pattern — = not inside parens/brackets, 
        // and NAME must be an identifier (not start with .)
        if (line.Length < 3) return -1;
        if (!IsIdentStart(line[0])) return -1;

        for (int i = 1; i < line.Length; i++)
        {
            if (line[i] == '=')
            {
                // Make sure it's not == or <=
                if (i > 0 && (line[i - 1] == '<' || line[i - 1] == '>' || line[i - 1] == '!')) return -1;
                if (i + 1 < line.Length && line[i + 1] == '=') return -1;
                // Check left side is a valid identifier
                var left = line.Substring(0, i).Trim();
                if (left.Length > 0 && IsValidIdentifier(left)) return i;
            }
        }
        return -1;
    }

    void HandleAssignment(string line, int eqIdx)
    {
        var name = line.Substring(0, eqIdx).Trim();
        var exprStr = line.Substring(eqIdx + 1).Trim();
        var val = Ca65Expression.TryEvaluate(exprStr.AsSpan(), LookupSymbol);
        if (val.HasValue)
            _constants[name] = val.Value;
        else
            // Defer to pass 2 — store as a line
            _lines.Add(new AssemblyLine(_currentSegment, AssemblyLineKind.Assignment, line, _lastGlobalLabel));
    }

    void HandleDirective(string line)
    {
        var lower = line.ToLowerInvariant();

        if (lower.StartsWith(".segment"))
        {
            // .segment "NAME"
            int q1 = line.IndexOf('"');
            int q2 = line.IndexOf('"', q1 + 1);
            if (q1 >= 0 && q2 > q1)
                _currentSegment = line.Substring(q1 + 1, q2 - q1 - 1).ToUpperInvariant();
            return;
        }

        if (lower.StartsWith(".export"))
        {
            var names = line.Substring(7).Trim();
            foreach (var name in names.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                _exports.Add(name.Trim());
            return;
        }

        if (lower.StartsWith(".import"))
        {
            var names = line.Substring(7).Trim();
            foreach (var name in names.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                _imports.Add(name.Trim());
            return;
        }

        if (lower.StartsWith(".define"))
        {
            // .define NAME VALUE
            var rest = line.Substring(7).Trim();
            int space = rest.IndexOfAny(new char[] { ' ', '\t' });
            if (space > 0)
            {
                var name = rest.Substring(0, space);
                var valStr = rest.Substring(space + 1).Trim();
                var val = Ca65Expression.TryEvaluate(valStr.AsSpan(), LookupSymbol);
                _defines[name] = val ?? 0;
            }
            else
            {
                // .define NAME (no value = 1 for conditionals)
                _defines[rest] = 1;
            }
            return;
        }

        if (lower.StartsWith(".byte") || lower.StartsWith(".word") || lower.StartsWith(".res") || lower.StartsWith(".addr"))
        {
            _lines.Add(new AssemblyLine(_currentSegment, AssemblyLineKind.Data, line, _lastGlobalLabel));
            return;
        }

        // Ignore unknown directives (.proc, .endproc, .scope, etc.)
    }

    bool TryParseLabel(string line, out string? label, out string? rest)
    {
        label = null;
        rest = null;

        // Label ends with ':' — could be "name:" or "name: instruction"
        int colon = -1;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == ':' && i > 0)
            {
                colon = i;
                break;
            }
            if (!IsIdentChar(line[i]) && line[i] != '@') break;
        }

        if (colon <= 0) return false;

        label = line.Substring(0, colon).Trim();
        rest = line.Substring(colon + 1).Trim();

        // Also handle "LABEL = subroutine" pattern on same line — not a label declaration
        if (rest.Length > 0 && rest[0] == '=')
        {
            label = null;
            rest = null;
            return false;
        }

        return true;
    }

    void RegisterLabel(string label)
    {
        if (label.Length > 0 && label[0] == '@')
        {
            // Local label: scope to last global label
            var scoped = _lastGlobalLabel != null ? $"{_lastGlobalLabel}:{label}" : label;
            _lines.Add(new AssemblyLine(_currentSegment, AssemblyLineKind.Label, scoped, _lastGlobalLabel));
        }
        else
        {
            _lastGlobalLabel = label;
            _lines.Add(new AssemblyLine(_currentSegment, AssemblyLineKind.Label, label, _lastGlobalLabel));
        }
    }

    #endregion

    #region Conditional Assembly

    bool IsConditionalDirective(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] != '.') return false;
        var lower = trimmed.ToLowerInvariant();
        return lower.StartsWith(".if") || lower.StartsWith(".else") || lower.StartsWith(".endif");
    }

    void HandleConditional(string line)
    {
        var trimmed = line.TrimStart();
        var lower = trimmed.ToLowerInvariant();

        if (lower.StartsWith(".endif"))
        {
            if (_conditionalStack.Count > 0)
                _conditionalStack.Pop();
            return;
        }

        if (lower.StartsWith(".else"))
        {
            if (_conditionalStack.Count > 0)
            {
                var (_, hadTrue) = _conditionalStack.Pop();
                _conditionalStack.Push((!hadTrue, hadTrue));
            }
            return;
        }

        if (lower.StartsWith(".if"))
        {
            if (!IsActive())
            {
                // Parent is inactive — push inactive child
                _conditionalStack.Push((false, true));
                return;
            }

            // Extract condition expression — .if(expr) or .if expr
            var rest = trimmed.Substring(3).TrimStart();
            // Remove opening paren if present
            if (rest.Length > 0 && rest[0] == '(' && rest.Length > 0 && rest[rest.Length - 1] == ')')
                rest = rest.Substring(1, rest.Length - 2);
            else if (rest.Length > 0 && rest[0] == '(')
                rest = rest.Substring(1);

            var val = Ca65Expression.TryEvaluate(rest.AsSpan(), LookupSymbol);
            bool isTrue = val.HasValue && val.Value != 0;
            _conditionalStack.Push((isTrue, isTrue));
        }
    }

    bool IsActive()
    {
        if (_conditionalStack.Count == 0) return true;
        return _conditionalStack.Peek().active;
    }

    #endregion

    #region Pass 2: Emit blocks

    void Pass2()
    {
        // Group lines by contiguous label+instructions for CODE, or by label for DATA
        // Build a single Block per global label in CODE, and data blocks for RODATA/data

        Block? currentBlock = null;
        string? currentBlockLabel = null;
        List<byte> dataBytes = new();
        List<(int offset, string label)> dataRelocations = new();
        string? dataLabel = null;
        string? dataSegment = null;

        // First, compute all label offsets for data segments
        ComputeDataLabelOffsets();
        // Then compute code label offsets
        ComputeCodeLabelOffsets();

        foreach (var asmLine in _lines)
        {
            if (asmLine.Segment == "ZEROPAGE" || asmLine.Segment == "BSS")
            {
                HandleZeroPageLine(asmLine);
                continue;
            }

            bool isCodeSegment = asmLine.Segment == "CODE";

            if (asmLine.Kind == AssemblyLineKind.Label)
            {
                if (isCodeSegment)
                {
                    // Check if next non-label line is data — if so, use label for inline data block
                    bool nextIsData = false;
                    for (int peek = _lines.IndexOf(asmLine) + 1; peek < _lines.Count; peek++)
                    {
                        if (_lines[peek].Kind == AssemblyLineKind.Label) continue;
                        if (_lines[peek].Kind == AssemblyLineKind.Data && _lines[peek].Segment == "CODE")
                            nextIsData = true;
                        break;
                    }

                    if (nextIsData)
                    {
                        // Flush current code block, save label for the inline data block
                        if (currentBlock != null)
                        {
                            _codeBlocks.Add(currentBlock);
                            currentBlock = null;
                            currentBlockLabel = null;
                        }
                        _pendingInlineLabel = asmLine.Text;
                    }
                    else if (currentBlock == null)
                    {
                        currentBlock = new Block(asmLine.Text);
                        currentBlockLabel = asmLine.Text;
                    }
                    else
                    {
                        // Set label on next instruction
                        currentBlock.SetNextLabel(asmLine.Text);
                    }
                }
                else
                {
                    // Data segment label
                    if (dataLabel != null && dataBytes.Count > 0)
                    {
                        EmitDataBlock(dataLabel, dataBytes, dataRelocations);
                    }
                    dataLabel = asmLine.Text;
                    dataSegment = asmLine.Segment;
                    dataBytes.Clear();
                    dataRelocations.Clear();
                }
                continue;
            }

            if (asmLine.Kind == AssemblyLineKind.Assignment)
            {
                // Deferred assignment — try again
                int eq = FindAssignment(asmLine.Text);
                if (eq > 0)
                {
                    var name = asmLine.Text.Substring(0, eq).Trim();
                    var exprStr = asmLine.Text.Substring(eq + 1).Trim();
                    // Check if this is a label alias (e.g., _famitone_init=FamiToneInit)
                    if (IsValidIdentifier(exprStr) && _labels.ContainsKey(exprStr))
                    {
                        // Create a label alias
                        _labels[name] = _labels[exprStr];
                    }
                    else
                    {
                        var val = Ca65Expression.TryEvaluate(exprStr.AsSpan(), LookupSymbol);
                        if (val.HasValue)
                            _constants[name] = val.Value;
                    }
                }
                continue;
            }

            if (asmLine.Kind == AssemblyLineKind.Data)
            {
                if (isCodeSegment)
                {
                    // Inline data in code segment: flush current code block,
                    // emit data as a separate raw data block, then continue code
                    if (currentBlock != null)
                    {
                        _codeBlocks.Add(currentBlock);
                        currentBlock = null;
                        currentBlockLabel = null;
                    }
                    // Use pending label from previous Label line if any
                    string? inlineLabel = _pendingInlineLabel;
                    _pendingInlineLabel = null;
                    var inlineBytes = new List<byte>();
                    var inlineRelocs = new List<(int, string)>();
                    EmitDataBytes(asmLine.Text, asmLine.ScopeLabel, inlineBytes, inlineRelocs);
                    if (inlineBytes.Count > 0)
                    {
                        EmitDataBlock(inlineLabel ?? ("_inline_data_" + _dataBlocks.Count), inlineBytes, inlineRelocs);
                    }
                }
                else
                {
                    // Data segment
                    EmitDataBytes(asmLine.Text, asmLine.ScopeLabel, dataBytes, dataRelocations);
                }
                continue;
            }

            if (asmLine.Kind == AssemblyLineKind.Instruction)
            {
                if (!isCodeSegment) continue;

                if (currentBlock == null)
                {
                    currentBlock = new Block("_anonymous_code_" + _codeBlocks.Count);
                }

                var instr = ParseInstruction(asmLine.Text, asmLine.ScopeLabel);
                if (instr != null)
                    currentBlock.Emit(instr);
            }
        }

        // Flush remaining blocks
        if (currentBlock != null)
            _codeBlocks.Add(currentBlock);
        if (dataLabel != null && dataBytes.Count > 0)
            EmitDataBlock(dataLabel, dataBytes, dataRelocations);

        // Apply label aliases to blocks (e.g., _famitone_init=FamiToneInit)
        ApplyLabelAliasesToBlocks();
    }

    void ApplyLabelAliasesToBlocks()
    {
        // For each label alias (e.g., _famitone_init=FamiToneInit),
        // add the alias to the containing block so Program6502 can resolve it.
        foreach (var asmLine in _lines)
        {
            if (asmLine.Kind != AssemblyLineKind.Assignment) continue;
            int eq = FindAssignment(asmLine.Text);
            if (eq <= 0) continue;

            var aliasName = asmLine.Text.Substring(0, eq).Trim();
            var targetName = asmLine.Text.Substring(eq + 1).Trim();
            if (!IsValidIdentifier(targetName) || !_labels.ContainsKey(targetName)) continue;

            // Find which block contains the target label
            foreach (var block in _codeBlocks)
            {
                // Check block-level label
                if (block.Label == targetName)
                {
                    if (block.AdditionalLabels == null)
                        block.AdditionalLabels = new List<string>();
                    block.AdditionalLabels.Add(aliasName);
                    break;
                }
                // Check instruction-level labels within the block
                bool foundInBlock = false;
                for (int i = 0; i < block.Count; i++)
                {
                    if (block.GetLabelAt(i) == targetName)
                    {
                        // Add as a label alias on this block
                        // Program6502 will resolve it using the instruction offset
                        if (block.AdditionalLabels == null)
                            block.AdditionalLabels = new List<string>();
                        // Store as "aliasName=targetName" so Program6502 knows it's an instruction alias
                        block.AdditionalLabels.Add(aliasName + "=" + targetName);
                        foundInBlock = true;
                        break;
                    }
                }
                if (foundInBlock) break;
            }

            // Check data blocks
            foreach (var block in _dataBlocks)
            {
                if (block.Label == targetName)
                {
                    if (block.AdditionalLabels == null)
                        block.AdditionalLabels = new List<string>();
                    block.AdditionalLabels.Add(aliasName);
                    break;
                }
            }
        }
    }

    void ComputeDataLabelOffsets()
    {
        int offset = 0;
        string? currentLabel = null;
        string? segment = null;

        foreach (var line in _lines)
        {
            if (line.Segment == "ZEROPAGE" || line.Segment == "BSS" || line.Segment == "CODE")
            {
                // When transitioning from a data segment, record current offset
                if (segment != null && segment != "CODE" && segment != "ZEROPAGE" && segment != "BSS")
                {
                    // Don't reset — continue counting
                }
                if (line.Segment == "CODE" || line.Segment == "ZEROPAGE" || line.Segment == "BSS")
                {
                    segment = line.Segment;
                    continue;
                }
            }
            segment = line.Segment;

            if (line.Kind == AssemblyLineKind.Label)
            {
                _labels[line.Text] = offset;
                currentLabel = line.Text;
                continue;
            }

            if (line.Kind == AssemblyLineKind.Data)
            {
                offset += ComputeDataSize(line.Text);
            }
        }
    }

    void ComputeCodeLabelOffsets()
    {
        int offset = 0;
        foreach (var line in _lines)
        {
            if (line.Segment != "CODE") continue;

            if (line.Kind == AssemblyLineKind.Label)
            {
                _labels[line.Text] = offset;
                continue;
            }

            if (line.Kind == AssemblyLineKind.Instruction)
            {
                offset += EstimateInstructionSize(line.Text);
            }

            if (line.Kind == AssemblyLineKind.Data)
            {
                offset += ComputeDataSize(line.Text);
            }
        }
    }

    void HandleZeroPageLine(AssemblyLine line)
    {
        if (line.Kind == AssemblyLineKind.Label)
        {
            _labels[line.Text] = _zeroPageOffset;
            _constants[line.Text] = _zeroPageOffset;
            return;
        }

        if (line.Kind == AssemblyLineKind.Data)
        {
            var trimmed = line.Text.TrimStart();
            var lower = trimmed.ToLowerInvariant();
            if (lower.StartsWith(".res"))
            {
                var countStr = trimmed.Substring(4).Trim();
                var val = Ca65Expression.TryEvaluate(countStr.AsSpan(), LookupSymbol);
                _zeroPageOffset += val ?? 1;
            }
        }
    }

    int ComputeDataSize(string line)
    {
        var trimmed = line.TrimStart();
        var lower = trimmed.ToLowerInvariant();

        if (lower.StartsWith(".byte"))
            return CountDataValues(trimmed.Substring(5));
        if (lower.StartsWith(".word") || lower.StartsWith(".addr"))
            return CountDataValues(trimmed.Substring(5)) * 2;
        if (lower.StartsWith(".res"))
        {
            var countStr = trimmed.Substring(4).Trim();
            // .res might have two args: .res count, value
            int comma = countStr.IndexOf(',');
            var sizeExpr = comma >= 0 ? countStr.Substring(0, comma) : countStr;
            var val = Ca65Expression.TryEvaluate(sizeExpr.AsSpan(), LookupSymbol);
            return val ?? 1;
        }
        return 0;
    }

    static int CountDataValues(string valuesStr)
    {
        if (string.IsNullOrWhiteSpace(valuesStr)) return 0;
        int count = 0;
        int depth = 0;
        bool inString = false;
        count = 1;
        for (int i = 0; i < valuesStr.Length; i++)
        {
            char c = valuesStr[i];
            if (c == '"') inString = !inString;
            if (inString) continue;
            if (c == '(') depth++;
            if (c == ')') depth--;
            if (c == ',' && depth == 0) count++;
        }
        return count;
    }

    int EstimateInstructionSize(string line)
    {
        // Parse mnemonic to determine addressing mode → size
        var parts = SplitInstruction(line);
        if (parts.mnemonic == null) return 0;

        if (parts.operand == null)
        {
            // Implied or Accumulator
            return 1;
        }

        var operand = parts.operand.Trim();

        // Accumulator: "a" or "A"
        if (operand.Equals("a", StringComparison.OrdinalIgnoreCase) || operand.Equals("A", StringComparison.OrdinalIgnoreCase))
            return 1;

        // Immediate: #value
        if (operand.Length > 0 && operand[0] == '#')
            return 2;

        // Branch instructions are always 2 bytes
        if (IsBranch(parts.mnemonic))
            return 2;

        // Indirect: ($xx,x) or ($xx),y or ($xxxx)
        if (operand.Length > 0 && operand[0] == '(')
            return operand.IndexOf(",x)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   operand.IndexOf("),y", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 3;

        // Force zero page with < prefix
        if (operand.Length > 0 && operand[0] == '<')
            return 2;

        // Try to evaluate to determine zero page vs absolute
        var cleanOperand = operand;
        if (cleanOperand.IndexOf(',') >= 0)
            cleanOperand = cleanOperand.Substring(0, cleanOperand.IndexOf(',')).Trim();

        var val = Ca65Expression.TryEvaluate(cleanOperand.AsSpan(), LookupSymbol);
        if (val.HasValue)
        {
            // Zero page if value < $100 and not a known 16-bit label
            return val.Value >= 0 && val.Value <= 0xFF ? 2 : 3;
        }

        // Unknown symbol — assume absolute (3 bytes) unless it's a known zero page symbol
        return 3;
    }

    #endregion

    #region Instruction Parsing

    Instruction? ParseInstruction(string line, string? scopeLabel)
    {
        var parts = SplitInstruction(line);
        if (parts.mnemonic == null) return null;

        if (!Enum.TryParse<Opcode>(parts.mnemonic, true, out var opcode))
            return null;

        if (parts.operand == null)
        {
            // Implied
            return new Instruction(opcode, AddressMode.Implied);
        }

        var operand = parts.operand.Trim();

        // Accumulator
        if (operand.Equals("a", StringComparison.OrdinalIgnoreCase))
            return new Instruction(opcode, AddressMode.Accumulator);

        // Branch instructions
        if (IsBranch(parts.mnemonic))
            return ParseBranchInstruction(opcode, operand, scopeLabel);

        // Immediate: #expr
        if (operand.Length > 0 && operand[0] == '#')
            return ParseImmediateInstruction(opcode, operand.Substring(1), scopeLabel);

        // Indirect modes: (expr,x) or (expr),y or (expr)
        if (operand.Length > 0 && operand[0] == '(')
            return ParseIndirectInstruction(opcode, operand, scopeLabel);

        // Direct addressing: expr or expr,x or expr,y
        return ParseDirectInstruction(opcode, operand, scopeLabel);
    }

    Instruction ParseBranchInstruction(Opcode opcode, string operand, string? scopeLabel)
    {
        var label = ResolveLocalLabel(operand.Trim(), scopeLabel);
        return new Instruction(opcode, AddressMode.Relative, new RelativeOperand(label));
    }

    Instruction ParseImmediateInstruction(Opcode opcode, string expr, string? scopeLabel)
    {
        var trimmed = expr.Trim();

        // Check for <label or >label (lo/hi byte of label)
        if (trimmed.Length > 0 && trimmed[0] == '<')
        {
            var inner = trimmed.Substring(1).Trim();
            // Try as expression first
            var val = EvalExpr(inner, scopeLabel);
            if (val.HasValue)
                return new Instruction(opcode, AddressMode.Immediate, new ImmediateOperand((byte)(val.Value & 0xFF)));
            // Label reference
            var label = ResolveLocalLabel(inner, scopeLabel);
            return new Instruction(opcode, AddressMode.Immediate_LowByte, new LowByteOperand(label));
        }

        if (trimmed.Length > 0 && trimmed[0] == '>')
        {
            var inner = trimmed.Substring(1).Trim();
            var val = EvalExpr(inner, scopeLabel);
            if (val.HasValue)
                return new Instruction(opcode, AddressMode.Immediate, new ImmediateOperand((byte)((val.Value >> 8) & 0xFF)));
            var label = ResolveLocalLabel(inner, scopeLabel);
            return new Instruction(opcode, AddressMode.Immediate_HighByte, new HighByteOperand(label));
        }

        // .lobyte/.hibyte
        if (trimmed.StartsWith(".lobyte", StringComparison.OrdinalIgnoreCase))
        {
            var inner = ExtractFunctionArg(trimmed, 7);
            var val = EvalExpr(inner, scopeLabel);
            if (val.HasValue)
                return new Instruction(opcode, AddressMode.Immediate, new ImmediateOperand((byte)(val.Value & 0xFF)));
            var label = ResolveLocalLabel(inner, scopeLabel);
            return new Instruction(opcode, AddressMode.Immediate_LowByte, new LowByteOperand(label));
        }

        if (trimmed.StartsWith(".hibyte", StringComparison.OrdinalIgnoreCase))
        {
            var inner = ExtractFunctionArg(trimmed, 7);
            var val = EvalExpr(inner, scopeLabel);
            if (val.HasValue)
                return new Instruction(opcode, AddressMode.Immediate, new ImmediateOperand((byte)((val.Value >> 8) & 0xFF)));
            var label = ResolveLocalLabel(inner, scopeLabel);
            return new Instruction(opcode, AddressMode.Immediate_HighByte, new HighByteOperand(label));
        }

        // Normal immediate value
        var value = EvalExpr(trimmed, scopeLabel);
        if (value.HasValue)
            return new Instruction(opcode, AddressMode.Immediate, new ImmediateOperand((byte)(value.Value & 0xFF)));

        // Unresolved — treat as label lo byte (common pattern: lda #label is an error, but handle gracefully)
        var lbl = ResolveLocalLabel(trimmed, scopeLabel);
        return new Instruction(opcode, AddressMode.Immediate_LowByte, new LowByteOperand(lbl));
    }

    Instruction ParseIndirectInstruction(Opcode opcode, string operand, string? scopeLabel)
    {
        // (expr,x) → IndexedIndirect
        // (expr),y → IndirectIndexed  
        // (expr) → Indirect (JMP only)

        var inner = operand.Substring(1); // skip (

        if (inner.EndsWith(",x)", StringComparison.OrdinalIgnoreCase))
        {
            var expr = inner.Substring(0, inner.Length - 3).Trim();
            var val = EvalExpr(expr, scopeLabel);
            if (val.HasValue)
                return new Instruction(opcode, AddressMode.IndexedIndirect, new ImmediateOperand((byte)(val.Value & 0xFF)));
            var label = ResolveLocalLabel(expr, scopeLabel);
            return new Instruction(opcode, AddressMode.IndexedIndirect, new LabelOperand(label, OperandSize.Byte));
        }

        if (inner.IndexOf("),y", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            int closeParen = inner.IndexOf(')');
            var expr = inner.Substring(0, closeParen).Trim();
            var val = EvalExpr(expr, scopeLabel);
            if (val.HasValue)
                return new Instruction(opcode, AddressMode.IndirectIndexed, new ImmediateOperand((byte)(val.Value & 0xFF)));
            var label = ResolveLocalLabel(expr, scopeLabel);
            return new Instruction(opcode, AddressMode.IndirectIndexed, new LabelOperand(label, OperandSize.Byte));
        }

        // (expr) — Indirect (JMP)
        int close = inner.IndexOf(')');
        if (close > 0)
        {
            var expr = inner.Substring(0, close).Trim();
            var val = EvalExpr(expr, scopeLabel);
            if (val.HasValue)
                return new Instruction(opcode, AddressMode.Indirect, new AbsoluteOperand((ushort)val.Value));
            var label = ResolveLocalLabel(expr, scopeLabel);
            return new Instruction(opcode, AddressMode.Indirect, new LabelOperand(label, OperandSize.Word));
        }

        // Fallback
        return new Instruction(opcode, AddressMode.Implied);
    }

    Instruction ParseDirectInstruction(Opcode opcode, string operand, string? scopeLabel)
    {
        bool forceZeroPage = operand.Length > 0 && operand[0] == '<';
        var cleanOperand = forceZeroPage ? operand.Substring(1).Trim() : operand;

        // Check for ,x or ,y indexing
        AddressMode indexMode = AddressMode.Absolute;
        string exprPart = cleanOperand;

        // Split on last comma that's not inside parens
        int commaIdx = FindIndexComma(cleanOperand);
        if (commaIdx > 0)
        {
            var indexStr = cleanOperand.Substring(commaIdx + 1).Trim().ToLowerInvariant();
            exprPart = cleanOperand.Substring(0, commaIdx).Trim();

            if (indexStr == "x")
                indexMode = AddressMode.AbsoluteX;
            else if (indexStr == "y")
                indexMode = AddressMode.AbsoluteY;
        }

        var val = EvalExpr(exprPart, scopeLabel);

        if (val.HasValue)
        {
            bool isZeroPage = forceZeroPage || (val.Value >= 0 && val.Value <= 0xFF);

            if (isZeroPage)
            {
                var zpMode = indexMode switch
                {
                    AddressMode.AbsoluteX => AddressMode.ZeroPageX,
                    AddressMode.AbsoluteY => AddressMode.ZeroPageY,
                    _ => AddressMode.ZeroPage
                };

                // Verify this opcode+mode exists, fall back to absolute if not
                if (OpcodeTable.IsValid(opcode, zpMode))
                    return new Instruction(opcode, zpMode, new ImmediateOperand((byte)(val.Value & 0xFF)));

                // Fall through to absolute
            }

            if (indexMode == AddressMode.AbsoluteX || indexMode == AddressMode.AbsoluteY || indexMode == AddressMode.Absolute)
                return new Instruction(opcode, indexMode, new AbsoluteOperand((ushort)val.Value));
        }

        // Unresolved label reference
        var label = ResolveLocalLabel(exprPart, scopeLabel);

        if (forceZeroPage)
        {
            var zpMode = indexMode switch
            {
                AddressMode.AbsoluteX => AddressMode.ZeroPageX,
                AddressMode.AbsoluteY => AddressMode.ZeroPageY,
                _ => AddressMode.ZeroPage
            };
            return new Instruction(opcode, zpMode, new LabelOperand(label, OperandSize.Byte));
        }

        // Use label operand with word size for absolute addressing
        return new Instruction(opcode, indexMode, new LabelOperand(label, OperandSize.Word));
    }

    static int FindIndexComma(string operand)
    {
        int depth = 0;
        for (int i = operand.Length - 1; i >= 0; i--)
        {
            char c = operand[i];
            if (c == ')') depth++;
            if (c == '(') depth--;
            if (c == ',' && depth == 0) return i;
        }
        return -1;
    }

    #endregion

    #region Data Emission

    void EmitDataInCode(Block block, string line, string? scopeLabel)
    {
        var trimmed = line.TrimStart();
        var lower = trimmed.ToLowerInvariant();

        if (lower.StartsWith(".byte"))
        {
            foreach (var val in ParseDataValues(trimmed.Substring(5), scopeLabel, 1))
            {
                // Emit as raw data instruction (NOP with raw data operand)
                // Actually, we need a way to emit raw bytes in a code block
                // Use a workaround: create individual byte instructions
                foreach (byte b in val.bytes)
                    block.Emit(CreateDataByteInstruction(b));
            }
        }
        else if (lower.StartsWith(".word") || lower.StartsWith(".addr"))
        {
            foreach (var val in ParseDataValues(trimmed.Substring(5), scopeLabel, 2))
            {
                if (val.label != null)
                {
                    // Word label reference — emit as two bytes resolved at link time
                    var instr = new Instruction(Opcode.NOP, AddressMode.Absolute,
                        new LabelOperand(val.label, OperandSize.Word));
                    // We need a proper data word instruction — for now, use raw bytes
                    block.Emit(CreateDataWordLabelInstruction(val.label));
                }
                else
                {
                    foreach (byte b in val.bytes)
                        block.Emit(CreateDataByteInstruction(b));
                }
            }
        }
    }

    void EmitDataBytes(string line, string? scopeLabel, List<byte> bytes, List<(int, string)> relocations)
    {
        var trimmed = line.TrimStart();
        var lower = trimmed.ToLowerInvariant();

        if (lower.StartsWith(".byte"))
        {
            foreach (var val in ParseDataValues(trimmed.Substring(5), scopeLabel, 1))
            {
                bytes.AddRange(val.bytes);
            }
        }
        else if (lower.StartsWith(".word") || lower.StartsWith(".addr"))
        {
            foreach (var val in ParseDataValues(trimmed.Substring(5), scopeLabel, 2))
            {
                if (val.label != null)
                    relocations.Add((bytes.Count, val.label));
                bytes.AddRange(val.bytes);
            }
        }
        else if (lower.StartsWith(".res"))
        {
            var rest = trimmed.Substring(4).Trim();
            int comma = rest.IndexOf(',');
            var countExpr = comma >= 0 ? rest.Substring(0, comma).Trim() : rest;
            var fillExpr = comma >= 0 ? rest.Substring(comma + 1).Trim() : null;

            int count = Ca65Expression.Evaluate(countExpr, s => LookupSymbol(s));
            byte fill = fillExpr != null ? (byte)(Ca65Expression.Evaluate(fillExpr, s => LookupSymbol(s)) & 0xFF) : (byte)0;

            for (int i = 0; i < count; i++)
                bytes.Add(fill);
        }
    }

    IEnumerable<(byte[] bytes, string? label)> ParseDataValues(string valuesStr, string? scopeLabel, int unitSize)
    {
        if (string.IsNullOrWhiteSpace(valuesStr))
            yield break;

        foreach (var part in SplitDataValues(valuesStr))
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // For .word directives, check if the expression references a label
            // (labels need relocation to absolute ROM addresses)
            if (unitSize == 2 && IsLabelReference(trimmed, scopeLabel))
            {
                var label = ResolveLocalLabel(trimmed, scopeLabel);
                yield return (new byte[] { 0x00, 0x00 }, label);
                continue;
            }

            var val = EvalExpr(trimmed, scopeLabel);
            if (val.HasValue)
            {
                if (unitSize == 1)
                    yield return (new byte[] { (byte)(val.Value & 0xFF) }, null);
                else
                    yield return (new byte[] { (byte)(val.Value & 0xFF), (byte)((val.Value >> 8) & 0xFF) }, null);
            }
            else
            {
                // Unresolved label reference
                var label = ResolveLocalLabel(trimmed, scopeLabel);
                if (unitSize == 1)
                    yield return (new byte[] { 0x00 }, null); // placeholder
                else
                    yield return (new byte[] { 0x00, 0x00 }, label); // placeholder with relocation
            }
        }
    }

    /// <summary>
    /// Checks if an expression is a label reference (not a pure numeric constant).
    /// Labels need relocations because their absolute address depends on ROM placement.
    /// </summary>
    bool IsLabelReference(string expr, string? scopeLabel)
    {
        var trimmed = expr.Trim();
        // If it starts with $ (hex), % (binary), or is a digit, it's a constant
        if (trimmed.Length > 0 && (trimmed[0] == '$' || trimmed[0] == '%' || char.IsDigit(trimmed[0])))
            return false;
        // If it starts with @ or is an identifier that resolves to a label, it's a label ref
        if (trimmed.Length > 0 && trimmed[0] == '@')
            return true;
        // Check if the identifier is a known label (not a constant/define)
        if (IsValidIdentifier(trimmed))
        {
            var resolved = ResolveLocalLabel(trimmed, scopeLabel);
            return _labels.ContainsKey(resolved) && !_constants.ContainsKey(trimmed) && !_defines.ContainsKey(trimmed);
        }
        // Expression with operators — check if any part references a label
        return false;
    }

    static IEnumerable<string> SplitDataValues(string valuesStr)
    {
        int depth = 0;
        int start = 0;
        bool inString = false;

        for (int i = 0; i < valuesStr.Length; i++)
        {
            char c = valuesStr[i];
            if (c == '"') inString = !inString;
            if (inString) continue;
            if (c == '(') depth++;
            if (c == ')') depth--;
            if (c == ',' && depth == 0)
            {
                yield return valuesStr.Substring(start, i - start);
                start = i + 1;
            }
        }
        if (start < valuesStr.Length)
            yield return valuesStr.Substring(start);
    }

    void EmitDataBlock(string label, List<byte> bytes, List<(int offset, string label)> relocations)
    {
        var block = Block.FromRawData(bytes.ToArray(), label);
        if (relocations.Count > 0)
            block.Relocations = new List<(int, string)>(relocations);
        _dataBlocks.Add(block);
    }

    #endregion

    #region Helper Methods

    int? LookupSymbol(string name)
    {
        if (_constants.TryGetValue(name, out int val)) return val;
        if (_defines.TryGetValue(name, out int dval)) return dval;
        if (_labels.TryGetValue(name, out int lval)) return lval;
        return null;
    }

    int? EvalExpr(string expr, string? scopeLabel)
    {
        return Ca65Expression.TryEvaluate(expr.AsSpan(), name =>
        {
            // Try scoped local label first
            if (name.Length > 0 && name[0] == '@' && scopeLabel != null)
            {
                var scoped = $"{scopeLabel}:{name}";
                if (_labels.TryGetValue(scoped, out int v)) return v;
            }
            return LookupSymbol(name);
        });
    }

    string ResolveLocalLabel(string name, string? scopeLabel)
    {
        if (name.Length > 0 && name[0] == '@' && scopeLabel != null)
            return $"{scopeLabel}:{name}";
        return name;
    }

    static (string? mnemonic, string? operand) SplitInstruction(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return (null, null);

        int space = trimmed.IndexOfAny(new char[] { ' ', '\t' });
        if (space < 0)
            return (trimmed, null);

        return (trimmed.Substring(0, space), trimmed.Substring(space + 1).Trim());
    }

    static string ExtractFunctionArg(string text, int fnNameLen)
    {
        var rest = text.Substring(fnNameLen).Trim();
        if (rest.Length > 0 && rest[0] == '(' && rest.Length > 0 && rest[rest.Length - 1] == ')')
            return rest.Substring(1, rest.Length - 2).Trim();
        if (rest.Length > 0 && rest[0] == '(')
        {
            int close = rest.IndexOf(')');
            if (close > 0)
                return rest.Substring(1, close - 1).Trim();
        }
        return rest;
    }

    static bool IsBranch(string mnemonic)
    {
        return mnemonic.Equals("bcc", StringComparison.OrdinalIgnoreCase) ||
               mnemonic.Equals("bcs", StringComparison.OrdinalIgnoreCase) ||
               mnemonic.Equals("beq", StringComparison.OrdinalIgnoreCase) ||
               mnemonic.Equals("bmi", StringComparison.OrdinalIgnoreCase) ||
               mnemonic.Equals("bne", StringComparison.OrdinalIgnoreCase) ||
               mnemonic.Equals("bpl", StringComparison.OrdinalIgnoreCase) ||
               mnemonic.Equals("bvc", StringComparison.OrdinalIgnoreCase) ||
               mnemonic.Equals("bvs", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '@';
    static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@';

    static bool IsValidIdentifier(string s)
    {
        if (s.Length == 0 || !IsIdentStart(s[0])) return false;
        for (int i = 1; i < s.Length; i++)
            if (!IsIdentChar(s[i])) return false;
        return true;
    }

    /// <summary>
    /// Creates a pseudo-instruction that emits a single raw byte (no opcode).
    /// Uses BRK (0x00) as a placeholder opcode — the byte is the "operand".
    /// </summary>
    static Instruction CreateDataByteInstruction(byte value)
    {
        // We abuse Instruction: BRK + Implied would be 1 byte (0x00).
        // Instead, we create a raw data wrapper. For code blocks with inline data,
        // we'll handle this differently during emission.
        // For now: use a recognizable pattern.
        return new Instruction(Opcode.BRK, AddressMode.Implied, new ImmediateOperand(value), Comment: "data");
    }

    /// <summary>
    /// Creates a pseudo-instruction that emits a 16-bit label reference (for .word in code).
    /// </summary>
    static Instruction CreateDataWordLabelInstruction(string label)
    {
        // This will be emitted as 2 bytes resolved from the label table
        return new Instruction(Opcode.BRK, AddressMode.Absolute, new LabelOperand(label, OperandSize.Word), Comment: "data.word");
    }

    #endregion

    #region Types

    enum AssemblyLineKind
    {
        Label,
        Instruction,
        Data,
        Assignment
    }

    record AssemblyLine(string Segment, AssemblyLineKind Kind, string Text, string? ScopeLabel);

    #endregion
}
