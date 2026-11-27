using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GalLens;

namespace LiveTest;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);

    [STAThread]
    private static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var log = new StringBuilder();
        void W(string s) { Console.WriteLine(s); log.AppendLine(s); }

        var procName = args.Length > 0 ? args[0] : "殻ノ少女";
        var seconds = args.Length > 1 ? int.Parse(args[1]) : 14;

        var proc = Process.GetProcessesByName(procName).FirstOrDefault();
        if (proc == null) { Console.WriteLine("process not found: " + procName); return; }

        var hwnd = proc.MainWindowHandle;
        W($"target: {procName} pid={proc.Id} hwnd={hwnd}");

        ShowWindow(hwnd, 9);          // SW_RESTORE
        SetForegroundWindow(hwnd);
        Thread.Sleep(800);

        var settings = AppStorage.LoadSettings();
        if (args.Length >= 6)
        {
            settings.RoiX = int.Parse(args[2]);
            settings.RoiY = int.Parse(args[3]);
            settings.RoiW = int.Parse(args[4]);
            settings.RoiH = int.Parse(args[5]);
        }
        W($"roi saved: {settings.RoiX},{settings.RoiY} {settings.RoiW}x{settings.RoiH}  model={settings.Model}");
        W($"regions: {string.Join(",", settings.Regions.Keys)}");

        var ocr = OcrService.TryCreate("ja", "ja-JP");
        W("ocr: " + (ocr == null ? "NULL" : ocr.Tag));

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var cache = new Dictionary<string, string>();

        var overlay = new OverlayWindow(false);
        overlay.ApplyStyle(settings.Style);
        overlay.SetText("……");
        overlay.Show();
        overlay.Interactive = true;   // 让截图里能看到按钮
        var p0 = new Native.POINT { X = settings.RoiX, Y = settings.RoiY };
        Native.ClientToScreen(hwnd, ref p0);
        int w = Math.Max(settings.RoiW, 560);
        int h = Math.Max(settings.RoiH, 120);
        int ox = Math.Max(0, p0.X - (w - settings.RoiW));
        overlay.Place(ox, p0.Y, w, h);

        var pipe = new Pipeline(hwnd, new WindowInteropHelper(overlay).Handle, ocr,
            () => settings, Dispatcher.CurrentDispatcher, cache);

        pipe.OcrUpdated += s => W("[ocr] " + s.Replace("\n", " / "));
        pipe.TranslationUpdated += s => { W("[zh ] " + s); overlay.SetText(s); };
        pipe.Status += s => W("[sts] " + s);
        pipe.LatencyMs += ms => W("[lat] " + ms + "ms");
        pipe.TermsFound += list => W("[term] " + string.Join(" | ", list));

        pipe.Start();

        bool forceVision = args.Contains("vision");
        if (forceVision)
        {
            var t2 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t2.Tick += (_, _) => { t2.Stop(); W("[act] force vision"); pipe.ForceVision(); };
            t2.Start();
        }

        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            pipe.Stop();

            Native.GetWindowRect(hwnd, out var wr);
            var frame = ScreenCapture.Capture(hwnd);
            if (frame != null)
            {
                var crop = frame.Crop(settings.RoiX, settings.RoiY, settings.RoiW, settings.RoiH);
                SavePng(crop, Path.Combine(AppContext.BaseDirectory, "roi.png"));
            }

            var shot = ScreenCapture.CaptureDesktop(wr.Left, wr.Top, wr.Width, wr.Height);
            if (shot != null)
            {
                var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                    shot.Width, shot.Height, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null, shot.Bgra, shot.Width * 4);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(AppContext.BaseDirectory, "live.png"));
                enc.Save(fs);
                W("[shot] saved live.png " + shot.Width + "x" + shot.Height);
            }

            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "live.log"), log.ToString(), Encoding.UTF8);
            overlay.Close();
            app.Shutdown();
        };
        t.Start();
        app.Run();
    }

    private static void SavePng(Frame f, string path)
    {
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
            f.Width, f.Height, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, f.Bgra, f.Width * 4);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}