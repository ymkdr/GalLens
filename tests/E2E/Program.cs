using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GalLens;

namespace E2E;

internal static class Program
{
    /// <summary>从可执行文件目录往上找到仓库根（含 GalLens.slnx）。</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "GalLens.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    private static readonly StringBuilder Log = new();

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.txt"), e.ExceptionObject.ToString());
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("start");
        var app = new Application();
        var listener = new TcpListener(IPAddress.Loopback, 18081);
        listener.Start();
        _ = ServeOnce(listener);

        var win = new Window
        {
            Width = 1280, Height = 720, Left = 120, Top = 120,
            WindowStyle = WindowStyle.None, Background = new SolidColorBrush(Color.FromRgb(18, 22, 34)),
            ShowInTaskbar = false, Title = "fake-galgame-e2e"
        };

        var dial = new Border
        {
            Margin = new Thickness(80, 0, 80, 60),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(220, 20, 16, 28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 120, 130, 200)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(40, 26, 40, 26),
            Child = new TextBlock
            {
                Text = "「……そうか。お前が、あの時の子供だったのか」",
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Yu Gothic UI"),
                FontSize = 30,
                TextWrapping = TextWrapping.Wrap
            }
        };

        var root = new Grid();
        root.Children.Add(new TextBlock
        {
            Text = "触手の蠢く夜",
            Foreground = new SolidColorBrush(Color.FromArgb(90, 200, 210, 255)),
            FontFamily = new FontFamily("Yu Gothic UI"),
            FontSize = 90,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        root.Children.Add(dial);
        win.Content = root;
        win.Show();

        win.Loaded += (_, _) => Console.WriteLine("loaded");
        win.ContentRendered += (_, _) =>
        {
            Console.WriteLine("rendered");
            try { Run(win, app, listener, dial); }
            catch (Exception ex)
            {
                Console.WriteLine("EX in Run: " + ex);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.txt"), ex.ToString());
            }
        };

        Console.WriteLine("before run");
        app.Run(win);
        Console.WriteLine("after run");
    }

    private static void Run(Window win, Application app, TcpListener listener, Border dial)
    {
        var hwnd = new WindowInteropHelper(win).Handle;
        SetForegroundWindow(hwnd);

        var frame = ScreenCapture.Capture(hwnd);
        double scale = frame.Width / win.ActualWidth;
        var pos = dial.TranslatePoint(new Point(0, 0), win);

        var settings = new AppSettings
        {
            BaseUrl = "http://127.0.0.1:18081",
            Model = "deepseek-chat",
            RoiX = (int)(pos.X * scale),
            RoiY = (int)(pos.Y * scale),
            RoiW = (int)(dial.ActualWidth * scale),
            RoiH = (int)(dial.ActualHeight * scale)
        };
        settings.ApiKey = "sk-test";
        Log.AppendLine($"[roi] {settings.RoiX},{settings.RoiY} {settings.RoiW}x{settings.RoiH} (scale {scale:F3})");

        var ocr = OcrService.TryCreate("ja", "ja-JP");
        var cache = new Dictionary<string, string>();
        var pipe = new Pipeline(hwnd, hwnd, ocr, () => settings, Dispatcher.CurrentDispatcher, cache);

        string lastOcr = "", lastZh = "";
        long latency = -1;
        pipe.OcrUpdated += s => { lastOcr = s; Log.AppendLine("[ocr] " + s); };
        pipe.TranslationUpdated += s => { lastZh = s; };
        pipe.LatencyMs += ms => latency = ms;
        pipe.Start();

        OverlayWindow overlay = null;
        bool done = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) =>
        {
            if (done || lastZh.Length == 0) return;

            if (overlay == null)
            {
                overlay = new OverlayWindow(false);
                overlay.ApplyStyle(new SubtitleStyle { FontSize = 28, BackgroundOpacity = 0.72 });
                overlay.Show();
                var sp = new Native.POINT { X = settings.RoiX, Y = settings.RoiY };
                Native.ClientToScreen(hwnd, ref sp);
                overlay.Place(sp.X, sp.Y, settings.RoiW, settings.RoiH);
            }
            overlay.SetText(lastZh);
            if (!lastZh.EndsWith("」")) return;

            done = true;
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                timer.Stop();

                Native.GetWindowRect(hwnd, out var wr);
                var shot = ScreenCapture.CaptureDesktop(wr.Left, wr.Top, wr.Width, wr.Height);
                if (shot != null)
                {
                    var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                        shot.Width, shot.Height, 96, 96, PixelFormats.Bgra32, null,
                        shot.Bgra, shot.Width * 4);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using (var fs = File.Create(Path.Combine(RepoRoot(), "docs", "demo.png")))
                        enc.Save(fs);
                    Log.AppendLine("[demo] saved");
                }
                else Log.AppendLine("[demo] capture failed");

                Log.AppendLine("[result] ocr=" + lastOcr.Replace("\n", " / "));
                Log.AppendLine("[result] zh=" + lastZh);
                Log.AppendLine("[result] latency=" + latency + "ms");
                Log.AppendLine("[result] cacheEntries=" + cache.Count);
                Log.AppendLine("[result] PASS=" + (lastZh.Length > 0 && lastZh.Contains("孩子")));

                pipe.Stop();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), Log.ToString(), Encoding.UTF8);
                overlay.Close();
                win.Close();
                listener.Stop();
                app.Shutdown();
            };
            delay.Start();
        };
        timer.Start();
    }

    private static async Task ServeOnce(TcpListener l)
    {
        try
        {
            using var client = await l.AcceptTcpClientAsync();
            using var ns = client.GetStream();
            var head = new byte[8192];
            await ns.ReadAsync(head);
            await ns.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nTransfer-Encoding: chunked\r\n\r\n"));

            foreach (var piece in new[] { "「", "原来", "如此", "，", "你就是", "那时", "的", "孩子", "啊", "」" })
            {
                var data = "data: {\"choices\":[{\"delta\":{\"content\":\"" + piece + "\"}}]}\n\n";
                await Chunk(ns, Encoding.UTF8.GetBytes(data));
                await Task.Delay(25);
            }
            await Chunk(ns, Encoding.UTF8.GetBytes("data: [DONE]\n\n"));
            await ns.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"));
        }
        catch { }
    }

    private static async Task Chunk(NetworkStream ns, byte[] d)
    {
        await ns.WriteAsync(Encoding.ASCII.GetBytes(d.Length.ToString("x") + "\r\n"));
        await ns.WriteAsync(d);
        await ns.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
        await ns.FlushAsync();
    }
}