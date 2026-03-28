// Usage: dotnet run scripts/record-nes.cs -- <rom.nes> [options]
// Options:
//   --gif              Record animated GIF instead of PNG screenshot
//   --frames N         Number of frames to capture (default: 30)
//   --interval N       Milliseconds between frames (default: 100)
//   --delay N          Milliseconds to wait before first capture (default: 3000)
//   --output FILE      Output file path (default: screenshot.png or recording.gif)
// Requires: .NET 10+, Windows
#:package System.Drawing.Common@9.*
#:package AnimatedGif@1.*
#:property NoWarn=CA1416

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AnimatedGif;

// Parse args
bool gifMode = false;
int frameCount = 30;
int intervalMs = 100;
int delayMs = 3000;
string? outputPath = null;
string? romPath = null;

for (int a = 0; a < args.Length; a++)
{
    switch (args[a])
    {
        case "--gif": gifMode = true; break;
        case "--frames": frameCount = int.Parse(args[++a]); break;
        case "--interval": intervalMs = int.Parse(args[++a]); break;
        case "--delay": delayMs = int.Parse(args[++a]); break;
        case "--output": outputPath = args[++a]; break;
        default:
            if (romPath == null) romPath = args[a];
            else { Console.Error.WriteLine($"Unknown argument: {args[a]}"); return; }
            break;
    }
}

if (romPath == null)
{
    Console.Error.WriteLine("Usage: dotnet run scripts/record-nes.cs -- <rom.nes> [--gif] [--frames N] [--interval N] [--delay N] [--output FILE]");
    return;
}

outputPath ??= gifMode ? "recording.gif" : "screenshot.png";

// Find Mesen
string repoRoot = Path.GetFullPath(".");
string mesenPath = Path.Combine(repoRoot, "src", "dotnes.mesen", "bin",
    OperatingSystem.IsWindows() ? "Mesen.exe" :
    OperatingSystem.IsMacOS() ? Path.Combine("Mesen.app", "Contents", "MacOS", "Mesen") :
    "Mesen");
if (!File.Exists(mesenPath))
{
    Console.Error.WriteLine($"ERROR: Mesen not found at {mesenPath}");
    return;
}

romPath = Path.GetFullPath(romPath);
if (!File.Exists(romPath))
{
    Console.Error.WriteLine($"ERROR: ROM not found: {romPath}");
    return;
}

Console.WriteLine($"Launching Mesen with {Path.GetFileName(romPath)}...");
var proc = Process.Start(new ProcessStartInfo(mesenPath, $"--doNotSaveSettings \"{romPath}\"") { UseShellExecute = false })!;

Console.WriteLine($"Waiting {delayMs}ms for emulator to render...");
Thread.Sleep(delayMs);

// Find window by PID
IntPtr hwnd = FindWindowByPid(proc.Id);
if (hwnd == IntPtr.Zero)
{
    Console.Error.WriteLine("ERROR: Could not find Mesen window");
    proc.Kill();
    return;
}

if (gifMode)
{
    RecordGif(hwnd, outputPath, frameCount, intervalMs);
}
else
{
    var bmp = CaptureWindow(hwnd);
    if (bmp != null)
    {
        outputPath = Path.GetFullPath(outputPath);
        bmp.Save(outputPath, ImageFormat.Png);
        bmp.Dispose();
        Console.WriteLine($"Screenshot saved to {outputPath}");
    }
    else
    {
        Console.Error.WriteLine("ERROR: Failed to capture screenshot");
    }
}

proc.Kill();
proc.WaitForExit();
Console.WriteLine("Done.");

// --- Functions ---

void RecordGif(IntPtr window, string output, int frames, int interval)
{
    output = Path.GetFullPath(output);
    Console.WriteLine($"Recording {frames} frames at {interval}ms intervals...");

    using var gif = AnimatedGif.AnimatedGif.Create(output, interval);
    int captured = 0;
    for (int i = 0; i < frames; i++)
    {
        var bmp = CaptureWindow(window);
        if (bmp != null)
        {
            // Crop to just the game area (remove window chrome)
            var cropped = CropToGameArea(bmp);
            gif.AddFrame(cropped, delay: interval, quality: GifQuality.Bit8);
            cropped.Dispose();
            if (cropped != bmp) bmp.Dispose();
            captured++;
        }
        if (i < frames - 1)
            Thread.Sleep(interval);
    }
    Console.WriteLine($"GIF saved to {output} ({captured} frames)");
}

Bitmap? CaptureWindow(IntPtr window)
{
    NativeMethods.GetWindowRect(window, out var rect);
    int w = rect.Right - rect.Left;
    int h = rect.Bottom - rect.Top;
    if (w <= 0 || h <= 0) return null;

    var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    IntPtr hdc = g.GetHdc();
    bool ok = NativeMethods.PrintWindow(window, hdc, 2); // PW_RENDERFULLCONTENT
    g.ReleaseHdc(hdc);
    if (!ok)
    {
        bmp.Dispose();
        return null;
    }
    return bmp;
}

Bitmap CropToGameArea(Bitmap src)
{
    // Mesen window has title bar, menu bar, and borders — find the NES rendering area
    // NES resolution is 256x240, Mesen scales it up
    // Simple approach: find the non-black content bounds
    // For now, just return the full bitmap — the window chrome is minimal
    return src;
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
                if (foundPid == pid)
                    found = h;
            }
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) Thread.Sleep(250);
    }
    return found;
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
