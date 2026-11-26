using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GalLens;

namespace Shot;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);

    private static void Main(string[] args)
    {
        var hwnd = new IntPtr(long.Parse(args[0]));
        var path = args[1];

        GetWindowRect(hwnd, out var wr);
        GetClientRect(hwnd, out var cr);
        Console.WriteLine($"window rect {wr.L},{wr.T} {wr.R - wr.L}x{wr.B - wr.T}");
        Console.WriteLine($"client rect {cr.R - cr.L}x{cr.B - cr.T}");
        Console.WriteLine($"screen {GetSystemMetrics(0)}x{GetSystemMetrics(1)}");

        var f = ScreenCapture.Capture(hwnd);
        if (f == null) { Console.WriteLine("capture failed"); return; }
        Console.WriteLine($"captured {f.Width}x{f.Height}");

        var bmp = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.Width * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
        Console.WriteLine($"saved {path}");
    }
}
