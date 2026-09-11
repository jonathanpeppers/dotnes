using System.Diagnostics;
using System.Security.Cryptography;
using System.Xml.Linq;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ByteHelperMSBuildTests(ITestOutputHelper output) : RoslynTests(output)
{
    [Fact]
    public async Task PropertyChangesRebuildAndRestoreBaselineRom()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dotnes-byte-helpers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string targetPath = Path.Combine(directory, "helpers.dll");
            using (var assembly = CompileAssembly("""
                NES.NESLib.poke(0x2001, 0);
                State.Result = helper(42);
                while (true) ;
                static byte helper(byte value) => (byte)(value ^ 3);
                static class State { public static byte Result; }
                """))
            using (var destination = File.Create(targetPath))
                assembly.CopyTo(destination);

            string chrPath = Path.Combine(directory, "chr_generic.s");
            using (var source = Utilities.GetResource("chr_generic.s"))
            using (var destination = File.Create(chrPath))
                source.CopyTo(destination);

            string outputDirectory = Path.Combine(directory, "out");
            string intermediateDirectory = Path.Combine(directory, "obj");
            Directory.CreateDirectory(outputDirectory);
            Directory.CreateDirectory(intermediateDirectory);
            string projectPath = Path.Combine(directory, "helpers.proj");
            string taskDirectory = Path.GetDirectoryName(typeof(TranspileToNES).Assembly.Location)!;
            var project = new XDocument(
                new XElement("Project",
                    new XElement("PropertyGroup",
                        new XElement("TargetPath", targetPath),
                        new XElement("OutputPath", outputDirectory + Path.DirectorySeparatorChar),
                        new XElement("TargetName", "helpers"),
                        new XElement("IntermediateOutputPath", intermediateDirectory + Path.DirectorySeparatorChar),
                        new XElement("NESMapper", "0"),
                        new XElement("NESPrgBanks", "2"),
                        new XElement("NESChrBanks", "1"),
                        new XElement("NESDiagnosticLogging", "true")),
                    new XElement("ItemGroup",
                        new XElement("NESAssembly", new XAttribute("Include", chrPath))),
                    new XElement("Import",
                        new XAttribute("Project", Path.Combine(taskDirectory, "dotnes.targets")))));
            project.Save(projectPath);

            string romPath = Path.Combine(outputDirectory, "helpers.nes");
            string stampPath = Path.Combine(intermediateDirectory, "dotnes.properties.stamp");
            DateTime assemblyTime = File.GetLastWriteTimeUtc(targetPath);
            DateTime projectTime = File.GetLastWriteTimeUtc(projectPath);
            const string transpilationMessage = "Single-pass transpilation...";

            Assert.Contains(transpilationMessage, await Build(null));
            byte[] defaultRom = File.ReadAllBytes(romPath);
            Assert.Contains("NESOptimizeByteHelpers=", File.ReadAllLines(stampPath));

            Assert.Contains(transpilationMessage, await Build(false));
            byte[] baseline = File.ReadAllBytes(romPath);
            Assert.Equal(defaultRom, baseline);
            string baselineHash = Convert.ToHexString(SHA256.HashData(baseline));
            Assert.Contains("NESOptimizeByteHelpers=false", File.ReadAllText(stampPath));

            Assert.Contains(transpilationMessage, await Build(true));
            Assert.NotEqual(baselineHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(romPath))));
            Assert.Contains("NESOptimizeByteHelpers=true", File.ReadAllText(stampPath));

            Assert.Contains(transpilationMessage, await Build(false));
            Assert.Equal(baselineHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(romPath))));
            Assert.Equal(baseline, File.ReadAllBytes(romPath));
            Assert.Contains("NESOptimizeByteHelpers=false", File.ReadAllText(stampPath));

            DateTime romTime = File.GetLastWriteTimeUtc(romPath);
            DateTime stampTime = File.GetLastWriteTimeUtc(stampPath);
            Assert.DoesNotContain(transpilationMessage, await Build(false));
            Assert.Equal(romTime, File.GetLastWriteTimeUtc(romPath));
            Assert.Equal(stampTime, File.GetLastWriteTimeUtc(stampPath));
            Assert.Equal(assemblyTime, File.GetLastWriteTimeUtc(targetPath));
            Assert.Equal(projectTime, File.GetLastWriteTimeUtc(projectPath));

            async Task<string> Build(bool? optimize)
            {
                var startInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = directory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add("msbuild");
                startInfo.ArgumentList.Add(projectPath);
                startInfo.ArgumentList.Add("-t:Transpile");
                startInfo.ArgumentList.Add("-nologo");
                startInfo.ArgumentList.Add("-v:normal");
                startInfo.ArgumentList.Add("-nr:false");
                if (optimize.HasValue)
                    startInfo.ArgumentList.Add($"-p:NESOptimizeByteHelpers={optimize.Value.ToString().ToLowerInvariant()}");
                using var process = Process.Start(startInfo);
                Assert.NotNull(process);
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    throw new TimeoutException("dotnet msbuild timed out.");
                }
                string output = string.Join(Environment.NewLine, await Task.WhenAll(standardOutput, standardError));
                Assert.True(process.ExitCode == 0, $"dotnet msbuild failed with exit code {process.ExitCode}:{Environment.NewLine}{output}");
                return output;
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
