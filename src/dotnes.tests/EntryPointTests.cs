using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit.Abstractions;

namespace dotnes.tests;

public class EntryPointTests(ITestOutputHelper output) : RoslynTests(output)
{
    [Theory]
    [InlineData("Startup")]
    [InlineData("<StartupCode$test>")]
    public void PeEntryPointProvidesMainBodyAndNumericSignature(string typeName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("startup.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("startup"), new Version(1, 0),
            default, default, default, default);
        var neslib = metadata.AddAssemblyReference(metadata.GetOrAddString("neslib"),
            new Version(1, 0), default, default, default, default);
        var nesType = metadata.AddTypeReference(neslib,
            metadata.GetOrAddString("NES"), metadata.GetOrAddString("NESLib"));

        var pokeSignature = new BlobBuilder();
        new BlobEncoder(pokeSignature).MethodSignature().Parameters(2,
            result => result.Void(), parameters =>
            {
                parameters.AddParameter().Type().UInt16();
                parameters.AddParameter().Type().Byte();
            });
        var poke = metadata.AddMemberReference(nesType,
            metadata.GetOrAddString("poke"), metadata.GetOrAddBlob(pokeSignature));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(0,
            result => result.Void(), _ => { });

        var bodies = new BlobBuilder();
        var instructions = new InstructionEncoder(new BlobBuilder(), new ControlFlowBuilder());
        instructions.LoadConstantI4(0x6000);
        instructions.LoadConstantI4(42);
        instructions.Call(poke);
        var loop = instructions.DefineLabel();
        instructions.MarkLabel(loop);
        instructions.Branch(ILOpCode.Br_s, loop);
        int body = new MethodBodyStreamEncoder(bodies).AddMethodBody(instructions);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default,
            metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(TypeAttributes.Public, default,
            metadata.GetOrAddString(typeName), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var entry = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL, metadata.GetOrAddString("main@"),
            metadata.GetOrAddBlob(signature), body, MetadataTokens.ParameterHandle(1));
        var image = new BlobBuilder();
        new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata), bodies, entryPoint: entry).Serialize(image);

        using var stream = new MemoryStream(image.ToArray());
        using var transpiler = new Transpiler(stream,
            [new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s")))], _logger);
        var program = transpiler.BuildProgram6502(out _, out _);
        Assert.Equal(PrimitiveTypeCode.Void, transpiler.NumericTypes["main"].ReturnType);
        Assert.DoesNotContain("main@", transpiler.UserMethods.Keys);
        byte[] code = program.ToBytes();
        Assert.True(program.Labels.TryResolve("main", out ushort main));
        var cpu = new Cpu6502(code, program.BaseAddress, main);
        for (int i = 0; i < 100; i++)
            cpu.Step();
        Assert.Equal(42, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFF, cpu.SP);
    }
}
