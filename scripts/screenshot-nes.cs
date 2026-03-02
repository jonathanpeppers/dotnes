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

// Bring to foreground, then capture via Alt+PrintScreen (clipboard)
NativeMethods.SetForegroundWindow(hwnd);
Thread.Sleep(1000);

// Send Alt+PrintScreen to capture active window to clipboard
NativeMethods.keybd_event(0x12, 0, 0, UIntPtr.Zero); // VK_MENU down
NativeMethods.keybd_event(0x2C, 0, 0, UIntPtr.Zero); // VK_SNAPSHOT down
NativeMethods.keybd_event(0x2C, 0, 2, UIntPtr.Zero); // VK_SNAPSHOT up
NativeMethods.keybd_event(0x12, 0, 2, UIntPtr.Zero); // VK_MENU up
Thread.Sleep(1000);

// Read bitmap from clipboard using Win32 APIs
Bitmap? bmp = null;
if (NativeMethods.OpenClipboard(IntPtr.Zero))
{
    IntPtr hBitmap = NativeMethods.GetClipboardData(2); // CF_BITMAP
    if (hBitmap != IntPtr.Zero)
    {
        bmp = Image.FromHbitmap(hBitmap);
    }
    NativeMethods.CloseClipboard();
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
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    public static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

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
