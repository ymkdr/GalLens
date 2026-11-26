using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using GalLens;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var log = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "result.txt"), false, Encoding.UTF8) { AutoFlush = true };
        var app = new Application();
        var win = new Window
        {
            Width = 1280, Height = 720, Left = 100, Top = 100,
            WindowStyle = WindowStyle.None, Background = Brushes.Black,
            ShowInTaskbar = true, Title = "fake-galgame"
        };

        var grid = new Grid { Background = Brushes.Black };
        var box = new Border
        {
            Margin = new Thickness(80, 0, 80, 60),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(220, 12, 12, 20)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(40, 24, 40, 24),
            Child = new TextBlock
            {
                Text = "「……そうか。お前が、あの時の子供だったのか」\n" +
                       "「なら、もう一度だけ聞かせてくれ。なぜ、あの日、君は笑っていたんだ？」",
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Yu Gothic UI"),
                FontSize = 30,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 48
            }
        };
        grid.Children.Add(box);
        win.Content = grid;
        win.Show();

        win.ContentRendered += async (_, _) =>
        {
            await Task.Delay(900);
            try { await RunTest(win, log); }
            catch (Exception ex) { log.WriteLine("EX: " + ex); }
            win.Close();
            app.Shutdown();
        };
        app.Run(win);
    }

    private static async Task RunTest(Window win, StreamWriter log)
    {
        var hwnd = new WindowInteropHelper(win).Handle;
        var ocr = OcrService.TryCreate("ja", "ja-JP");
        log.WriteLine("OCR engine: " + (ocr == null ? "NULL" : ocr.Tag));
        if (ocr == null) return;

        long capMs = 0, ocrMs = 0, totalMs = 0;
        string last = "";
        const int N = 20;
        for (int i = 0; i < N; i++)
        {
            var sw = Stopwatch.StartNew();
            var f = ScreenCapture.Capture(hwnd);
            long c = sw.ElapsedMilliseconds;
            if (f == null) { log.WriteLine("capture NULL"); return; }
            int ry = (int)(f.Height * 0.72);
            var crop = f.Crop(0, ry, f.Width, f.Height - ry);
            long c2 = sw.ElapsedMilliseconds;
            var text = TextUtil.Normalize(await ocr.RecognizeAsync(crop));
            long o = sw.ElapsedMilliseconds;
            capMs += c; ocrMs += (c2 - c); totalMs += o;
            last = text;
            if (i == 0) log.WriteLine($"frame={f.Width}x{f.Height} crop={crop.Width}x{crop.Height}");
        }

        log.WriteLine("--- average over " + N + " frames ---");
        log.WriteLine($"capture  {capMs / (double)N:F1} ms");
        log.WriteLine($"crop     {ocrMs / (double)N:F1} ms");
        log.WriteLine($"ocr      {(totalMs - capMs - ocrMs) / (double)N:F1} ms");
        log.WriteLine($"total    {totalMs / (double)N:F1} ms");
        log.WriteLine("--- recognized ---");
        foreach (var line in last.Split('\n')) log.WriteLine("[" + line + "]");
        log.WriteLine("--- expected ---");
        log.WriteLine("「……そうか。お前が、あの時の子供だったのか」");
        log.WriteLine("「なら、もう一度だけ聞かせてくれ。なぜ、あの日、君は笑っていたんだ？」");
    }
}