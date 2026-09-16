using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;

namespace dotnes.tests;

public class MesenMSBuildTests
{
    const string Version = "2.2.1";
    static readonly string Repository = FindRepository();
    static readonly string TargetsPath = Path.Combine(Repository, "src", "dotnes.mesen", "build", "dotnes.mesen.targets");

    public static TheoryData<string, string, string, string, string> ReleaseAssets => new()
    {
        { "windows", "", "Windows", "Mesen.exe", "c19e10b3b3865eb0e86e4d01bf09a5027a4452081c04b5bd5b6cbbf76e346fd6" },
        { "linux", "X64", "Linux_x64", "Mesen", "c88ff4d251b407515c43d3332d641927655cd69fb538996b6a21da4509dbb58f" },
        { "linux", "Arm64", "Linux_ARM64", "Mesen", "5030702ba13d043bf926ee3cc6c6a5d3567e08345e9c50b2df4d4d08b90ead30" },
        { "osx", "X64", "macOS_x64_Intel", "Mesen.app/Contents/MacOS/Mesen", "804cca46e70b9015898973b5a0a86e679d13f93a89e10813f74e3b7364c9c2d7" },
        { "osx", "Arm64", "macOS_ARM64_AppleSilicon", "Mesen.app/Contents/MacOS/Mesen", "9c230cc25ad0bae79fed810c9eeb21c1c507b96b1ce795bbd22ca4331ffb6e48" },
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluatesDownloadAndRunMetadata(bool testRunner)
    {
        using var project = new TestProject();
        var properties = await project.Evaluate(
            "MesenVersion,_MesenBaseUrl,_MesenLicenseUrl,_MesenDir,_MesenExe,_MesenZipName,_MesenZipSha256,RunCommand,RunArguments,RunWorkingDirectory,_MesenConfigBaseDir",
            testRunner ? ["-p:MesenTestRunner=true", "-p:MesenTimeout=10", "-p:MesenLuaScript=smoke-test.lua"] : []);
        Assert.Equal(Version, properties["MesenVersion"]);
        Assert.Equal($"https://github.com/nesdev-org/MesenCE/releases/download/{Version}", properties["_MesenBaseUrl"]);
        Assert.Equal($"https://raw.githubusercontent.com/nesdev-org/MesenCE/{Version}/LICENSE", properties["_MesenLicenseUrl"]);
        string os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var asset = Assert.Single(ReleaseAssets, a => (string)a[0] == os &&
            ((string)a[1] == "" || (string)a[1] == RuntimeInformation.OSArchitecture.ToString()));
        Assert.Equal($"Mesen_{Version}_{asset[2]}.zip", properties["_MesenZipName"]);
        Assert.Equal(asset[4], properties["_MesenZipSha256"]);
        Assert.Equal(Path.GetFullPath(Path.Combine(properties["_MesenDir"], (string)asset[3])),
            Path.GetFullPath(properties["RunCommand"]));
        Assert.Equal(properties["_MesenExe"], properties["RunCommand"]);
        Assert.Equal("out/", properties["RunWorkingDirectory"]);
        Assert.Equal(testRunner
            ? ["game.nes", "--testrunner", "--doNotSaveSettings", "--timeout=10", "smoke-test.lua"]
            : ["game.nes"], properties["RunArguments"].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var folder = OperatingSystem.IsWindows() ? Environment.SpecialFolder.MyDocuments : Environment.SpecialFolder.ApplicationData;
        Assert.Equal(Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify), properties["_MesenConfigBaseDir"]);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public async Task ConfigPreservesExistingSettings(bool legacy, bool current, bool packagingProject)
    {
        using var project = new TestProject();
        string configBase = Path.Combine(project.Directory, "config");
        string legacySettings = Path.Combine(configBase, "Mesen2", "settings.json");
        string currentSettings = Path.Combine(configBase, "MesenCE", "settings.json");
        const string existing = """{"Preferences":{"Theme":"Dark"}}""";
        foreach (string path in new[] { legacy ? legacySettings : null, current ? currentSettings : null }.OfType<string>())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, existing);
        }

        var properties = await project.Evaluate("_MesenSettingsJson",
            ["-t:_MesenEnsureConfigured", $"-p:_MesenConfigBaseDir={configBase}",
             $"-p:PackageId={(packagingProject ? "dotnes.mesen" : "consumer")}"]);
        string expected = legacy && !current ? legacySettings : currentSettings;
        Assert.Equal(expected, properties["_MesenSettingsJson"]);
        Assert.Equal(legacy, File.Exists(legacySettings));
        Assert.Equal(current || (!legacy && !packagingProject), File.Exists(currentSettings));
        if (legacy)
            Assert.Equal(existing, File.ReadAllText(legacySettings));
        if (current)
            Assert.Equal(existing, File.ReadAllText(currentSettings));
        if (!legacy && !current && !packagingProject)
            Assert.Equal("{}", File.ReadAllText(currentSettings).Trim());
    }

    sealed class TestProject : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), $"dotnes-mesen-{Guid.NewGuid():N}");
        readonly string projectPath;

        public TestProject()
        {
            System.IO.Directory.CreateDirectory(Directory);
            projectPath = Path.Combine(Directory, "consumer.proj");
            new XDocument(new XElement("Project",
                new XElement("PropertyGroup",
                    new XElement("TargetName", "game"),
                    new XElement("OutputPath", "out/")),
                new XElement("Import", new XAttribute("Project", TargetsPath)))).Save(projectPath);
        }

        public async Task<Dictionary<string, string>> Evaluate(string properties, string[]? arguments = null)
        {
            var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                WorkingDirectory = Directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in new[] { "msbuild", projectPath, "-nologo", "-nr:false", $"-getProperty:{properties},MSBuildProjectName" }.Concat(arguments ?? []))
                startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("Mesen MSBuild evaluation timed out.");
            }
            string output = await stdout;
            string error = await stderr;
            Assert.True(process.ExitCode == 0, $"dotnet msbuild exited {process.ExitCode}:{Environment.NewLine}{output}{error}");
            using var json = JsonDocument.Parse(output);
            return json.RootElement.GetProperty("Properties").EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString()!);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "dotnes.mesen", "dotnes.mesen.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find the dotnes repository.");
    }
}
