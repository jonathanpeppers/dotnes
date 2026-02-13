// Usage: dotnet run scripts/ildump.cs <path-to-dll>
// Dumps IL opcodes for all methods in a .NET assembly.
// If no DLL path provided, compiles inline C# source and dumps that.

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

string[] opcodeNames = new string[512];
foreach (var field in typeof(ILOpCode).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
{
    if (field.GetValue(null) is ILOpCode code)
    {
        int index = (int)code;
        if (index >= 0xFE00) index = 256 + (index & 0xFF);
        if (index < opcodeNames.Length) opcodeNames[index] = field.Name.ToLowerInvariant();
    }
}

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: dotnet run scripts/ildump.cs <path-to-dll>");
    return;
}

var bytes = File.ReadAllBytes(args[0]);
var pe = new PEReader(ImmutableArray.Create(bytes));
var mr = pe.GetMetadataReader();

foreach (var mh in mr.MethodDefinitions)
{
    var md = mr.GetMethodDefinition(mh);
    var name = mr.GetString(md.Name);
    
    // Skip property accessors and other compiler-generated methods
    if (name.StartsWith("get_") || name.StartsWith("set_")) continue;
    
    if (md.RelativeVirtualAddress == 0) continue;
    var body = pe.GetMethodBody(md.RelativeVirtualAddress);
    var il = body.GetILBytes();
    if (il == null || il.Length == 0) continue;

    Console.WriteLine($"--- {name} (IL size: {il.Length}) ---");
    
    int i = 0;
    while (i < il.Length)
    {
        int offset = i;
        int opByte = il[i++];
        int opIndex = opByte;
        if (opByte == 0xFE)
        {
            opByte = il[i++];
            opIndex = 256 + opByte;
        }
        
        string opName = opIndex < opcodeNames.Length && opcodeNames[opIndex] != null 
            ? opcodeNames[opIndex] 
            : $"???({opIndex:X2})";

        // Determine operand size from opcode
        int operandSize = GetOperandSize(opName, opIndex);
        
        string operandStr = "";
        if (operandSize == 1 && i < il.Length)
        {
            byte val = il[i];
            if (opName.Contains("br") || opName.Contains("leave"))
            {
                sbyte sval = (sbyte)val;
                operandStr = $" {sval:+0;-0;0} (-> IL_{i + 1 + sval:X4})";
            }
            else
                operandStr = $" 0x{val:X2}";
            i++;
        }
        else if (operandSize == 2 && i + 1 < il.Length)
        {
            operandStr = $" 0x{BitConverter.ToInt16(il, i):X4}";
            i += 2;
        }
        else if (operandSize == 4 && i + 3 < il.Length)
        {
            int val = BitConverter.ToInt32(il, i);
            if (opName.Contains("br") || opName == "leave")
                operandStr = $" {val:+0;-0;0} (-> IL_{i + 4 + val:X4})";
            else if (opName == "call" || opName == "callvirt" || opName == "newobj" || opName == "ldtoken" || opName == "ldfld" || opName == "stfld" || opName == "newarr")
            {
                var handle = MetadataTokens.EntityHandle(val);
                operandStr = $" [{ResolveToken(mr, handle)}]";
            }
            else if (opName == "ldstr")
            {
                var str = mr.GetUserString(MetadataTokens.UserStringHandle(val));
                operandStr = $" \"{str}\"";
            }
            else
                operandStr = $" 0x{val:X8}";
            i += 4;
        }
        else if (operandSize == 8 && i + 7 < il.Length)
        {
            operandStr = $" 0x{BitConverter.ToInt64(il, i):X16}";
            i += 8;
        }
        else if (operandSize > 0)
        {
            i += operandSize;
        }

        Console.WriteLine($"  IL_{offset:X4}: {opName}{operandStr}");
    }
    Console.WriteLine();
}

static string ResolveToken(MetadataReader mr, EntityHandle handle)
{
    switch (handle.Kind)
    {
        case HandleKind.MemberReference:
            return mr.GetString(mr.GetMemberReference((MemberReferenceHandle)handle).Name);
        case HandleKind.MethodDefinition:
            return mr.GetString(mr.GetMethodDefinition((MethodDefinitionHandle)handle).Name);
        case HandleKind.TypeReference:
            return mr.GetString(mr.GetTypeReference((TypeReferenceHandle)handle).Name);
        case HandleKind.TypeDefinition:
            return mr.GetString(mr.GetTypeDefinition((TypeDefinitionHandle)handle).Name);
        case HandleKind.FieldDefinition:
            return mr.GetString(mr.GetFieldDefinition((FieldDefinitionHandle)handle).Name);
        default:
            return $"token:{MetadataTokens.GetToken(handle):X8}";
    }
}

static int GetOperandSize(string name, int opIndex)
{
    // Short branch targets, short inline vars, short inline ints
    if (name.EndsWith("_s") && (name.Contains("br") || name.Contains("leave"))) return 1;
    if (name == "ldc_i4_s") return 1;
    if (name == "ldloc_s" || name == "stloc_s" || name == "ldarg_s" || name == "starg_s") return 1;
    
    // Long branch targets
    if (name == "br" || name == "leave" || name == "brtrue" || name == "brfalse" 
        || name == "beq" || name == "bne_un" || name == "bge" || name == "bge_un" 
        || name == "bgt" || name == "bgt_un" || name == "ble" || name == "ble_un" 
        || name == "blt" || name == "blt_un") return 4;
    
    // 4-byte operands: call, token, field, type, string, int32
    if (name == "call" || name == "callvirt" || name == "newobj" || name == "ldfld" || name == "stfld"
        || name == "ldsfld" || name == "stsfld" || name == "ldtoken" || name == "newarr"
        || name == "ldc_i4" || name == "ldstr" || name == "castclass" || name == "isinst"
        || name == "ldftn" || name == "ldvirtftn" || name == "stobj" || name == "ldobj"
        || name == "box" || name == "unbox" || name == "unbox_any" || name == "initobj"
        || name == "constrained_") return 4;
    
    // 8-byte: ldc.i8, ldc.r8
    if (name == "ldc_i8" || name == "ldc_r8") return 8;
    
    // 4-byte: ldc.r4
    if (name == "ldc_r4") return 4;
    
    // 2-byte: ldloc, stloc, ldarg, starg (non-short forms via FE prefix)
    if (opIndex >= 256 && (name == "ldloc" || name == "stloc" || name == "ldarg" || name == "starg")) return 2;
    
    // No operand for most other opcodes (ldloc.0, stloc.0, ldc.i4.0, add, sub, and, or, etc.)
    return 0;
}
