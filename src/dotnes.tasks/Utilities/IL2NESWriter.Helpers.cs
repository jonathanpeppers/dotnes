using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static NES.NESLib;
using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;

namespace dotnes;

/// <summary>
/// Static helper methods and utilities.
/// </summary>
partial class IL2NESWriter
{
    /// <summary>
    /// Gets the local index for a Stloc opcode, or null if not a Stloc.
    /// </summary>
    static int? GetStlocIndex(ILOpCode opCode) => opCode switch
    {
        ILOpCode.Stloc_0 => 0,
        ILOpCode.Stloc_1 => 1,
        ILOpCode.Stloc_2 => 2,
        ILOpCode.Stloc_3 => 3,
        ILOpCode.Stloc => null, // Would need operand
        ILOpCode.Stloc_s => null, // Would need operand  
        _ => null
    };

    /// <summary>
    /// Gets the local index from a Ldloc instruction.
    /// </summary>
    static int? GetLdlocIndex(ILInstruction instr) => instr.OpCode switch
    {
        ILOpCode.Ldloc_0 => 0,
        ILOpCode.Ldloc_1 => 1,
        ILOpCode.Ldloc_2 => 2,
        ILOpCode.Ldloc_3 => 3,
        ILOpCode.Ldloc_s => instr.Integer,
        _ => null
    };

    static int? GetLdcValue(ILInstruction instr) => instr.OpCode switch
    {
        ILOpCode.Ldc_i4_m1 => -1,
        ILOpCode.Ldc_i4_0 => 0,
        ILOpCode.Ldc_i4_1 => 1,
        ILOpCode.Ldc_i4_2 => 2,
        ILOpCode.Ldc_i4_3 => 3,
        ILOpCode.Ldc_i4_4 => 4,
        ILOpCode.Ldc_i4_5 => 5,
        ILOpCode.Ldc_i4_6 => 6,
        ILOpCode.Ldc_i4_7 => 7,
        ILOpCode.Ldc_i4_8 => 8,
        ILOpCode.Ldc_i4_s => instr.Integer,
        ILOpCode.Ldc_i4 => instr.Integer,
        _ => null
    };

    /// <summary>
    /// Returns a user-friendly error message for an unsupported IL opcode, explaining
    /// what C# pattern likely generated it and what to use instead.
    /// </summary>
    internal static string GetUnsupportedOpcodeMessage(ILOpCode opCode) => opCode switch
    {
        ILOpCode.Conv_i8 =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by using 'long' (64-bit integer). " +
            "Use 'byte', 'sbyte', 'ushort', or 'int' instead.",
        ILOpCode.Conv_r4 or ILOpCode.Conv_r8 =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by using 'float' or 'double' (floating-point types). " +
            "The NES has no floating-point hardware. Use integer types ('byte', 'ushort', 'int') instead.",
        ILOpCode.Conv_r_un =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by converting an unsigned integer to a floating-point type. " +
            "The NES has no floating-point hardware. Use integer types ('byte', 'ushort', 'int') instead.",
        ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_i_un or ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i1_un or
        ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_i2_un or ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_i4_un or
        ILOpCode.Conv_ovf_i8 or ILOpCode.Conv_ovf_i8_un or ILOpCode.Conv_ovf_u or ILOpCode.Conv_ovf_u_un or
        ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u1_un or ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u2_un or
        ILOpCode.Conv_ovf_u4 or ILOpCode.Conv_ovf_u4_un or ILOpCode.Conv_ovf_u8 or ILOpCode.Conv_ovf_u8_un =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by a 'checked' arithmetic expression or cast. " +
            "Use unchecked arithmetic instead (the NES does not support overflow checking).",
        ILOpCode.Box or ILOpCode.Unbox or ILOpCode.Unbox_any =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by boxing a value type (e.g., passing an 'int' as 'object'). " +
            "Avoid using 'object', generics, or interfaces that require boxing.",
        ILOpCode.Castclass or ILOpCode.Isinst =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by type casting ('as', 'is', or explicit cast to a reference type). " +
            "Avoid reference type casts; use only primitive types.",
        ILOpCode.Throw or ILOpCode.Rethrow =>
            $"The IL opcode '{opCode}' is not supported. " +
            "Exception handling ('throw', 'try/catch') is not supported on the NES.",
        ILOpCode.Leave or ILOpCode.Leave_s or ILOpCode.Endfinally or ILOpCode.Endfilter =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by 'try/catch/finally' blocks. " +
            "Exception handling is not supported on the NES.",
        ILOpCode.Newobj =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by creating an object with 'new' (e.g., 'new List<byte>()'). " +
            "Only 'byte[]', 'ushort[]', and struct arrays can be created. Avoid classes and reference types.",
        ILOpCode.Callvirt =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by calling an instance method or virtual method on an object. " +
            "Use only static methods and NESLib API calls.",
        ILOpCode.Ldlen =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by accessing '.Length' on an array. " +
            "Track array length in a separate variable instead.",
        ILOpCode.Neg =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by the unary negation operator ('-x'). " +
            "Rewrite the expression using explicit two's-complement negation, such as '0 - x' (and cast to the desired width, e.g., '(byte)(0 - x)' or '(ushort)(0 - x)').",
        ILOpCode.Not =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by the bitwise NOT operator ('~x'). " +
            "Use XOR with an all-ones mask instead (for a byte: 'x ^ 0xFF').",
        ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un =>
            $"The IL opcode '{opCode}' is not supported. " +
            "This is typically caused by a comparison expression used as a value (e.g., 'bool b = x > y;'). " +
            "Use comparisons only in 'if' or 'while' conditions, not as standalone values.",
        _ =>
            $"The IL opcode '{opCode}' is not yet supported. This C# feature cannot be transpiled to 6502 assembly."
    };
}
