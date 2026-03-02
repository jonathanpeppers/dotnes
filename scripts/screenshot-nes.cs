// Usage: dotnet run scripts/screenshot-nes.cs -- <rom_path> [delay_ms] [output.png]
// Requires: .NET 10+
#:package System.Drawing.Common@9.*
#:property NoWarn=CA1416

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Parse args
string romPath = args.Length > 0 ? args[0] : throw new Exception("Usage: dotnet run scripts/screenshot-nes.cs -- <rom> [delay_ms] [output.png]");
int delayMs = args.Length > 1 ? int.Parse(args[1]) : 3000;
string outputPath = args.Length > 2 ? args[2] : "screenshot.png";

// Find anese.exe
string repoRoot = Path.GetFullPath(".");
string anesePath = Path.Combine(repoRoot, "src", "dotnes.anese", "obj", "Debug", "win", "anese.exe");
if (!File.Exists(anesePath))
{
    Console.Error.WriteLine($"ERROR: anese.exe not found at {anesePath}");
    return;
}

romPath = Path.GetFullPath(romPath);
if (!File.Exists(romPath))
{
    Console.Error.WriteLine($"ERROR: ROM not found: {romPath}");
    return;
}

Console.WriteLine($"Launching ANESE with {romPath}...");
var proc = Process.Start(new ProcessStartInfo(anesePath, $"\"{romPath}\"") { UseShellExecute = false })!;

Console.WriteLine($"Waiting {delayMs}ms for emulator to render...");
Thread.Sleep(delayMs);

// Find window by PID
IntPtr hwnd = IntPtr.Zero;
int pid = proc.Id;
for (int attempt = 0; attempt < 20 && hwnd == IntPtr.Zero; attempt++)
{
    NativeMethods.EnumWindows((h, _) =>
    {
        if (NativeMethods.IsWindowVisible(h))
        {
            NativeMethods.GetWindowThreadProcessId(h, out uint foundPid);
            if (foundPid == pid)
                hwnd = h;
        }
        return hwnd == IntPtr.Zero;
    }, IntPtr.Zero);
    if (hwnd == IntPtr.Zero) Thread.Sleep(250);
}

if (hwnd == IntPtr.Zero)
{
    Console.Error.WriteLine("ERROR: Could not find ANESE window");
    proc.Kill();
    return;
}

// Capture via PrintWindow with PW_RENDERFULLCONTENT (flag 2)
// This captures hardware-accelerated (SDL2/DX) windows on Win 8.1+
NativeMethods.GetWindowRect(hwnd, out var rect);
int w = rect.Right - rect.Left;
int h = rect.Bottom - rect.Top;
Bitmap? bmp = null;
if (w > 0 && h > 0)
{
    bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    IntPtr hdc = g.GetHdc();
    bool ok = NativeMethods.PrintWindow(hwnd, hdc, 2); // PW_RENDERFULLCONTENT
    g.ReleaseHdc(hdc);
    if (!ok)
    {
        bmp.Dispose();
        bmp = null;
    }
}

if (bmp == null)
{
    Console.Error.WriteLine("ERROR: Failed to capture screenshot from clipboard");
    proc.Kill();
    proc.WaitForExit();
    return;
}

using (bmp)
{

outputPath = Path.GetFullPath(outputPath);
    bmp.Save(outputPath, ImageFormat.Png);
    Console.WriteLine($"Screenshot saved to {outputPath}");
}

proc.Kill();
proc.WaitForExit();
Console.WriteLine("Done.");

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
