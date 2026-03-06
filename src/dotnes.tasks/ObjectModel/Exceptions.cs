namespace dotnes.ObjectModel;

/// <summary>
/// Exception thrown when the transpiler encounters an unsupported C# feature or pattern.
/// Provides actionable error messages that name the C# feature and suggest workarounds.
/// </summary>
public class TranspileException : Exception
{
    /// <summary>
    /// The name of the method being transpiled when the error occurred, if available.
    /// </summary>
    public string? MethodName { get; }

    public TranspileException(string message, string? methodName = null)
        : base(FormatMessage(message ?? "An unsupported C# feature was encountered during transpilation.", methodName))
    {
        MethodName = methodName;
    }

    static string FormatMessage(string message, string? methodName)
    {
        if (methodName != null)
            return $"In method '{methodName}': {message}";
        return message;
    }
}

/// <summary>
/// Exception thrown when a label cannot be resolved
/// </summary>
public class UnresolvedLabelException : Exception
{
    public string Label { get; }

    public UnresolvedLabelException(string label)
        : base($"Unresolved label: '{label}'")
    {
        Label = label;
    }
}

/// <summary>
/// Exception thrown when a branch target is out of range (-128 to +127)
/// </summary>
public class BranchOutOfRangeException : Exception
{
    public string Label { get; }
    public int Offset { get; }

    public BranchOutOfRangeException(string label, int offset)
        : base($"Branch to '{label}' is out of range: offset {offset} (must be -128 to +127)")
    {
        Label = label;
        Offset = offset;
    }
}

/// <summary>
/// Exception thrown when a duplicate label is defined
/// </summary>
public class DuplicateLabelException : Exception
{
    public string Label { get; }

    public DuplicateLabelException(string label)
        : base($"Duplicate label: '{label}'")
    {
        Label = label;
    }
}

/// <summary>
/// Exception thrown when an invalid opcode/addressing mode combination is used
/// </summary>
public class InvalidOpcodeAddressModeException : Exception
{
    public Opcode Opcode { get; }
    public AddressMode Mode { get; }

    public InvalidOpcodeAddressModeException(Opcode opcode, AddressMode mode)
        : base($"Invalid addressing mode {mode} for opcode {opcode}")
    {
        Opcode = opcode;
        Mode = mode;
    }
}

/// <summary>
/// Exception thrown when decoding an unknown opcode byte
/// </summary>
public class UnknownOpcodeException : Exception
{
    public byte EncodedOpcode { get; }

    public UnknownOpcodeException(byte encodedOpcode)
        : base($"Unknown opcode: ${encodedOpcode:X2}")
    {
        EncodedOpcode = encodedOpcode;
    }
}
