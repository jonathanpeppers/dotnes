// Quick test to count segments from AssemblyReader
using dotnes;

var reader = new AssemblyReader(args[0]);
int segCount = 0;
int totalBytes = 0;
foreach (var seg in reader.GetSegments())
{
    segCount++;
    totalBytes += seg.Bytes.Length;
    Console.WriteLine($"Segment {segCount}: name={seg.Name}, bytes={seg.Bytes.Length}");
}
Console.WriteLine($"Total: {segCount} segments, {totalBytes} bytes");
