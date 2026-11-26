using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace GalLens;

public sealed class Translator
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public const string DefaultSystemPrompt =
        "你是一个日文 Galgame 实时字幕翻译器，把日文台词译成自然的简体中文。\n" +
        "规则：\n" +
        "1. 只输出译文，不要原文、不要解释、不要引号、不要\"翻译：\"之类的前缀。\n" +
        "2. 语气口语化，贴合角色身份；避免翻译腔、避免书面语。\n" +
        "3. 保留原文的「」等符号和换行结构。\n" +
        "4. 专有名词严格按【术语表】翻译；表中没有的，人名保留日文原文。\n" +
        "5. 拟声词译成中文习惯表达，不要音译。\n" +
        "6. 不要把一句话拆成多句，也不要合并上文。\n" +
        "7. 输入里可能有 OCR 噪声（只有符号、读不通的假名堆、UI 按钮残留）。\n" +
        "   这些片段直接跳过，不要照抄进译文。\n" +
        "8. 译文里不允许出现日文原文；宁可少译，也不要把它原样贴出来。";

    public static string BuildUserMessage(string jp, string glossary, IReadOnlyList<(string Jp, string Zh)> context)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(glossary))
        {
            sb.AppendLine("【术语表】");
            sb.AppendLine(glossary.Trim());
            sb.AppendLine();
        }
        if (context.Count > 0)
        {
            sb.AppendLine("【已译上文（仅供语气衔接，不要重复翻译）】");
            foreach (var (j, z) in context)
            {
                sb.AppendLine("日：" + j.Replace("\n", " "));
                sb.AppendLine("中：" + z.Replace("\n", " "));
            }
            sb.AppendLine();
        }
        sb.AppendLine("【待译】");
        sb.Append("日：" + jp);
        return sb.ToString();
    }

    public static string BuildVisionMessage(string glossary, IReadOnlyList<(string Jp, string Zh)> context)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(glossary))
        {
            sb.AppendLine("【术语表】");
            sb.AppendLine(glossary.Trim());
            sb.AppendLine();
        }
        if (context.Count > 0)
        {
            sb.AppendLine("【已译上文（仅供语气衔接）】");
            foreach (var (j, z) in context)
                sb.AppendLine("中：" + z.Replace("\n", " "));
            sb.AppendLine();
        }
        sb.AppendLine("【任务】");
        sb.Append("图片是一张 galgame 截图，里面有一句日文台词。请先在心里读准图上的日文，再直接给出简体中文译文。只输出译文。");
        return sb.ToString();
    }

    public IAsyncEnumerable<string> TranslateAsync(
        string jp, string glossary, IReadOnlyList<(string Jp, string Zh)> context,
        string baseUrl, string apiKey, string model, CancellationToken ct)
    {
        var messages = new object[]
        {
            new { role = "system", content = DefaultSystemPrompt },
            new { role = "user", content = BuildUserMessage(jp, glossary, context) }
        };
        return PostAsync(BuildPayload(model, messages, 0.4, baseUrl), baseUrl, apiKey, ct);
    }

    /// <summary>
    /// DeepSeek 默认开启思考模式，对实时字幕是灾难（每次先想好几秒）。
    /// 只在 deepseek 官方域名下关闭它，避免给别的服务商塞不认识的字段。
    /// </summary>
    private static Dictionary<string, object> BuildPayload(string model, object[] messages, double temperature, string baseUrl)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["stream"] = true,
            ["max_tokens"] = 600,
            ["messages"] = messages
        };

        if (IsDeepSeek(baseUrl))
        {
            payload["thinking"] = new Dictionary<string, object> { ["type"] = "disabled" };
        }
        else
        {
            payload["temperature"] = temperature;
        }

        return payload;
    }

    private static bool IsDeepSeek(string baseUrl)
    {
        try { return new Uri(baseUrl).Host.Contains("deepseek", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public IAsyncEnumerable<string> TranslateImageAsync(
        byte[] png, string glossary, IReadOnlyList<(string Jp, string Zh)> context,
        string baseUrl, string apiKey, string model, string detail, CancellationToken ct)
    {
        var dataUrl = "data:image/png;base64," + Convert.ToBase64String(png);

        var messages = new object[]
        {
            new { role = "system", content = DefaultSystemPrompt },
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = BuildVisionMessage(glossary, context) },
                    new { type = "image_url", image_url = new { url = dataUrl, detail } }
                }
            }
        };
        return PostAsync(BuildPayload(model, messages, 0.2, baseUrl), baseUrl, apiKey, ct);
    }

    private static async IAsyncEnumerable<string> PostAsync(
        Dictionary<string, object> payload, string baseUrl, string apiKey, [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("还没有填写 API Key。");

        var endpoint = baseUrl.TrimEnd('/');
        if (!endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            endpoint += "/chat/completions";

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (body.Length > 400) body = body[..400];
            throw new InvalidOperationException($"接口返回 {(int)resp.StatusCode}：{body}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (line.Length == 0) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") break;

            string? delta = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var c0 = choices[0];
                    if (c0.TryGetProperty("delta", out var d) &&
                        d.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String)
                        delta = content.GetString();
                }
            }
            catch (JsonException) { continue; }

            if (!string.IsNullOrEmpty(delta)) yield return delta;
        }
    }
}