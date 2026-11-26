using System.Net;
using System.Net.Sockets;
using System.Text;
using GalLens;

namespace TransTest;

internal static class Program
{
    private static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var listener = new TcpListener(IPAddress.Loopback, 18080);
        listener.Start();

        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var ns = client.GetStream();
            var head = new byte[8192];
            int n = await ns.ReadAsync(head);
            Console.WriteLine("[server] " + Encoding.ASCII.GetString(head, 0, n).Split('\n')[0].Trim());

            await ns.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nTransfer-Encoding: chunked\r\n\r\n"));

            string[] pieces = { "「", "原来", "如此", "，", "你", "就是", "那时", "的", "孩子", "啊", "」" };
            foreach (var piece in pieces)
            {
                var data = "data: {\"choices\":[{\"delta\":{\"content\":\"" + piece + "\"}}]}\n\n";
                await WriteChunk(ns, Encoding.UTF8.GetBytes(data));
                await Task.Delay(15);
            }
            await WriteChunk(ns, Encoding.UTF8.GetBytes("data: [DONE]\n\n"));
            await ns.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"));
            await ns.FlushAsync();
        });

        var sb = new StringBuilder();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long firstMs = -1;
        await foreach (var d in new Translator().TranslateAsync(
            "「……そうか。お前が、あの時の子供だったのか」",
            "遠野=远野",
            new List<(string, string)> { ("そうか", "这样啊") },
            "http://127.0.0.1:18080", "sk-test", "deepseek-chat", CancellationToken.None))
        {
            if (firstMs < 0) firstMs = sw.ElapsedMilliseconds;
            sb.Append(d);
            Console.Write(d);
        }
        sw.Stop();
        Console.WriteLine();
        Console.WriteLine($"[client] assembled = {sb}");
        Console.WriteLine($"[client] first chunk {firstMs}ms, total {sw.ElapsedMilliseconds}ms");
        listener.Stop();
        await server;

        await TestVisionAsync();
    }

    private static async Task TestVisionAsync()
    {
        var l2 = new TcpListener(IPAddress.Loopback, 18082);
        l2.Start();
        string seen = "";

        var server = Task.Run(async () =>
        {
            using var client = await l2.AcceptTcpClientAsync();
            using var ns = client.GetStream();
            var head = new byte[65536];
            int n = 0;
            // 读到请求体结束
            while (n < head.Length)
            {
                int r = await ns.ReadAsync(head.AsMemory(n));
                if (r == 0) break;
                n += r;
                var s = Encoding.UTF8.GetString(head, 0, n);
                if (s.Contains("\r\n\r\n") && s.Contains("image_url")) break;
                if (s.Contains("\r\n\r\n"))
                {
                    var idx = s.IndexOf("\r\n\r\n") + 4;
                    var cl = System.Text.RegularExpressions.Regex.Match(s, @"Content-Length:\s*(\d+)");
                    if (cl.Success && n >= idx + int.Parse(cl.Groups[1].Value)) break;
                }
            }
            seen = Encoding.UTF8.GetString(head, 0, n);

            await ns.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nTransfer-Encoding: chunked\r\n\r\n"));
            foreach (var piece in new[] { "「", "图像", "模式", "正常", "」" })
            {
                var data = "data: {\"choices\":[{\"delta\":{\"content\":\"" + piece + "\"}}]}\n\n";
                await WriteChunk(ns, Encoding.UTF8.GetBytes(data));
                await Task.Delay(15);
            }
            await WriteChunk(ns, Encoding.UTF8.GetBytes("data: [DONE]\n\n"));
            await ns.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"));
            await ns.FlushAsync();
        });

        int w = 64, h = 32;
        var frame = new Frame { Bgra = new byte[w * h * 4], Width = w, Height = h };
        for (int i = 0; i < frame.Bgra.Length; i += 4)
        {
            frame.Bgra[i] = 30; frame.Bgra[i + 1] = 30; frame.Bgra[i + 2] = 30; frame.Bgra[i + 3] = 255;
        }
        var png = ImageUtil.EncodePng(frame);
        Console.WriteLine($"[vision] png bytes = {png.Length}");

        var sb = new StringBuilder();
        await foreach (var d in new Translator().TranslateImageAsync(
            png, "", new List<(string, string)>(), "http://127.0.0.1:18082", "sk-test",
            "deepseek-flash", "auto", CancellationToken.None))
        {
            sb.Append(d);
            Console.Write(d);
        }

        await server;
        l2.Stop();
        Console.WriteLine();
        Console.WriteLine("[vision] assembled = " + sb);
        Console.WriteLine("[vision] payload has image_url = " + seen.Contains("image_url"));
        Console.WriteLine("[vision] payload has data:image/png;base64 = " + seen.Contains("data:image/png;base64"));
        Console.WriteLine("[vision] PASS = " + (sb.ToString().Contains("图像模式") && seen.Contains("image_url")));
    }

    private static async Task WriteChunk(NetworkStream ns, byte[] data)
    {
        await ns.WriteAsync(Encoding.ASCII.GetBytes(data.Length.ToString("x") + "\r\n"));
        await ns.WriteAsync(data);
        await ns.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
        await ns.FlushAsync();
    }
}