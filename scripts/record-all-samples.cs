// Usage: dotnet run scripts/record-all-samples.cs
// Builds and runs all samples via `dotnet run`, captures PNG/GIF for each
// into samples/{name}/{name}.png or .gif
// Requires: .NET 10+, Windows
#:package System.Drawing.Common@9.*
#:package AnimatedGif@1.*
#:property NoWarn=CA1416

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AnimatedGif;

string repoRoot = Path.GetFullPath(".");

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

    // dotnet run builds and launches the emulator
    Console.Write($"  Launching... ");
    var proc = Process.Start(new ProcessStartInfo("dotnet", "run")
    {
        WorkingDirectory = sampleDir,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    });
    if (proc == null)
    {
        Console.WriteLine("FAILED (couldn't start)");
        failed++;
        continue;
    }

    Thread.Sleep(5000); // Wait for build + emulator boot

    // Find the Mesen emulator window (spawned by running the sample)
    IntPtr hwnd = FindMesenWindow();
    if (hwnd == IntPtr.Zero)
    {
        Console.WriteLine("FAILED (couldn't find emulator window)");
        proc.Kill();
        failed++;
        continue;
    }

    Console.Write($"  Recording {ext}... ");

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
        // Kill the Mesen emulator process (grandchild of dotnet run)
        KillMesenProcess();
        try { proc.Kill(); } catch { }
        try { proc.WaitForExit(3000); } catch { }
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

IntPtr FindMesenWindow()
{
    // Mesen window title contains "Mesen" — find it regardless of parent PID
    // since dotnet run spawns it as a grandchild process
    IntPtr found = IntPtr.Zero;
    for (int attempt = 0; attempt < 20 && found == IntPtr.Zero; attempt++)
    {
        NativeMethods.EnumWindows((h, _) =>
        {
            if (NativeMethods.IsWindowVisible(h))
            {
                int len = NativeMethods.GetWindowTextLength(h);
                if (len > 0)
                {
                    var sb = new System.Text.StringBuilder(len + 1);
                    NativeMethods.GetWindowText(h, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (title.Contains("Mesen", StringComparison.OrdinalIgnoreCase))
                        found = h;
                }
            }
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) Thread.Sleep(500);
    }
    return found;
}

void KillMesenProcess()
{
    foreach (var p in Process.GetProcesses())
    {
        try
        {
            if (p.ProcessName.Contains("Mesen", StringComparison.OrdinalIgnoreCase))
                p.Kill();
        }
        catch { }
    }
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
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
