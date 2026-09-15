// Usage: dotnet run scripts/test-package.cs -- candidate.nupkg [baseline.nupkg]
//        [--artifacts directory] [--timeout-seconds 300]
// Requires a .NET 10+ SDK, the .NET 10 runtime, and nuget.org. Child commands honor
// DOTNET_HOST_PATH (when set), otherwise resolve dotnet using the inherited PATH.
// All generated projects, isolated caches, logs and
// test results are retained under artifacts/pkg-<unique run> by default.
// No repository project is built, and no existing package cache is used.

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

try
{
    var options = Options.Parse(args);
    await new PackageTests(options).Run();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {ex.Message}");
    return 1;
}

sealed record Options(string Candidate, string? Baseline, string Artifacts, string Repository, int TimeoutSeconds)
{
    public static Options Parse(string[] args)
    {
        var positional = new List<string>();
        string? artifacts = null;
        int timeout = 300;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--")
                continue;
            if (args[i] == "--artifacts" && i + 1 < args.Length)
                artifacts = Path.GetFullPath(args[++i]);
            else if (args[i] == "--timeout-seconds" && i + 1 < args.Length && int.TryParse(args[++i], out int seconds) && seconds > 0)
                timeout = seconds;
            else if (args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unknown/incomplete option: {args[i]}");
            else
                positional.Add(Path.GetFullPath(args[i]));
        }
        if (positional.Count is < 1 or > 2 || positional.Any(p => !File.Exists(p)))
            throw new ArgumentException("Usage: dotnet run scripts/test-package.cs -- candidate.nupkg [baseline.nupkg] [--artifacts directory] [--timeout-seconds 300]");
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "samples", "hello", "Program.cs")))
            directory = directory.Parent;
        if (directory == null)
            throw new ArgumentException("Run this script from the dotnes repository or one of its subdirectories.");
        // Keep paths short enough for Windows tools which still use MAX_PATH.
        artifacts ??= Path.Combine(directory.FullName, "artifacts", $"pkg-{Guid.NewGuid():N}"[..12]);
        if (Directory.Exists(artifacts) && Directory.EnumerateFileSystemEntries(artifacts).Any())
            throw new ArgumentException($"Artifacts directory must be new or empty: {artifacts}");
        return new(positional[0], positional.ElementAtOrDefault(1), artifacts, directory.FullName, timeout);
    }
}

sealed class PackageTests(Options options)
{
    const string TestSdkVersion = "18.9.0";
    const string XunitVersion = "2.9.3";
    const string XunitRunnerVersion = "4.0.0";
    const string RoslynVersion = "5.9.0";
    const string NativeSource = """
        .segment "CODE"
        _native_write:
            lda #$42
            sta $6000
            rts
        .segment "CHARS"
        .byte $12,$34
        """;
    const string PropertyNames = "IsTestProject,OutputType,NoStdLib,Optimize,DebugSymbols,DebugType,AllowUnsafeBlocks,NoWarn,ImplicitUsings,Nullable,ComputeNETCoreBuildOutputFiles,GenerateDependencyFile,GenerateRuntimeConfigurationFiles,ProduceReferenceAssembly,CopyLocalLockFileAssemblies,TargetPath,NESTargetPath,_NESPropertiesStampFile,CustomAfterMicrosoftCommonProps,PackageHookImported,PackageHookSawIsTestProject";
    const string ItemNames = "Analyzer,Using,ReferencePath,ReferenceCopyLocalPaths,CscCommandLineArgs,NESAssembly";
    static readonly string[] DesktopProperties = [
        "IsTestProject", "OutputType", "NoStdLib", "Optimize", "DebugSymbols", "DebugType",
        "AllowUnsafeBlocks", "NoWarn", "ImplicitUsings", "Nullable", "ComputeNETCoreBuildOutputFiles",
        "GenerateDependencyFile", "GenerateRuntimeConfigurationFiles", "ProduceReferenceAssembly", "CopyLocalLockFileAssemblies"
    ];
    static readonly string[] CompilerSwitches = ["/optimize", "/debug", "/unsafe", "/nostdlib", "/target:", "/nullable:", "/nowarn:"];
    int processNumber;
    int checks;
    Package candidate = null!;
    string candidateRoot = "";
    string compilerHash = "";
    string compilerVersion = "";
    readonly string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";

    public async Task Run()
    {
        Directory.CreateDirectory(options.Artifacts);
        Console.WriteLine($"Artifacts: {options.Artifacts}");
        // Stop repository Directory.Build.* and NuGet configuration from leaking into
        // ordinary generated SDK projects. Consumers contain no special imports/targets.
        Write(options.Artifacts, "Directory.Build.props", "<Project />");
        Write(options.Artifacts, "Directory.Build.targets", "<Project />");
        Write(options.Artifacts, "Directory.Packages.props", "<Project />");
        var sdk = await RunProcess(options.Artifacts, "sdk", "--version");
        string sdkVersion = sdk.Output.Trim();
        Check(Version.TryParse(sdkVersion.Split('-')[0], out var parsedSdk) && parsedSdk.Major >= 10,
            $"A .NET 10 or newer SDK is required; found {sdkVersion}");
        Console.WriteLine($"SDK: {sdkVersion}; dotnet host: {dotnetHost}");
        candidateRoot = Path.Combine(options.Artifacts, "candidate");
        candidate = PreparePackage(options.Candidate, candidateRoot, "candidate", requireCompiler: true);
        Package? baseline = options.Baseline == null ? null :
            PreparePackage(options.Baseline, Path.Combine(options.Artifacts, "baseline"), "baseline", requireCompiler: false);
        await CheckRomPropertyOverrides(baseline);

        string automatic = CreateProject(candidateRoot, "AutoTests", TestProperties(), TestPackages(candidate), TestSource());
        string hookTest = CreateProject(candidateRoot, "HookTests", TestProperties(), TestPackages(candidate), TestSource());
        // This is an existing consumer hook, not a workaround for package imports.
        Write(ProjectDirectory(hookTest), "Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <CustomAfterMicrosoftCommonProps>$(MSBuildThisFileDirectory)ExistingCommonHook.props</CustomAfterMicrosoftCommonProps>
              </PropertyGroup>
            </Project>
            """);
        Write(ProjectDirectory(hookTest), "ExistingCommonHook.props", """
            <Project>
              <PropertyGroup>
                <PackageHookImported>$(PackageHookImported)x</PackageHookImported>
                <PackageHookSawIsTestProject>$(IsTestProject)</PackageHookSawIsTestProject>
              </PropertyGroup>
            </Project>
            """);
        string explicitProperties = TestProperties(explicitTest: true) + """
            <Optimize Condition="'$(Configuration)' == 'Debug'">true</Optimize>
            <Optimize Condition="'$(Configuration)' == 'Release'">false</Optimize>
            <DebugType>embedded</DebugType>
            """;
        string explicitTest = CreateProject(candidateRoot, "ExplicitTests", explicitProperties, TestPackages(candidate), TestSource());
        string explicitControl = CreateProject(candidateRoot, "ExplicitControl", explicitProperties, TestPackages(null), """
            using Xunit;
            public sealed class ControlTests { [Fact] public void OrdinaryClr() => Assert.Equal(6, new[] { 1, 2, 3 }.Sum()); }
            """);
        string testControl = CreateProject(candidateRoot, "TestControl", TestProperties(), TestPackages(null), """
            using Xunit;
            public sealed class ControlTests { [Fact] public void OrdinaryClr() => Assert.Equal(6, new[] { 1, 2, 3 }.Sum()); }
            """);
        string host = CreateProject(candidateRoot, "MarkedHost", TestProperties(), PackageReferences(candidate, roslyn: true), BridgeSource());
        string hostControl = CreateProject(candidateRoot, "HostControl", TestProperties(), "", """
            public sealed class OrdinaryClass { public string Value => $"{new[] { 1, 2, 3 }.Sum()}"; }
            """);
        foreach (string earlyMarkedProject in new[] { host, hostControl })
            Write(ProjectDirectory(earlyMarkedProject), "Directory.Build.props", """
                <Project>
                  <PropertyGroup>
                    <IsTestProject>true</IsTestProject>
                  </PropertyGroup>
                </Project>
                """);
        string transitive = CreateProject(candidateRoot, "TransitiveTests", TestProperties(),
            TestPackages(null) + """<ProjectReference Include="..\MarkedHost\MarkedHost.csproj" />""", """
            using Xunit;
            public sealed class TransitiveTests
            {
                [Fact]
                public void CompilerRunsThroughProjectReference()
                {
                    Assert.Equal("A9428D006060", Convert.ToHexString(PackageBridge.NativeBytes()));
                    Assert.Equal("desktop:6", PackageBridge.Desktop());
                    Assert.Equal("dotnes.tasks", typeof(dotnes.NesCompiler).Assembly.GetName().Name);
                    Assert.True(File.Exists(typeof(NES.NESLib).Assembly.Location));
                }
            }
            """);

        foreach (string project in new[] { automatic, explicitTest, explicitControl, hookTest, testControl, host, hostControl, transitive })
            await Restore(project);
        await CheckImportOrder(automatic, explicitTest, host);

        foreach (string configuration in new[] { "Debug", "Release" })
        {
            JsonElement expectedTest = await DesignTime(testControl, configuration);
            JsonElement expectedExplicit = await DesignTime(explicitControl, configuration);
            JsonElement expectedHost = await DesignTime(hostControl, configuration);
            Check(Property(expectedTest, "Optimize") == (configuration == "Release" ? "true" : "false"),
                $"{configuration}: control SDK Optimize default changed.");
            foreach (string project in new[] { automatic, explicitTest, hookTest, host, transitive })
            {
                JsonElement evaluated = await DesignTime(project, configuration);
                CheckDesktop(project, configuration, evaluated, project == host ? expectedHost : project == explicitTest ? expectedExplicit : expectedTest);
                if (project == hookTest)
                {
                    Check(Property(evaluated, "PackageHookImported") == "x", "dotnes must preserve and import the existing CustomAfterMicrosoftCommonProps hook exactly once.");
                    Check(Property(evaluated, "PackageHookSawIsTestProject") == "true", "The existing common-props hook ran before Test SDK props.");
                }
                await Build(project, configuration);
                CheckDesktopOutputs(project, configuration, evaluated);
                CheckResolvedAssets(project, candidate);
                if (project != host)
                    await Test(project, configuration, project == transitive ? 1 : 5);
                NoRomArtifacts(project);
            }
        }
        Console.WriteLine("PASS: auto/explicit Test SDK detection, early-marked no-Test-SDK host, SDK hook chaining, desktop settings, design-time compiler args, discovery and transitive runtime.");
        await CheckLateClassificationErrors();

        string libraryRom = CreateProject(candidateRoot, "LibraryRom", "<OutputType>Library</OutputType>", PackageReferences(candidate), "while (true) ;");
        await Restore(libraryRom);
        JsonElement library = await Evaluate(libraryRom, "Debug", resolveReferences: true);
        Check(Property(library, "IsTestProject") != "true", "OutputType=Library must not imply IsTestProject.");
        Check(Property(library, "NoStdLib") == "true" && Property(library, "Optimize") == "true", "Non-test Library lost ROM defaults.");
        Check(Property(library, "DebugType").Equals("none", StringComparison.OrdinalIgnoreCase), "Non-test Library must retain ROM DebugType=None.");
        Check(Items(library, "Analyzer").Any(IsNesAnalyzer), "Non-test Library must keep the NES analyzer.");

        await CheckAnalyzerExclusion(baseline);

        string hello = await BuildHello(candidateRoot, candidate);
        string baselineFile = Path.Combine(options.Repository, "src", "dotnes.tests", "TranspilerTests.Write.hello.verified.bin");
        if (baseline != null)
            baselineFile = await BuildHello(baseline.Root, baseline);
        Check(File.ReadAllBytes(hello).AsSpan().SequenceEqual(File.ReadAllBytes(baselineFile)),
            $"hello ROM differs byte-for-byte from {baselineFile}. Candidate: {hello}. Never update an unchanged snapshot to fix this.");

        string removedAssembly = CreateProject(candidateRoot, "RemovedAssemblyRom", "<OutputType>Exe</OutputType>",
            PackageReferences(candidate) + """<NESAssembly Remove="excluded.s" />""", "");
        foreach (string file in new[] { "Program.cs", "chr_generic.s" })
            File.Copy(Path.Combine(options.Repository, "samples", "hello", file), Path.Combine(ProjectDirectory(removedAssembly), file), overwrite: true);
        Write(ProjectDirectory(removedAssembly), "excluded.s", """
            .segment "CODE"
            _must_not_be_assembled:
                invalid_opcode
            """);
        await Restore(removedAssembly);
        JsonElement removedItems = await Evaluate(removedAssembly, "Release");
        Check(Identities(removedItems, "NESAssembly").Any(a => Path.GetFileName(a) == "chr_generic.s"),
            "Default NESAssembly glob no longer includes the CHR input.");
        Check(!Identities(removedItems, "NESAssembly").Any(a => Path.GetFileName(a) == "excluded.s"),
            "Late package defaults re-added the NESAssembly removed in the project body.");
        await Build(removedAssembly, "Release", rom: true);
        Check(File.ReadAllBytes(RomPath(removedAssembly, "Release")).AsSpan().SequenceEqual(File.ReadAllBytes(hello)),
            "Project-body NESAssembly Remove changed the resulting hello ROM.");
        Console.WriteLine("PASS: project-body NESAssembly Remove survives package evaluation and produces the unchanged hello ROM.");

        string native = CreateProject(candidateRoot, "NativeRom", "<OutputType>Exe</OutputType>", PackageReferences(candidate), """
            static extern void native_write();
            native_write();
            ppu_off();
            while (true) ;
            """);
        Write(ProjectDirectory(native), "native.s", NativeSource);
        await Restore(native);
        await Build(native, "Release", rom: true);
        byte[] nativeRom = File.ReadAllBytes(RomPath(native, "Release"));
        Check(nativeRom.Length >= 16 + 32768 + 8192 && nativeRom.AsSpan(0, 4).SequenceEqual(new byte[] { 0x4E, 0x45, 0x53, 0x1A }),
            "Native ROM lacks the expected iNES header/PRG/CHR layout.");
        Check(nativeRom.AsSpan(16, 32768).IndexOf(new byte[] { 0xA9, 0x42, 0x8D, 0x00, 0x60, 0x60 }) >= 0, "Native .s machine code was not embedded in ROM.");
        Check(nativeRom.AsSpan(16 + 32768, 2).SequenceEqual(new byte[] { 0x12, 0x34 }), "Native .s CHARS asset was not embedded in ROM.");
        Console.WriteLine($"PASS: hello ROM unchanged ({Hash(File.ReadAllBytes(hello))}); native source and CHR assets work.");

        string invalid = CreateProject(candidateRoot, "InvalidRom", "<OutputType>Exe</OutputType>", PackageReferences(candidate), """
            ppu_off();
            while (true) ;
            public class InvalidNesClass { public int Value => 42; }
            """);
        File.Copy(Path.Combine(options.Repository, "samples", "hello", "chr_generic.s"), Path.Combine(ProjectDirectory(invalid), "chr_generic.s"));
        await Restore(invalid);
        var failure = await RunProcess(ProjectDirectory(invalid), "invalid-rom",
            ["build", invalid, "--no-restore", "--disable-build-servers", "-v:minimal", "-nr:false", "-p:UseSharedCompilation=false"], expectedSuccess: false);
        Check(!failure.Output.Contains("CS9057", StringComparison.Ordinal),
            $"The SDK rejected the packaged NES analyzer (CS9057), so NES002 cannot protect ROM builds. Use an analyzer compatible with the supported SDK. {failure.Log}");
        Check(failure.ExitCode != 0 && failure.Output.Contains("error NES002", StringComparison.Ordinal), "Invalid ROM class must fail compilation with NES002. " + failure.Log);
        Check(!HasTranspileMessage(failure.Output), "Invalid ROM reached transpilation instead of failing in the NES analyzer.");
        NoRomArtifacts(invalid);
        Console.WriteLine("PASS: Library is not a test opt-out; invalid ROM still fails NES002 before transpilation.");
        Console.WriteLine($"PASS: {checks} package regression checks. Logs, evaluated items, generated projects and TRX retained at {options.Artifacts}");
    }

    Package PreparePackage(string path, string root, string label, bool requireCompiler)
    {
        Directory.CreateDirectory(root);
        using var zip = ZipFile.OpenRead(path);
        var nuspecEntry = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var nuspecStream = nuspecEntry.Open();
        var nuspec = XDocument.Load(nuspecStream);
        var metadata = nuspec.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
        string id = metadata.Elements().Single(e => e.Name.LocalName == "id").Value;
        string version = metadata.Elements().Single(e => e.Name.LocalName == "version").Value;
        Check(id == "dotnes", $"{path} is {id}, not dotnes.");
        string commit = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "repository")?.Attribute("commit")?.Value ?? "(not recorded)";
        string feed = Path.Combine(root, "feed");
        Directory.CreateDirectory(feed);
        File.Copy(path, Path.Combine(feed, $"dotnes.{version}.nupkg"));
        Write(root, "NuGet.Config", new XDocument(
            new XElement("configuration",
                new XElement("packageSources", new XElement("clear"),
                    new XElement("add", new XAttribute("key", "candidate"), new XAttribute("value", feed)),
                    new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))),
                new XElement("fallbackPackageFolders", new XElement("clear")),
                new XElement("packageSourceMapping", new XElement("clear"),
                    new XElement("packageSource", new XAttribute("key", "candidate"), new XElement("package", new XAttribute("pattern", "dotnes"))),
                    new XElement("packageSource", new XAttribute("key", "nuget.org"), new XElement("package", new XAttribute("pattern", "*")))),
                new XElement("config", new XElement("add", new XAttribute("key", "globalPackagesFolder"), new XAttribute("value", Path.Combine(root, "packages"))))
        )).ToString());
        string hash = Hash(File.ReadAllBytes(path));
        Console.WriteLine($"{label}: dotnes {version}; repository commit {commit}; package SHA256 {hash}");
        if (requireCompiler)
        {
            foreach (string assembly in new[] { "dotnes.tasks.dll", "neslib.dll" })
            {
                Check(zip.Entries.Any(e => e.FullName.StartsWith("ref/", StringComparison.Ordinal) && e.Name == assembly), $"Missing normal ref asset {assembly}.");
                Check(zip.Entries.Any(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal) && e.Name == assembly), $"Missing normal lib asset {assembly}.");
            }
            var compiler = zip.Entries.First(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal) && e.Name == "dotnes.tasks.dll");
            using var stream = compiler.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            compilerHash = Hash(bytes.ToArray());
            bytes.Position = 0;
            compilerVersion = InformationalVersion(bytes);
            Console.WriteLine($"compiler: {compiler.FullName}; informational version {compilerVersion}; SHA256 {compilerHash}");
        }
        return new(version, hash, root);
    }

    static string InformationalVersion(Stream assembly)
    {
        using var pe = new PEReader(assembly);
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference)
                continue;
            var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
            if (parent.Kind != HandleKind.TypeReference ||
                reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name) != "AssemblyInformationalVersionAttribute")
                continue;
            var value = reader.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() == 1)
                return value.ReadSerializedString() ?? "(empty)";
        }
        throw new InvalidOperationException("Packaged compiler has no AssemblyInformationalVersionAttribute.");
    }

    static string TestProperties(bool explicitTest = false) => "<OutputType>Library</OutputType>" + (explicitTest ? "<IsTestProject>true</IsTestProject>" : "");

    static string PackageReferences(Package? package, bool roslyn = false) =>
        (package == null ? "" : $"""<PackageReference Include="dotnes" Version="{package.Version}" />""") +
        (roslyn ? $"""<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="{RoslynVersion}" />""" : "");

    static string TestPackages(Package? package) => $"""
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="{TestSdkVersion}" />
        <PackageReference Include="xunit" Version="{XunitVersion}" />
        <PackageReference Include="xunit.runner.visualstudio" Version="{XunitRunnerVersion}" PrivateAssets="all" />
        """ + PackageReferences(package, roslyn: package != null);

    static string CreateProject(string root, string name, string properties, string items, string source)
    {
        string directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        Write(directory, $"{name}.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup Label="PackageConsumer">
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                {properties}
              </PropertyGroup>
              <ItemGroup>{items}</ItemGroup>
            </Project>
            """);
        Write(directory, "Program.cs", source);
        return Path.Combine(directory, $"{name}.csproj");
    }

    string TestSource() => BridgeSource() + $$"""

        public sealed class PackedCompilerTests
        {
            [Xunit.Fact]
            public void NativeCompilerRunsFromNuGetRuntimeAssets()
            {
                Xunit.Assert.Equal("A9428D006060", Convert.ToHexString(PackageBridge.NativeBytes()));
                string path = typeof(dotnes.NesCompiler).Assembly.Location;
                Xunit.Assert.Equal(Path.GetFullPath(AppContext.BaseDirectory), Path.GetDirectoryName(path) + Path.DirectorySeparatorChar);
                Xunit.Assert.Equal("{{compilerHash}}", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
                Xunit.Assert.Equal({{Quote(compilerVersion)}},
                    System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                        typeof(dotnes.NesCompiler).Assembly)!.InformationalVersion);
                Xunit.Assert.True(File.Exists(typeof(NES.NESLib).Assembly.Location));
            }

            [Xunit.Fact]
            public void ManagedSourceCompilesAtRuntime()
            {
                using var assembly = PackageBridge.Compile("NES.NESLib.poke(0x6000, 0x42); while (true) ;");
                dotnes.ObjectModel.Program6502 program = dotnes.NesCompiler.Compile(assembly);
                Xunit.Assert.Contains("A9428D0060", Convert.ToHexString(program.GetMainBlock()));
                Xunit.Assert.NotEmpty(program.ToBytes());
            }

            [Xunit.Theory]
            [Xunit.InlineData(1)]
            [Xunit.InlineData(2)]
            [Xunit.InlineData(3)]
            public void NormalClrCodeIsNotAnalyzedAsNes(int value)
            {
                var objects = Enumerable.Range(1, value).Select(x => new Box(x)).ToArray();
                Xunit.Assert.Equal(value * (value + 1) / 2, objects.Sum(x => x.Value));
                Xunit.Assert.Equal("desktop:6", PackageBridge.Desktop());
                Xunit.Assert.Throws<InvalidOperationException>((Action)(() => throw new InvalidOperationException($"normal CLR {value}")));
            }

            sealed class Box(int value) { public int Value { get; } = value; }
        }
        """;

    static string BridgeSource() => $$"""
        public static class PackageBridge
        {
            public static string Desktop() => $"desktop:{new[] { 1, 2, 3 }.Where(x => x > 0).Sum()}";

            public static MemoryStream Compile(string source)
            {
                string framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
                var references = new[]
                {
                    Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(Path.Combine(framework, "System.Runtime.dll")),
                    Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(Path.Combine(framework, "netstandard.dll")),
                    Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(NES.NESLib).Assembly.Location),
                };
                var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                    "RomInput",
                    new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source) },
                    references,
                    new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                        Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
                        optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Release,
                        deterministic: true));
                var stream = new MemoryStream();
                var result = compilation.Emit(stream);
                if (!result.Success)
                    throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
                stream.Position = 0;
                return stream;
            }

            public static byte[] NativeBytes()
            {
                using var assembly = Compile("static extern void native_write(); native_write(); NES.NESLib.ppu_off(); while (true) ;");
                using var source = new StringReader({{Quote(NativeSource)}});
                using var native = new dotnes.AssemblyReader(source);
                dotnes.ObjectModel.Program6502 program = dotnes.NesCompiler.Compile(assembly, assemblyFiles: new[] { native });
                if (program.BaseAddress != 0x8000 || program.ToBytes().Length == 0)
                    throw new InvalidOperationException("Invalid compiler output.");
                ushort address = program.GetLabels()["_native_write"];
                byte[] call = { 0x20, (byte)address, (byte)(address >> 8) };
                if (!program.GetMainBlock().AsSpan(0, 3).SequenceEqual(call))
                    throw new InvalidOperationException("Native call relocation is incorrect.");
                if (!native.GetSegments().Single().Bytes.AsSpan().SequenceEqual(new byte[] { 0x12, 0x34 }))
                    throw new InvalidOperationException("AssemblyReader did not read CHARS.");
                return program.GetMainBlock("_native_write");
            }
        }
        """;

    async Task Restore(string project)
    {
        await RunProcess(ProjectDirectory(project), $"{Path.GetFileNameWithoutExtension(project)}-restore",
            "restore", project, "--configfile", Path.Combine(PackageRoot(project), "NuGet.Config"),
            "--no-http-cache", "--disable-build-servers", "-v:minimal", "-nr:false");
    }

    async Task<JsonElement> Evaluate(string project, string configuration, bool designTime = false, bool resolveReferences = false)
    {
        var args = new List<string> { "msbuild", project, "-nologo", "-nr:false", "-v:quiet", $"-p:Configuration={configuration}",
            $"-getProperty:{PropertyNames}", $"-getItem:{ItemNames}" };
        if (designTime)
            args.AddRange(["-t:Compile", "-p:DesignTimeBuild=true", "-p:BuildingProject=false",
                "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true", "-p:UseSharedCompilation=false"]);
        else if (resolveReferences)
            args.Add("-t:ResolveReferences");
        var result = await RunProcess(ProjectDirectory(project), $"{Path.GetFileNameWithoutExtension(project)}-{configuration}-{(designTime ? "design" : resolveReferences ? "references" : "evaluate")}", args.ToArray());
        int start = result.Output.IndexOf('{');
        Check(start >= 0, $"No MSBuild evaluation JSON: {result.Log}");
        using var document = JsonDocument.Parse(result.Output[start..]);
        var data = document.RootElement.Clone();
        if (designTime)
        {
            Check(Items(data, "CscCommandLineArgs").Count > 20, $"Design-time Compile did not execute Csc to produce real command-line arguments: {result.Log}");
            Check(!HasTranspileMessage(result.Output), $"Design-time compilation invoked transpilation: {result.Log}");
            NoRomArtifacts(project);
        }
        return data;
    }

    Task<JsonElement> DesignTime(string project, string configuration) => Evaluate(project, configuration, designTime: true);

    void CheckDesktop(string project, string configuration, JsonElement actual, JsonElement expected)
    {
        string label = $"{Path.GetFileNameWithoutExtension(project)}/{configuration}";
        foreach (string property in DesktopProperties)
            Check(Property(actual, property) == Property(expected, property),
                $"{label}: desktop {property} was changed by dotnes. Expected '{Property(expected, property)}', got '{Property(actual, property)}'.");
        Check(Property(actual, "IsTestProject") == "true", $"{label}: test classification was lost.");
        Check(Property(actual, "NESTargetPath") == "" && Property(actual, "_NESPropertiesStampFile") == "", $"{label}: ROM paths must not be set for tests.");
        Check(!Items(actual, "Analyzer").Any(IsNesAnalyzer), $"{label}: NES analyzer leaked into the desktop compiler.");
        foreach (string analyzer in Identities(expected, "Analyzer"))
            Check(Identities(actual, "Analyzer").Any(a => Path.GetFileName(a) == Path.GetFileName(analyzer)),
                $"{label}: package handling removed an unrelated analyzer: {Path.GetFileName(analyzer)}.");
        Check(!Items(actual, "Using").Any(i => i.GetProperty("Identity").GetString()!.StartsWith("NES", StringComparison.Ordinal)),
            $"{label}: NES global usings leaked into desktop code.");
        var commandLine = Identities(actual, "CscCommandLineArgs");
        var expectedCommandLine = Identities(expected, "CscCommandLineArgs");
        foreach (string prefix in CompilerSwitches)
            Check(commandLine.Where(a => a.StartsWith(prefix, StringComparison.Ordinal)).SequenceEqual(
                    expectedCommandLine.Where(a => a.StartsWith(prefix, StringComparison.Ordinal))),
                $"{label}: evaluated compiler switch {prefix} differs from a normal SDK project.");
        Check(commandLine.Any(a => a.Contains("System.Runtime.dll", StringComparison.OrdinalIgnoreCase)), $"{label}: desktop framework references are missing.");
        Check(!commandLine.Any(a => a.Contains("dotnes.analyzers", StringComparison.OrdinalIgnoreCase)), $"{label}: Csc received a NES analyzer.");
        foreach (string assembly in new[] { "dotnes.tasks.dll", "neslib.dll" })
        {
            Check(Identities(actual, "ReferencePath").Any(a => Path.GetFileName(a) == assembly), $"{label}: compile reference {assembly} missing.");
            if (Property(actual, "CopyLocalLockFileAssemblies") == "true")
                Check(Identities(actual, "ReferenceCopyLocalPaths").Any(a => Path.GetFileName(a) == assembly), $"{label}: runtime reference {assembly} missing.");
        }
    }

    async Task Build(string project, string configuration, bool rom = false)
    {
        if (rom)
        {
            JsonElement properties = await Evaluate(project, configuration);
            Check(Property(properties, "DebugType").Equals("none", StringComparison.OrdinalIgnoreCase),
                $"{project}/{configuration}: ROM DebugType must remain None; SDK defaults such as portable can change emitted IL.");
        }
        var result = await RunProcess(ProjectDirectory(project), $"{Path.GetFileNameWithoutExtension(project)}-{configuration}-build",
            "build", project, "--no-restore", "--disable-build-servers", "-c", configuration, "-v:minimal", "-nr:false", "-p:UseSharedCompilation=false");
        if (!rom)
            Check(!HasTranspileMessage(result.Output), $"Desktop build invoked transpilation: {result.Log}");
        else
            Check(File.Exists(RomPath(project, configuration)), $"ROM build did not produce {RomPath(project, configuration)}. {result.Log}");
    }

    async Task Test(string project, string configuration, int expectedTests)
    {
        string results = Path.Combine(ProjectDirectory(project), "results", configuration);
        // Intentionally rebuild: a no-build test can hide broken package build targets.
        var result = await RunProcess(ProjectDirectory(project), $"{Path.GetFileNameWithoutExtension(project)}-{configuration}-test",
            "test", project, "--no-restore", "--disable-build-servers", "-c", configuration, "-v:minimal",
            "-nr:false", "-p:UseSharedCompilation=false", "--logger", "trx;LogFileName=tests.trx", "--results-directory", results);
        Check(!HasTranspileMessage(result.Output), $"dotnet test invoked transpilation: {result.Log}");
        string trx = Path.Combine(results, "tests.trx");
        Check(File.Exists(trx), $"Test runner did not create TRX: {result.Log}");
        var document = XDocument.Load(trx);
        var counters = document.Descendants().Single(e => e.Name.LocalName == "Counters");
        foreach (string attribute in new[] { "total", "executed", "passed" })
            Check((int?)counters.Attribute(attribute) == expectedTests, $"{project}: expected {expectedTests} {attribute} tests, got {counters}. {result.Log}");
        Check((int?)counters.Attribute("failed") == 0, $"Test failures in {trx}");
        var resultsElements = document.Descendants().Where(e => e.Name.LocalName == "UnitTestResult").ToArray();
        Check(resultsElements.Length == expectedTests && resultsElements.All(e => (string?)e.Attribute("outcome") == "Passed"),
            $"Test discovery or per-test results differ from the expected {expectedTests} passed cases: {trx}");
    }

    async Task CheckImportOrder(string automatic, string explicitTest, string host)
    {
        string props = Path.Combine(ProjectDirectory(automatic), "obj", $"{Path.GetFileName(automatic)}.nuget.g.props");
        var imports = XDocument.Load(props).Descendants().Where(e => e.Name.LocalName == "Import")
            .Select(e => e.Attribute("Project")!.Value.Replace('\\', '/')).ToArray();
        int dotnes = Array.FindIndex(imports, p => p.EndsWith("/dotnes.props", StringComparison.OrdinalIgnoreCase));
        int testSdk = Array.FindIndex(imports, p => p.EndsWith("/Microsoft.NET.Test.Sdk.props", StringComparison.OrdinalIgnoreCase));
        Check(dotnes >= 0 && testSdk >= 0, $"NuGet did not import both dotnes and Test SDK props; inspect {props}");
        Console.WriteLine($"Import order: dotnes.props #{dotnes + 1}, Test SDK props #{testSdk + 1}. " +
            (testSdk > dotnes ? "Auto detection exercises a later Test SDK flag." : "This NuGet version imports Test SDK first; explicit body marking is tested alongside the SDK."));
        string testSdkProps = Directory.EnumerateFiles(Path.Combine(candidateRoot, "packages", "microsoft.net.test.sdk"), "Microsoft.NET.Test.Sdk.props", SearchOption.AllDirectories).First();
        Check(XDocument.Load(testSdkProps).Descendants().Any(e => e.Name.LocalName == "IsTestProject" && e.Value == "true"),
            "Test SDK no longer marks IsTestProject; update the detection regression.");
        foreach (string project in new[] { explicitTest, host })
        {
            string preprocessed = Path.Combine(ProjectDirectory(project), "imports.xml");
            await RunProcess(ProjectDirectory(project), $"{Path.GetFileNameWithoutExtension(project)}-imports",
                "msbuild", project, "-nologo", "-nr:false", $"-preprocess:{preprocessed}");
            string text = File.ReadAllText(preprocessed);
            int earlyProps = text.IndexOf("dotnes.props", StringComparison.OrdinalIgnoreCase);
            int body = text.IndexOf("<PropertyGroup Label=\"PackageConsumer\">", StringComparison.Ordinal);
            int flag = text.IndexOf("<IsTestProject>true</IsTestProject>", Math.Max(body, 0), StringComparison.Ordinal);
            if (project == host)
            {
                int earlyFlag = text.IndexOf("<IsTestProject>true</IsTestProject>", StringComparison.Ordinal);
                Check(earlyFlag >= 0 && earlyFlag < earlyProps, $"The no-Test-SDK host must mark IsTestProject in normal Directory.Build.props before NuGet imports: {preprocessed}");
                Check(!text.Contains("Microsoft.NET.Test.Sdk.props", StringComparison.OrdinalIgnoreCase), "Explicit no-SDK host unexpectedly imported Test SDK props.");
            }
            else
                Check(earlyProps >= 0 && body > earlyProps && flag > body, $"Project-body IsTestProject must follow dotnes.props: {preprocessed}");
        }
    }

    void CheckDesktopOutputs(string project, string configuration, JsonElement evaluation)
    {
        string output = Path.Combine(ProjectDirectory(project), "bin", configuration, "net10.0");
        string name = Path.GetFileNameWithoutExtension(project);
        foreach (string file in new[] { $"{name}.dll", $"{name}.deps.json" })
            Check(File.Exists(Path.Combine(output, file)), $"{project}/{configuration}: missing desktop output {file}.");
        bool copyLocal = Property(evaluation, "CopyLocalLockFileAssemblies") == "true";
        foreach (string file in new[] { "dotnes.tasks.dll", "neslib.dll", "Microsoft.CodeAnalysis.CSharp.dll", "Microsoft.CodeAnalysis.dll" })
            Check(File.Exists(Path.Combine(output, file)) == copyLocal, $"{project}/{configuration}: SDK CopyLocalLockFileAssemblies={copyLocal} was not respected for {file}.");
        Check(File.Exists(Path.Combine(output, $"{name}.runtimeconfig.json")) == (Property(evaluation, "GenerateRuntimeConfigurationFiles") == "true"),
            $"{project}/{configuration}: runtimeconfig generation differs from SDK settings.");
        Check(File.Exists(Path.Combine(ProjectDirectory(project), "obj", configuration, "net10.0", "ref", $"{name}.dll")),
            $"{project}/{configuration}: SDK reference assembly missing.");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, $"{name}.deps.json")));
        var target = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value;
        var package = target.EnumerateObject().Single(p => p.Name.StartsWith("dotnes/", StringComparison.OrdinalIgnoreCase)).Value;
        Check(package.TryGetProperty("runtime", out var runtime) && runtime.EnumerateObject().Any(a => a.Name.EndsWith("/dotnes.tasks.dll", StringComparison.Ordinal)),
            $"{project}: deps.json does not include compiler runtime assets.");
        if (copyLocal)
            Check(Hash(File.ReadAllBytes(Path.Combine(output, "dotnes.tasks.dll"))) == compilerHash, $"{project}: runtime compiler is not the candidate compiler.");
    }

    void CheckResolvedAssets(string project, Package package)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectDirectory(project), "obj", "project.assets.json")));
        var root = document.RootElement;
        string cache = Path.Combine(package.Root, "packages");
        var folders = root.GetProperty("packageFolders").EnumerateObject().Select(p => Path.GetFullPath(p.Name).TrimEnd(Path.DirectorySeparatorChar)).ToArray();
        Check(folders.Length == 1 && string.Equals(folders[0], cache, StringComparison.OrdinalIgnoreCase), $"{project}: restore used a non-isolated package cache.");
        var library = root.GetProperty("libraries").EnumerateObject().Single(p => p.Name.StartsWith("dotnes/", StringComparison.OrdinalIgnoreCase));
        string installed = Path.Combine(cache, library.Value.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
        Check(Hash(File.ReadAllBytes(Directory.EnumerateFiles(installed, "*.nupkg").Single())) == package.Hash,
            $"{project}: restored package is not the supplied nupkg.");
        foreach (var target in root.GetProperty("targets").EnumerateObject())
        {
            var entry = target.Value.GetProperty(library.Name);
            foreach (string kind in new[] { "compile", "runtime" })
            {
                Check(entry.TryGetProperty(kind, out var assets), $"{project}: no {kind} package assets.");
                foreach (string assembly in new[] { "dotnes.tasks.dll", "neslib.dll" })
                    Check(assets.EnumerateObject().Any(a => a.Name.StartsWith(kind == "compile" ? "ref/" : "lib/", StringComparison.Ordinal) && a.Name.EndsWith("/" + assembly, StringComparison.Ordinal)),
                        $"{project}: {assembly} is not a normal NuGet {kind} asset.");
            }
        }
    }

    async Task<string> BuildHello(string root, Package package)
    {
        string project = CreateProject(root, "HelloRom", "<OutputType>Exe</OutputType>", PackageReferences(package), "");
        foreach (string file in new[] { "Program.cs", "chr_generic.s" })
            File.Copy(Path.Combine(options.Repository, "samples", "hello", file), Path.Combine(ProjectDirectory(project), file), overwrite: true);
        await Restore(project);
        foreach (string configuration in new[] { "Debug", "Release" })
            await Build(project, configuration, rom: true);
        Check(File.ReadAllBytes(RomPath(project, "Debug")).AsSpan().SequenceEqual(File.ReadAllBytes(RomPath(project, "Release"))),
            $"{package.Version}: hello Debug/Release ROMs differ.");
        return RomPath(project, "Release");
    }

    async Task CheckRomPropertyOverrides(Package? baseline)
    {
        const string properties = """
            <OutputType>Exe</OutputType>
            <Optimize>false</Optimize>
            <DebugSymbols>true</DebugSymbols>
            <DebugType>embedded</DebugType>
            <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
            <NoStdLib>false</NoStdLib>
            <ComputeNETCoreBuildOutputFiles>true</ComputeNETCoreBuildOutputFiles>
            <GenerateDependencyFile>true</GenerateDependencyFile>
            <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
            <ProduceReferenceAssembly>true</ProduceReferenceAssembly>
            <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
            <NoWarn>CS0168</NoWarn>
            """;
        // Evaluation only: user overrides must retain normal SDK/project/hook
        // precedence, even when unoptimized IL would not be supported by dotnes.
        foreach (bool useHook in new[] { false, true })
        {
            string name = useHook ? "RomHookOverrides" : "RomOverrides";
            string body = useHook ? "" : properties;
            string project = CreateProject(candidateRoot, name, body, PackageReferences(candidate), "while (true) ;");
            string control = CreateProject(candidateRoot, name + "Control", body, "", "while (true) ;");
            string? baselineProject = baseline == null ? null :
                CreateProject(baseline.Root, name, body, PackageReferences(baseline), "while (true) ;");
            foreach (string fixture in new[] { control, project, baselineProject }.OfType<string>())
            {
                if (useHook)
                {
                    Write(ProjectDirectory(fixture), "Directory.Build.props", """
                        <Project>
                          <PropertyGroup>
                            <CustomAfterMicrosoftCommonProps>$(MSBuildThisFileDirectory)ExistingOverrides.props</CustomAfterMicrosoftCommonProps>
                          </PropertyGroup>
                        </Project>
                        """);
                    Write(ProjectDirectory(fixture), "ExistingOverrides.props", $"<Project><PropertyGroup>{properties}</PropertyGroup></Project>");
                }
                await Restore(fixture);
            }
            foreach (string configuration in new[] { "Debug", "Release" })
            {
                JsonElement expected = await Evaluate(control, configuration, resolveReferences: true);
                JsonElement actual = await Evaluate(project, configuration, resolveReferences: true);
                JsonElement? original = baselineProject == null ? null : await Evaluate(baselineProject, configuration, resolveReferences: true);
                foreach (string property in DesktopProperties)
                {
                    if (original is { } originalProperties)
                        Check(Property(originalProperties, property) == Property(expected, property),
                            $"Original package differs from SDK override semantics for {name}/{property}/{configuration}; inspect evaluation logs.");
                    Check(Property(actual, property) == Property(expected, property),
                        $"{name} override {property}/{configuration} was overwritten: expected '{Property(expected, property)}', got '{Property(actual, property)}'.");
                }
                Check(Property(actual, "IsTestProject") != "true", "Explicit ROM compiler settings must not classify the project as a test.");
                Check(Items(actual, "Analyzer").Any(IsNesAnalyzer), "Explicit ROM compiler settings must not disable the NES analyzer.");
            }
            NoRomArtifacts(project);
        }
        Console.WriteLine("PASS: ROM project-body and existing-hook compiler/output overrides retain ordinary SDK precedence" +
            (baseline == null ? "." : " and match the original package."));
    }

    async Task CheckLateClassificationErrors()
    {
        string lateTrue = CreateProject(candidateRoot, "LateTestMark", TestProperties(explicitTest: true),
            PackageReferences(candidate), "public sealed class OrdinaryClass { }");
        string lateFalse = CreateProject(candidateRoot, "LateTestUnmark",
            "<OutputType>Library</OutputType><IsTestProject>false</IsTestProject>",
            TestPackages(candidate), "public sealed class OrdinaryClass { }");
        foreach (string project in new[] { lateTrue, lateFalse })
        {
            await Restore(project);
            foreach (string scenario in new[] { "build", "design", "transpile", "references" })
            {
                string[] args = scenario switch
                {
                    "build" => ["build", project, "--no-restore", "--disable-build-servers", "-v:minimal", "-nr:false", "-p:UseSharedCompilation=false"],
                    "design" => ["msbuild", project, "-nologo", "-nr:false", "-v:minimal", "-t:Compile",
                        "-p:DesignTimeBuild=true", "-p:BuildingProject=false", "-p:SkipCompilerExecution=true", "-p:UseSharedCompilation=false"],
                    _ => ["msbuild", project, "-nologo", "-nr:false", "-v:minimal",
                        scenario == "transpile" ? "-t:Transpile" : "-t:ResolveReferences"],
                };
                var result = await RunProcess(ProjectDirectory(project),
                    $"{Path.GetFileNameWithoutExtension(project)}-{scenario}", args, expectedSuccess: false);
                Check(result.ExitCode != 0 &&
                    result.Output.Contains("IsTestProject changed after package props were evaluated", StringComparison.Ordinal),
                    $"Late IsTestProject changes must fail explicitly during {scenario}, in both directions. {result.Log}");
                Check(!HasTranspileMessage(result.Output), $"Late IsTestProject validation reached transpilation. {result.Log}");
                Check(!File.Exists(Path.Combine(ProjectDirectory(project), "bin", "Debug", "net10.0", Path.GetFileNameWithoutExtension(project) + ".dll")),
                    $"Late IsTestProject validation occurred after compilation. {result.Log}");
                NoRomArtifacts(project);
            }
        }
        Console.WriteLine("PASS: late test classification changes in both directions fail during build, design-time compilation, and direct Transpile/ResolveReferences.");
    }

    async Task CheckAnalyzerExclusion(Package? baseline)
    {
        async Task<(int Analyzers, int Arguments)> Inspect(Package package)
        {
            string project = CreateProject(package.Root, "ExcludedAnalyzerRom", "<OutputType>Exe</OutputType>",
                $"""<PackageReference Include="dotnes" Version="{package.Version}" ExcludeAssets="analyzers" />""", "while (true) ;");
            await Restore(project);
            JsonElement evaluated = await DesignTime(project, "Debug");
            Check(Property(evaluated, "IsTestProject") != "true" && Property(evaluated, "Optimize") == "true",
                "Excluding an analyzer must not classify a ROM as a desktop test.");
            using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectDirectory(project), "obj", "project.assets.json")));
            var dependency = assets.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject().Single()
                .Value.GetProperty("dependencies").GetProperty("dotnes");
            Check(dependency.TryGetProperty("include", out var include) &&
                !include.GetString()!.Split(',').Any(flag => flag.Trim().Equals("Analyzers", StringComparison.OrdinalIgnoreCase)),
                "NuGet did not record the requested analyzer asset exclusion.");
            return (Items(evaluated, "Analyzer").Count(IsNesAnalyzer),
                Identities(evaluated, "CscCommandLineArgs").Count(a => a.Contains("dotnes.analyzers", StringComparison.OrdinalIgnoreCase)));
        }

        var actual = await Inspect(candidate);
        if (baseline != null)
        {
            var original = await Inspect(baseline);
            Check(actual == original, $"ExcludeAssets=analyzers changed resolved analyzer/compiler-argument counts: candidate {actual}, original {original}.");
        }
        // SDK 10.0.401 retains the original package's analyzer despite recording
        // its exclusion. Test compatibility, not a stronger SDK behavior.
        Console.WriteLine($"PASS: NuGet analyzer-exclusion metadata retained; SDK resolves {actual.Analyzers} NES analyzer(s)" +
            (baseline == null ? " (supply a baseline package to compare SDK behavior)." : ", matching the original package."));
    }

    void NoRomArtifacts(string project)
    {
        string[] forbidden = Directory.EnumerateFiles(ProjectDirectory(project), "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) || f.EndsWith("dotnes.properties.stamp", StringComparison.OrdinalIgnoreCase)).ToArray();
        Check(forbidden.Length == 0, $"{project}: unexpected ROM/stamp artifacts: {string.Join(", ", forbidden)}");
    }

    async Task<ProcessResult> RunProcess(string workingDirectory, string label, params string[] args) =>
        await RunProcess(workingDirectory, label, args, expectedSuccess: true);

    async Task<ProcessResult> RunProcess(string workingDirectory, string label, string[] args, bool expectedSuccess)
    {
        string logs = Path.Combine(options.Artifacts, "logs");
        Directory.CreateDirectory(logs);
        string log = Path.Combine(logs, $"{++processNumber:D3}-{label}.log");
        string root = workingDirectory.StartsWith(Path.Combine(options.Artifacts, "baseline") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(options.Artifacts, "baseline") : Path.Combine(options.Artifacts, "candidate");
        var start = new ProcessStartInfo(dotnetHost)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
            start.ArgumentList.Add(arg);
        start.Environment["NUGET_PACKAGES"] = Path.Combine(root, "packages");
        start.Environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, "http-cache");
        start.Environment["NUGET_PLUGINS_CACHE_PATH"] = Path.Combine(root, "plugin-cache");
        start.Environment["DOTNET_CLI_HOME"] = Path.Combine(options.Artifacts, "cli-home");
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using var process = new Process { StartInfo = start };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        await using var writer = new StreamWriter(log, append: false, Encoding.UTF8);
        await writer.WriteLineAsync(Quote(dotnetHost) + " " + string.Join(" ", args.Select(Quote)));
        var output = new StringBuilder();
        var gate = new SemaphoreSlim(1);
        bool truncated = false;
        async Task Drain(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                await gate.WaitAsync();
                try
                {
                    await writer.WriteLineAsync(line);
                    if (output.Length < 8 * 1024 * 1024)
                        output.AppendLine(line);
                    else
                        truncated = true;
                }
                finally { gate.Release(); }
            }
        }
        process.Start();
        Task stdout = Drain(process.StandardOutput), stderr = Drain(process.StandardError);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await Task.WhenAll(stdout, stderr);
            throw new TimeoutException($"{label} exceeded {options.TimeoutSeconds}s. Log: {log}");
        }
        await Task.WhenAll(stdout, stderr);
        await writer.FlushAsync();
        Check(!truncated, $"{label} exceeded the 8 MiB captured-output limit. Full log: {log}");
        string text = output.ToString();
        if (expectedSuccess && process.ExitCode != 0)
        {
            string excerpt = string.Join(Environment.NewLine, text.Split('\n').TakeLast(15));
            throw new InvalidOperationException($"{label} exited {process.ExitCode}. Log: {log}\n{excerpt[^Math.Min(excerpt.Length, 12000)..]}");
        }
        return new(process.ExitCode, text, log);
    }

    void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        checks++;
    }

    static string Property(JsonElement data, string name) => data.GetProperty("Properties").GetProperty(name).GetString() ?? "";
    static List<JsonElement> Items(JsonElement data, string name) => data.GetProperty("Items").GetProperty(name).EnumerateArray().ToList();
    static string[] Identities(JsonElement data, string name) => Items(data, name).Select(i => i.GetProperty("Identity").GetString()!).ToArray();
    static bool IsNesAnalyzer(JsonElement item) => item.GetProperty("Identity").GetString()!.Contains("dotnes.analyzers", StringComparison.OrdinalIgnoreCase);
    static bool HasTranspileMessage(string output) => output.Contains("Transpiling", StringComparison.OrdinalIgnoreCase) || output.Contains("TranspileToNES", StringComparison.OrdinalIgnoreCase);
    static string ProjectDirectory(string project) => Path.GetDirectoryName(project)!;
    static string PackageRoot(string project) => Path.GetDirectoryName(ProjectDirectory(project))!;
    static string RomPath(string project, string configuration) => Path.Combine(ProjectDirectory(project), "bin", configuration, "net10.0", Path.GetFileNameWithoutExtension(project) + ".nes");
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static string Quote(string value) => "\"" + JsonEncodedText.Encode(value) + "\"";
    static void Write(string directory, string name, string contents) => File.WriteAllText(Path.Combine(directory, name), contents, new UTF8Encoding(false));
    sealed record Package(string Version, string Hash, string Root);
    sealed record ProcessResult(int ExitCode, string Output, string Log);
}
