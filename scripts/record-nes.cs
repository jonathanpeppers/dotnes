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

Console.WriteLine($"Launching ANESE with {Path.GetFileName(romPath)}...");
var proc = Process.Start(new ProcessStartInfo(anesePath, $"\"{romPath}\" --no-sav") { UseShellExecute = false })!;

Console.WriteLine($"Waiting {delayMs}ms for emulator to render...");
Thread.Sleep(delayMs);

// Find window by PID
IntPtr hwnd = FindWindowByPid(proc.Id);
if (hwnd == IntPtr.Zero)
{
    Console.Error.WriteLine("ERROR: Could not find ANESE window");
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
    // Force ANESE window to topmost and foreground
    NativeMethods.ShowWindow(window, 9); // SW_RESTORE
    NativeMethods.SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040); // HWND_TOPMOST, SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW
    NativeMethods.SetForegroundWindow(window);
    Thread.Sleep(1000);

    // Get window position on screen
    NativeMethods.GetWindowRect(window, out var rect);
    int w = rect.Right - rect.Left;
    int h = rect.Bottom - rect.Top;
    if (w <= 0 || h <= 0) return null;

    // Method 1: Alt+PrintScreen → clipboard (DWM compositor capture)
    // Clear clipboard first
    NativeMethods.OpenClipboard(IntPtr.Zero);
    NativeMethods.EmptyClipboard();
    NativeMethods.CloseClipboard();

    // Simulate Alt+PrintScreen
    NativeMethods.keybd_event(0x12, 0, 0, UIntPtr.Zero); // VK_MENU down
    NativeMethods.keybd_event(0x2C, 0, 0, UIntPtr.Zero); // VK_SNAPSHOT down
    NativeMethods.keybd_event(0x2C, 0, 2, UIntPtr.Zero); // VK_SNAPSHOT up
    NativeMethods.keybd_event(0x12, 0, 2, UIntPtr.Zero); // VK_MENU up
    Thread.Sleep(500);

    // Read image from clipboard
    if (NativeMethods.OpenClipboard(IntPtr.Zero))
    {
        if (NativeMethods.IsClipboardFormatAvailable(2)) // CF_BITMAP
        {
            IntPtr hBitmap = NativeMethods.GetClipboardData(2);
            if (hBitmap != IntPtr.Zero)
            {
                var fullScreen = Image.FromHbitmap(hBitmap);
                NativeMethods.CloseClipboard();
                Console.WriteLine("[Capture] Alt+PrintScreen succeeded via clipboard");
                // Crop to just the ANESE window bounds
                var cropped = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(cropped))
                    g.DrawImage(fullScreen, 0, 0, new Rectangle(rect.Left, rect.Top, w, h), GraphicsUnit.Pixel);
                fullScreen.Dispose();
                return cropped;
            }
        }
        NativeMethods.CloseClipboard();
    }
    Console.WriteLine("[Capture] Alt+PrintScreen failed, trying CopyFromScreen...");

    // Method 2: CopyFromScreen fallback
    var bmp2 = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp2))
        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(w, h));
    return bmp2;
}

Bitmap CropToGameArea(Bitmap src)
{
    // ANESE window has title bar and borders — find the NES rendering area
    // NES resolution is 256x240, ANESE scales it up
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

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    public static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    public static extern bool IsClipboardFormatAvailable(uint format);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
