using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace dotnes.tests;

/// <summary>
/// Initializes Verify settings for the test assembly.
/// Adds an OnVerifyMismatch handler that shows disassembled 6502 code when binary comparisons fail.
/// </summary>
public static class VerifyInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // When a binary comparison fails, show the disassembly in the error output
        // This keeps .verified.bin as the source of truth but makes failures readable
        VerifierSettings.OnVerifyMismatch((filePair, message, autoVerify) =>
        {
            if (filePair.ReceivedPath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var receivedBytes = File.ReadAllBytes(filePair.ReceivedPath);
                    var receivedDisasm = Disassembler6502.DisassembleRom(receivedBytes);
                    
                    // Write to multiple outputs to ensure visibility
                    var output = $"\n=== RECEIVED ROM DISASSEMBLY ===\n{receivedDisasm}";
                    Console.WriteLine(output);
                    Debug.WriteLine(output);
                    
                    // Also write a .txt file with the disassembly for easy viewing
                    var txtPath = filePair.ReceivedPath.Replace(".bin", ".txt");
                    File.WriteAllText(txtPath, receivedDisasm);
                    
                    if (File.Exists(filePair.VerifiedPath))
                    {
                        var verifiedBytes = File.ReadAllBytes(filePair.VerifiedPath);
                        var verifiedDisasm = Disassembler6502.DisassembleRom(verifiedBytes);
                        
                        output = $"\n=== VERIFIED ROM DISASSEMBLY ===\n{verifiedDisasm}";
                        Console.WriteLine(output);
                        Debug.WriteLine(output);
                        
                        // Write expected.txt for comparison (not .verified.txt to avoid Verify detecting it)
                        var expectedTxtPath = filePair.ReceivedPath.Replace(".received.bin", ".expected.txt");
                        File.WriteAllText(expectedTxtPath, verifiedDisasm);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not disassemble: {ex.Message}");
                }
            }
            return Task.CompletedTask;
        });
    }
}
