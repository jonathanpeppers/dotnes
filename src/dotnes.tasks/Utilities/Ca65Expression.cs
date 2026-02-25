using System.Globalization;

namespace dotnes;

/// <summary>
/// Evaluates ca65-style expressions: hex ($xx), decimal, binary (%xx),
/// arithmetic (+,-,*,/), bitwise (&,|,^,<<,>>), logical (&&, ||),
/// unary (<,>,~,-,!), functions (.lobyte, .hibyte), and symbol references.
/// </summary>
public static class Ca65Expression
{
    /// <summary>
    /// Evaluates an expression string, resolving symbols via the lookup function.
    /// Returns null if any symbol is unresolved.
    /// </summary>
    public static int? TryEvaluate(ReadOnlySpan<char> expr, Func<string, int?> symbolLookup)
    {
        int pos = 0;
        var trimmed = Trim(expr);
        var result = ParseLogicalOr(trimmed, ref pos, symbolLookup);
        return result;
    }

    /// <summary>
    /// Evaluates an expression, throwing if any symbol is unresolved.
    /// </summary>
    public static int Evaluate(string expr, Func<string, int?> symbolLookup)
    {
        return TryEvaluate(expr.AsSpan(), symbolLookup)
            ?? throw new InvalidOperationException($"Unresolved symbol in expression: {expr}");
    }

    static ReadOnlySpan<char> Trim(ReadOnlySpan<char> s)
    {
        return s.Trim();
    }

    static void SkipWhitespace(ReadOnlySpan<char> expr, ref int pos)
    {
        while (pos < expr.Length && char.IsWhiteSpace(expr[pos]))
            pos++;
    }

    static int? ParseLogicalOr(ReadOnlySpan<char> expr, ref int pos, Func<string, int?> lookup)
    {
        var left = ParseLogicalAnd(expr, ref pos, lookup);
        if (left == null) return null;

        while (pos < expr.Length)
        {
            SkipWhitespace(expr, ref pos);
            if (pos + 1 >= expr.Length || expr[pos] != '|' || expr[pos + 1] != '|') break;
            pos += 2;
            var right = ParseLogicalAnd(expr, ref pos, lookup);
            if (right == null) return null;
            left = (left.Value != 0 || right.Value != 0) ? 1 : 0;
        }
        return left;
    }

    static int? ParseLogicalAnd(ReadOnlySpan<char> expr, ref int pos, Func<string, int?> lookup)
    {
        var left = ParseBitwiseOr(expr, ref pos, lookup);
        if (left == null) return null;

        while (pos < expr.Length)
        {
            SkipWhitespace(expr, ref pos);
            if (pos + 1 >= expr.Length || expr[pos] != '&' || expr[pos + 1] != '&') break;
            pos += 2;
            var right = ParseBitwiseOr(expr, ref pos, lookup);
            if (right == null) return null;
            left = (left.Value != 0 && right.Value != 0) ? 1 : 0;
        }
        return left;
    }

    static int? ParseBitwiseOr(ReadOnlySpan<char> expr, ref int pos, Func<string, int?> lookup)
    {
        var left = ParseBitwiseXor(expr, ref pos, lookup);
        if (left == null) return null;

        while (pos < expr.Length)
        {
            SkipWhitespace(expr, ref pos);
            if (pos >= expr.Length || expr[pos] != '|') break;
            // Don't consume || as bitwise or
            if (pos + 1 < expr.Length && expr[pos + 1] == '|') break;
            pos++;
            var right = ParseBitwiseXor(expr, ref pos, lookup);
            if (right == null) return null;
            left = left.Value | right.Value;
        }
        return left;
    }

    static int? ParseBitwiseXor(ReadOnlySpan<char> expr, ref int pos, Func<string, int?> lookup)
    {
        var left = ParseBitwiseAnd(expr, ref pos, lookup);
        if (left == null) return null;

        while (pos < expr.Length)
        {
            SkipWhitespace(expr, ref pos);
            if (pos >= expr.Length || expr[pos] != '^') break;
            pos++;
            var right = ParseBitwiseAnd(expr, ref pos, lookup);
            if (right == null) return null;
            left = left.Value ^ right.Value;
        }
        return left;
    }

    static int? ParseBitwiseAnd(ReadOnlySpan<char> expr, ref int pos, Func<string, int?> lookup)
    {
        var left = ParseShift(expr, ref pos, lookup);
        if (left == null) return null;

        while (pos < expr.Length)
        {
            SkipWhitespace(expr, ref pos);
            if (pos >= expr.Length || expr[pos] != '&') break;
            // Don't consume && as bitwise and
            if (pos + 1 < expr.Length && expr[pos + 1] == '&') break;
            pos++;
            var right = ParseShift(expr, ref pos, lookup);
            if (right == null) return null;
            left = left.Value & right.Value;
        }
        return left;
    }

    static int? ParseShift(ReadOnlySpan<char> expr, ref int pos, Func<string, int?> lookup)
    {
        var left = ParseAddSub(expr, ref pos, lookup);
        if (left == null) return null;

        while (pos < expr.Length)
        {
            SkipWhitespace(expr, ref pos);
            if (pos + 1 >= expr.Length) break;
            if (expr[pos] == '<' && expr[pos + 1] == '<')
            {
                pos += 2;
                var right = ParseAddSub(expr, ref pos, lookup);
                if (right == null) return null;
                left = left.Value << right.Value;
            }
            else if (expr[pos] == '>' && expr[pos + 1] == '>')
            {
                pos += 2;
                var right = ParseAddSub(expr, ref pos, lookup);
                if (right == null) return null;
                left = left.Value >> right.Value;
            }
            else break;
        }
        return left;
    }

    static int? ParseAddSub(ReadOnlySpan<char> expr, ref int pos, Func<string, int?> lookup)
    {
        var left = ParseMulDiv(expr, ref pos, lookup);
        if (left == null) return null;

        while (pos < expr.Length)
        {
            SkipWhitespace(expr, ref pos);
            if (pos >= expr.Length) break;
            char op = expr[pos];
            if (op != '+' && op != '-') break;
            pos++;
            var right = ParseMulDiv(expr, ref pos, lookup);
            if (right == null) return null;
            left = op == '+' ? left.Value + right.Value : left.Value - right.Value;
        }
        return left;
    }

    static int? ParseMulDiv(ReadOnlySpan<char> expr, ref int pos, Func<string, int?> lookup)
    {
        var left = ParseUnary(expr, ref pos, lookup);
        if (left == null) return null;

        while (pos < expr.Length)
        {
            SkipWhitespace(expr, ref pos);
            if (pos >= expr.Length) break;
            char op = expr[pos];
            if (op != '*' && op != '/') break;
            pos++;
            var right = ParseUnary(expr, ref pos, lookup);
            if (right == null) return null;
            left = op == '*' ? left.Value * right.Value : left.Value / right.Value;
        }
        return left;
    }

    static int? ParseUnary(ReadOnlySpan<char> expr, ref int pos, Func<string, int?> lookup)
    {
        SkipWhitespace(expr, ref pos);
        if (pos >= expr.Length) return null;

        char c = expr[pos];

        // < prefix: low byte
        if (c == '<')
        {
            pos++;
            var val = ParseUnary(expr, ref pos, lookup);
            return val.HasValue ? val.Value & 0xFF : null;
        }

        // > prefix: high byte
        if (c == '>')
        {
            // Make sure it's not >> (shift)
            if (pos + 1 < expr.Length && expr[pos + 1] == '>') return ParsePrimary(expr, ref pos, lookup);
            pos++;
            var val = ParseUnary(expr, ref pos, lookup);
            return val.HasValue ? (val.Value >> 8) & 0xFF : null;
        }

        // ~ prefix: complement
        if (c == '~')
        {
            pos++;
            var val = ParseUnary(expr, ref pos, lookup);
            return val.HasValue ? ~val.Value : null;
        }

        // - prefix: negate (only if not followed by a digit that would be part of primary)
        // We need to distinguish unary minus from subtraction — at this level it's always unary
        if (c == '-')
        {
            pos++;
            var val = ParseUnary(expr, ref pos, lookup);
            return val.HasValue ? -val.Value : null;
        }

        // ! prefix: logical NOT
        if (c == '!')
        {
            pos++;
            var val = ParseUnary(expr, ref pos, lookup);
            return val.HasValue ? (val.Value == 0 ? 1 : 0) : null;
        }

        return ParsePrimary(expr, ref pos, lookup);
    }

    static int? ParsePrimary(ReadOnlySpan<char> expr, ref int pos, Func<string, int?> lookup)
    {
        SkipWhitespace(expr, ref pos);
        if (pos >= expr.Length) return null;

        char c = expr[pos];

        // Parenthesized expression
        if (c == '(')
        {
            pos++;
            var val = ParseBitwiseOr(expr, ref pos, lookup);
            SkipWhitespace(expr, ref pos);
            if (pos < expr.Length && expr[pos] == ')') pos++;
            return val;
        }

        // Hex literal: $xxxx
        if (c == '$')
        {
            pos++;
            int start = pos;
            while (pos < expr.Length && IsHexDigit(expr[pos])) pos++;
            if (pos == start) return null;
            return int.Parse(expr.Slice(start, pos - start).ToString(), NumberStyles.HexNumber);
        }

        // Binary literal: %xxxxxxxx
        if (c == '%')
        {
            pos++;
            int start = pos;
            while (pos < expr.Length && (expr[pos] == '0' || expr[pos] == '1')) pos++;
            if (pos == start) return null;
            return Convert.ToInt32(expr.Slice(start, pos - start).ToString(), 2);
        }

        // Decimal literal
        if (char.IsDigit(c))
        {
            int start = pos;
            while (pos < expr.Length && char.IsDigit(expr[pos])) pos++;
            return int.Parse(expr.Slice(start, pos - start).ToString());
        }

        // .lobyte() / .hibyte() functions
        if (c == '.')
        {
            if (MatchFunction(expr, ref pos, ".lobyte"))
            {
                var val = ParseParenExpr(expr, ref pos, lookup);
                return val.HasValue ? val.Value & 0xFF : null;
            }
            if (MatchFunction(expr, ref pos, ".hibyte"))
            {
                var val = ParseParenExpr(expr, ref pos, lookup);
                return val.HasValue ? (val.Value >> 8) & 0xFF : null;
            }
            // Unknown directive — treat as unresolvable
            return null;
        }

        // Symbol/identifier (letters, digits, underscores, starting with letter/underscore/@)
        if (IsIdentStart(c))
        {
            int start = pos;
            while (pos < expr.Length && IsIdentChar(expr[pos])) pos++;
            string name = expr.Slice(start, pos - start).ToString();
            return lookup(name);
        }

        return null;
    }

    static int? ParseParenExpr(ReadOnlySpan<char> expr, ref int pos, Func<string, int?> lookup)
    {
        SkipWhitespace(expr, ref pos);
        if (pos < expr.Length && expr[pos] == '(')
        {
            pos++;
            var val = ParseBitwiseOr(expr, ref pos, lookup);
            SkipWhitespace(expr, ref pos);
            if (pos < expr.Length && expr[pos] == ')') pos++;
            return val;
        }
        // If no parens, just parse a primary (ca65 allows .lobyte expr without parens)
        return ParseUnary(expr, ref pos, lookup);
    }

    static bool MatchFunction(ReadOnlySpan<char> expr, ref int pos, string name)
    {
        if (pos + name.Length > expr.Length) return false;
        if (!expr.Slice(pos, name.Length).Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;
        // Make sure it's not a longer identifier
        if (pos + name.Length < expr.Length && IsIdentChar(expr[pos + name.Length])) return false;
        pos += name.Length;
        return true;
    }

    static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '@';
    static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@';
}
