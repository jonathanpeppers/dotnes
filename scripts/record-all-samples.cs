// Usage: dotnet run scripts/record-all-samples.cs
// Builds all samples and records PNG/GIF for each into samples/{name}/{name}.png or .gif
// Requires: .NET 10+, Windows, ANESE emulator built
#:package System.Drawing.Common@9.*
#:package AnimatedGif@1.*
#:property NoWarn=CA1416

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AnimatedGif;

string repoRoot = Path.GetFullPath(".");
string anesePath = Path.Combine(repoRoot, "src", "dotnes.anese", "obj", "Debug", "win", "anese.exe");
if (!File.Exists(anesePath))
{
    Console.Error.WriteLine($"ERROR: anese.exe not found at {anesePath}");
    Console.Error.WriteLine("Build the emulator first: dotnet build src/dotnes.anese");
    return;
}

// Static samples get a PNG, animated samples get a GIF
HashSet<string> staticSamples = [
    "hello", "hellofs", "attributetable", "tileset1",
    "rletitle", "tint", "staticsprite", "nestedloop"
];

string samplesDir = Path.Combine(repoRoot, "samples");
var sampleDirs = Directory.GetDirectories(samplesDir)
    .Where(d => Directory.GetFiles(d, "*.csproj").Length > 0 || Directory.GetFiles(d, "*.fsproj").Length > 0)
    .OrderBy(d => Path.GetFileName(d))
    .ToList();

Console.WriteLine($"Found {sampleDirs.Count} samples to process");
Console.WriteLine();

int succeeded = 0, failed = 0, skipped = 0;

foreach (var sampleDir in sampleDirs)
{
    string name = Path.GetFileName(sampleDir);
    bool isGif = !staticSamples.Contains(name);
    string ext = isGif ? "gif" : "png";
    string outputPath = Path.Combine(sampleDir, $"{name}.{ext}");

    Console.WriteLine($"=== {name} ({ext}) ===");

    // Build the sample
    Console.Write($"  Building... ");
    var buildResult = RunProcess("dotnet", $"build \"{sampleDir}\" -c Debug --nologo -v q", timeoutMs: 60000);
    if (buildResult.ExitCode != 0)
    {
        Console.WriteLine("FAILED");
        Console.Error.WriteLine($"  Build error: {buildResult.StdErr.Trim()}");
        failed++;
        continue;
    }
    Console.WriteLine("OK");

    // Find the .nes file
    string? nesFile = Directory.GetFiles(sampleDir, "*.nes", SearchOption.AllDirectories)
        .Where(f => !f.Contains("obj"))
        .FirstOrDefault();

    // Also check bin output
    nesFile ??= Directory.GetFiles(sampleDir, "*.nes", SearchOption.AllDirectories).FirstOrDefault();

    if (nesFile == null)
    {
        Console.WriteLine($"  No .nes file found after build — skipping");
        skipped++;
        continue;
    }

    Console.Write($"  Recording {ext}... ");

    // Launch emulator
    var proc = Process.Start(new ProcessStartInfo(anesePath, $"\"{nesFile}\" --no-sav") { UseShellExecute = false });
    if (proc == null)
    {
        Console.WriteLine("FAILED (couldn't start emulator)");
        failed++;
        continue;
    }

    Thread.Sleep(3000); // Wait for emulator to boot

    IntPtr hwnd = FindWindowByPid(proc.Id);
    if (hwnd == IntPtr.Zero)
    {
        Console.WriteLine("FAILED (couldn't find window)");
        proc.Kill();
        failed++;
        continue;
    }

    try
    {
        if (isGif)
        {
            int frames = 30;
            int interval = 100;
            using var gif = AnimatedGif.AnimatedGif.Create(outputPath, interval);
            int captured = 0;
            for (int i = 0; i < frames; i++)
            {
                var bmp = CaptureWindow(hwnd);
                if (bmp != null)
                {
                    gif.AddFrame(bmp, delay: interval, quality: GifQuality.Bit8);
                    bmp.Dispose();
                    captured++;
                }
                if (i < frames - 1) Thread.Sleep(interval);
            }
            Console.WriteLine($"OK ({captured} frames)");
        }
        else
        {
            var bmp = CaptureWindow(hwnd);
            if (bmp != null)
            {
                bmp.Save(outputPath, ImageFormat.Png);
                bmp.Dispose();
                Console.WriteLine("OK");
            }
            else
            {
                Console.WriteLine("FAILED (capture returned null)");
                failed++;
                proc.Kill();
                proc.WaitForExit();
                continue;
            }
        }
        succeeded++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED ({ex.Message})");
        failed++;
    }
    finally
    {
        proc.Kill();
        proc.WaitForExit();
    }
}

Console.WriteLine();
Console.WriteLine($"Done: {succeeded} succeeded, {failed} failed, {skipped} skipped");

// --- Helper functions ---

Bitmap? CaptureWindow(IntPtr window)
{
    NativeMethods.GetWindowRect(window, out var rect);
    int w = rect.Right - rect.Left;
    int h = rect.Bottom - rect.Top;
    if (w <= 0 || h <= 0) return null;

    var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    IntPtr hdc = g.GetHdc();
    bool ok = NativeMethods.PrintWindow(window, hdc, 2);
    g.ReleaseHdc(hdc);
    if (!ok) { bmp.Dispose(); return null; }
    return bmp;
}

IntPtr FindWindowByPid(int pid)
{
    IntPtr found = IntPtr.Zero;
    for (int attempt = 0; attempt < 20 && found == IntPtr.Zero; attempt++)
    {
        NativeMethods.EnumWindows((h, _) =>
        {
            if (NativeMethods.IsWindowVisible(h))
            {
                NativeMethods.GetWindowThreadProcessId(h, out uint foundPid);
                if (foundPid == pid) found = h;
            }
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) Thread.Sleep(250);
    }
    return found;
}

(int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments, int timeoutMs = 30000)
{
    var psi = new ProcessStartInfo(fileName, arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    using var p = Process.Start(psi)!;
    string stdout = p.StandardOutput.ReadToEnd();
    string stderr = p.StandardError.ReadToEnd();
    p.WaitForExit(timeoutMs);
    return (p.ExitCode, stdout, stderr);
}

static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
