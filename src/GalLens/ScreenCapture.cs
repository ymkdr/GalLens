using System.Runtime.InteropServices;

namespace GalLens;

public sealed class Frame
{
    public required byte[] Bgra { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    public Frame Crop(int x, int y, int w, int h)
    {
        x = Math.Clamp(x, 0, Math.Max(0, Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, Height - 1));
        w = Math.Clamp(w, 1, Width - x);
        h = Math.Clamp(h, 1, Height - y);
        var buf = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
        {
            int src = ((y + row) * Width + x) * 4;
            Buffer.BlockCopy(Bgra, src, buf, row * w * 4, w * 4);
        }
        return new Frame { Bgra = buf, Width = w, Height = h };
    }
}

public static class ScreenCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr sec, uint off);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();

    /// <summary>捕获窗口客户区。PrintWindow 优先，失败则退回屏幕 BitBlt。</summary>
    public static Frame? Capture(IntPtr hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out var wr)) return null;
        if (!Native.GetClientRect(hwnd, out var cr)) return null;

        int cw = cr.Width, ch = cr.Height;
        if (cw <= 0 || ch <= 0) return null;

        var origin = new Native.POINT();
        Native.ClientToScreen(hwnd, ref origin);

        // PrintWindow 是把「整个窗口」画到 DC 上的，包含标题栏和边框。
        // 所以先按窗口尺寸画，再把客户区裁出来，否则底部会被切掉、ROI 也会整体偏移。
        int offX = origin.X - wr.Left;
        int offY = origin.Y - wr.Top;

        var full = Blit(wr.Width, wr.Height, hdc => Native.PrintWindow(hwnd, hdc, 2));
        if (full != null)
        {
            var clientArea = full.Crop(offX, offY, cw, ch);
            if (clientArea.Width == cw && clientArea.Height == ch && !IsMostlyBlank(clientArea))
                return clientArea;
        }

        // 兜底：直接从屏幕对应位置拷客户区
        var screen = Native.GetDC(IntPtr.Zero);
        try
        {
            var g = Blit(cw, ch, hdc => Native.BitBlt(hdc, 0, 0, cw, ch, screen, origin.X, origin.Y, 0x00CC0020));
            if (g != null && !IsMostlyBlank(g)) return g;
            return g ?? full;
        }
        finally { Native.ReleaseDC(IntPtr.Zero, screen); }
    }

    private static Frame? Blit(int w, int h, Action<IntPtr> draw)
    {
        IntPtr hdc = CreateCompatibleDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return null;
        var bi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };
        IntPtr hbmp = CreateDIBSection(hdc, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (hbmp == IntPtr.Zero) { DeleteDC(hdc); return null; }
        IntPtr old = SelectObject(hdc, hbmp);
        try
        {
            draw(hdc);
            GdiFlush();
            var buf = new byte[w * h * 4];
            Marshal.Copy(bits, buf, 0, buf.Length);
            return new Frame { Bgra = buf, Width = w, Height = h };
        }
        finally
        {
            SelectObject(hdc, old);
            DeleteObject(hbmp);
            DeleteDC(hdc);
        }
    }

    /// <summary>直接抓屏幕上的一块矩形（会包含叠在上面的其它窗口）。</summary>
    public static Frame? CaptureDesktop(int x, int y, int w, int h)
    {
        var screen = Native.GetDC(IntPtr.Zero);
        try { return Blit(w, h, hdc => Native.BitBlt(hdc, 0, 0, w, h, screen, x, y, 0x00CC0020)); }
        finally { Native.ReleaseDC(IntPtr.Zero, screen); }
    }

    /// <summary>黑屏检测：D3D 独占全屏或受保护内容会返回全黑。</summary>
    public static bool IsMostlyBlank(Frame f)
    {
        int step = Math.Max(4, f.Bgra.Length / 4 / 4000) * 4;
        int lit = 0, total = 0;
        for (int i = 0; i < f.Bgra.Length; i += step)
        {
            total++;
            if (f.Bgra[i] + f.Bgra[i + 1] + f.Bgra[i + 2] > 24) lit++;
        }
        return total == 0 || lit * 100.0 / total < 1.0;
    }

    /// <summary>轻量指纹，用于判断区域画面有没有变化。</summary>
    public static ulong Hash(Frame f)
    {
        ulong h = 1469598103934665603UL;
        int step = Math.Max(4, (f.Width * f.Height / 2000)) * 4;
        for (int i = 0; i < f.Bgra.Length; i += step)
        {
            h ^= f.Bgra[i] | ((ulong)f.Bgra[i + 1] << 8) | ((ulong)f.Bgra[i + 2] << 16);
            h *= 1099511628211UL;
        }
        return h;
    }
}
