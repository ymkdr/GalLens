using System.Text;
using System.Windows.Threading;

namespace GalLens;

public sealed class Pipeline
{
    private readonly IntPtr _hwnd;
    private readonly IntPtr _ownerHwnd;
    private readonly OcrService _ocr;
    private readonly Func<AppSettings> _settings;
    private readonly Dispatcher _ui;
    private readonly Dictionary<string, string> _cache;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _translateCts;
    private readonly List<(string Jp, string Zh)> _context = new();
    private readonly object _saveLock = new();
    private string _lastJp = "";
    private Frame? _lastFrame;

    public event Action<string>? OcrUpdated;
    public event Action<string>? TranslationUpdated;
    public event Action<string>? Status;
    public event Action<long>? LatencyMs;

    /// <summary>新识别出的术语（日文=中文）</summary>
    public event Action<List<string>>? TermsFound;

    private readonly List<(string Jp, string Zh)> _history = new();
    private readonly List<(string Jp, string Zh)> _pendingTerms = new();
    private readonly Dictionary<string, (string Zh, int Count)> _termVotes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _termEmitted = new(StringComparer.Ordinal);
    private const int TermsBatch = 8;

    public IReadOnlyList<(string Jp, string Zh)> GetHistory() => _history.ToArray();

    public Pipeline(IntPtr hwnd, IntPtr ownerHwnd, OcrService ocr,
        Func<AppSettings> settings, Dispatcher ui, Dictionary<string, string> cache)
    {
        _hwnd = hwnd;
        _ownerHwnd = ownerHwnd;
        _ocr = ocr;
        _settings = settings;
        _ui = ui;
        _cache = cache;
    }

    public bool IsRunning => _cts != null;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _translateCts?.Cancel();
        _translateCts = null;
    }

    private void Emit(string s) => _ = _ui.BeginInvoke(() => Status?.Invoke(s));

    private async Task LoopAsync(CancellationToken ct)
    {
        var committed = "";
        var pending = "";
        int pendingTicks = 0;
        ulong lastHash = 0;
        Frame? visionCrop = null;
        bool visionPending = false;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var fg = Native.GetForegroundWindow();
                if (fg != _hwnd && fg != _ownerHwnd)
                {
                    Emit("已暂停：游戏不在前台");
                    await Task.Delay(250, ct);
                    continue;
                }

                if (!Native.IsWindow(_hwnd))
                {
                    Emit("目标窗口已关闭，已停止。");
                    return;
                }

                var frame = ScreenCapture.Capture(_hwnd);
                if (frame == null) { await Task.Delay(300, ct); continue; }

                var s = _settings();
                int rx = s.RoiX, ry = s.RoiY, rw = s.RoiW, rh = s.RoiH;
                if (rw <= 8 || rh <= 8)
                {
                    rx = 0; ry = (int)(frame.Height * 0.72);
                    rw = frame.Width; rh = frame.Height - ry;
                }

                var crop = frame.Crop(rx, ry, rw, rh);
                _lastFrame = crop;
                var hash = ScreenCapture.Hash(crop);
                bool changed = hash != lastHash;
                lastHash = hash;

                if (changed)
                {
                    string text;
                    try { text = TextUtil.Normalize(await _ocr.RecognizeAsync(crop)); }
                    catch (Exception ex)
                    {
                        Emit("OCR 失败：" + ex.Message);
                        await Task.Delay(300, ct);
                        continue;
                    }

                    // 又高又窄的框基本就是竖排台词，OCR 对竖排只能读出碎片，
                    // 这种时候直接交给视觉模型，质量差别很明显。
                    bool tallNarrow = rw < 420 && rh > rw * 1.8;

                    bool poor = s.VisionFallback
                                && (TextUtil.IsLowQuality(text)
                                    || TextUtil.IsVerticalLayout(text)
                                    || tallNarrow);

                    if (text.Length == 0)
                    {
                        pending = "";
                        pendingTicks = 0;
                        visionPending = false;
                    }
                    else if (poor)
                    {
                        // 仍然用 OCR 文本当"这屏变了没有"的判据（画面有雨滴动画，
                        // 用像素指纹会导致每帧都重发请求），真正发的是图。
                        if (text != pending)
                        {
                            pending = text;
                            pendingTicks = 0;
                            visionCrop = crop;
                            _ = _ui.BeginInvoke(() =>
                                OcrUpdated?.Invoke(tallNarrow || TextUtil.IsVerticalLayout(text)
                                    ? "（竖排文字，改用图像识别）"
                                    : "（OCR 结果不可靠，改用图像识别）"));
                        }
                        else
                        {
                            visionCrop = crop;
                        }
                    }
                    else if (text != pending)
                    {
                        pending = text;
                        pendingTicks = 0;
                        visionPending = false;
                        _ = _ui.BeginInvoke(() => OcrUpdated?.Invoke(text));
                    }
                    visionPending = poor;
                }

                if (pending.Length > 0 && pending != committed)
                {
                    pendingTicks += changed ? 1 : 2;

                    // 已经以句末标点收尾，就不用再等第二帧，省掉一百多毫秒
                    bool looksComplete = pending.Length > 0 && "。！？!?…」』".IndexOf(pending[^1]) >= 0;
                    int need = looksComplete && !visionPending ? 1 : 2;

                    if (pendingTicks >= need)
                    {
                        committed = pending;
                        if (visionPending && visionCrop != null)
                        {
                            var shot = visionCrop;
                            visionCrop = null;
                            _ = TranslateImageAsync(shot, ct);
                        }
                        else
                        {
                            _ = TranslateAsync(committed, ct);
                        }
                    }
                }

                await Task.Delay(60, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Emit("循环异常：" + ex.Message);
                try { await Task.Delay(500, ct); } catch { return; }
            }
        }
    }

    /// <summary>手动重译当前句：绕过缓存，重新请求一次。</summary>
    public void Retranslate()
    {
        var jp = _lastJp;
        if (string.IsNullOrEmpty(jp))
        {
            Emit("还没有可重译的句子。");
            return;
        }

        lock (_saveLock) _cache.Remove(jp);
        var ct = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(() => TranslateAsync(jp, ct, bypassCache: true));
    }

    /// <summary>手动走图像通道：用于「译文不对」或「本来就是图片、OCR 没出文本」的情况。</summary>
    public void ForceVision()
    {
        var frame = _lastFrame;
        if (frame == null)
        {
            Emit("还没有画面，先点「开始翻译」。");
            return;
        }
        var ct = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(() => TranslateImageAsync(frame, ct, bypassCache: true));
    }

    private async Task TranslateAsync(string jp, CancellationToken ct, bool bypassCache = false)
    {
        _translateCts?.Cancel();
        var tcs = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _translateCts = tcs;
        var token = tcs.Token;
        _lastJp = jp;

        if (!bypassCache && _cache.TryGetValue(jp, out var cached))
        {
            _ = _ui.BeginInvoke(() => TranslationUpdated?.Invoke(cached));
            _context.Add((jp, cached));
            TrimContext();
            return;
        }

        var s = _settings();
        var sb = new StringBuilder();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            sb.Clear();
            try
            {
                await foreach (var delta in new Translator().TranslateAsync(
                    jp, s.Glossary, _context.ToList(), s.BaseUrl, s.ApiKey, s.Model, token))
                {
                    if (token.IsCancellationRequested) return;
                    if (sb.Length == 0) _ = _ui.BeginInvoke(() => LatencyMs?.Invoke(sw.ElapsedMilliseconds));
                    sb.Append(delta);
                    var snapshot = sb.ToString();
                    _ = _ui.BeginInvoke(() => TranslationUpdated?.Invoke(snapshot));
                }

                var zh = sb.ToString().Trim();
                if (zh.Length > 0)
                {
                    lock (_saveLock) _cache[jp] = zh;
                    _context.Add((jp, zh));
                    TrimContext();
                    CommitEntry(jp, zh);
                }
                _ = _ui.BeginInvoke(() => LatencyMs?.Invoke(sw.ElapsedMilliseconds));
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (attempt >= maxAttempts || token.IsCancellationRequested)
                {
                    var msg = ex.Message.Length > 120 ? ex.Message[..120] : ex.Message;
                    _ = _ui.BeginInvoke(() =>
                        TranslationUpdated?.Invoke("（翻译失败：" + msg + "）点「重译当前句」可重试"));
                    return;
                }

                Emit($"请求失败（{attempt}/{maxAttempts}），正在重试…");
                try { await Task.Delay(350 * attempt, token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>视觉兜底：把当前这一帧直接发给模型，看图翻译。</summary>
    private async Task TranslateImageAsync(Frame frame, CancellationToken ct, bool bypassCache = false)
    {
        _translateCts?.Cancel();
        var tcs = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _translateCts = tcs;
        var token = tcs.Token;

        var key = "vlm:" + ScreenCapture.Hash(frame).ToString("x");
        _lastJp = key;

        if (!bypassCache && _cache.TryGetValue(key, out var cached))
        {
            _ = _ui.BeginInvoke(() => TranslationUpdated?.Invoke(cached));
            _context.Add(("（图像）", cached));
            TrimContext();
            return;
        }

        byte[] png;
        try { png = ImageUtil.EncodePng(frame); }
        catch (Exception ex)
        {
            _ = _ui.BeginInvoke(() => TranslationUpdated?.Invoke("（图像编码失败：" + ex.Message + "）"));
            return;
        }

        var s = _settings();
        var sb = new StringBuilder();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            sb.Clear();
            try
            {
                await foreach (var delta in new Translator().TranslateImageAsync(
                    png, s.Glossary, _context.ToList(), s.BaseUrl, s.ApiKey, s.Model, s.VisionDetail, token))
                {
                    if (token.IsCancellationRequested) return;
                    if (sb.Length == 0) _ = _ui.BeginInvoke(() => LatencyMs?.Invoke(sw.ElapsedMilliseconds));
                    sb.Append(delta);
                    var snapshot = sb.ToString();
                    _ = _ui.BeginInvoke(() => TranslationUpdated?.Invoke(snapshot));
                }

                var zh = sb.ToString().Trim();
                if (zh.Length > 0)
                {
                    lock (_saveLock) _cache[key] = zh;
                    _context.Add(("（图像）", zh));
                    TrimContext();
                    CommitEntry("（图像）", zh);
                }
                _ = _ui.BeginInvoke(() => LatencyMs?.Invoke(sw.ElapsedMilliseconds));
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (attempt >= 3 || token.IsCancellationRequested)
                {
                    var msg = ex.Message.Length > 120 ? ex.Message[..120] : ex.Message;
                    _ = _ui.BeginInvoke(() =>
                        TranslationUpdated?.Invoke("（图像翻译失败：" + msg + "）"));
                    return;
                }
                Emit($"图像请求失败（{attempt}/3），正在重试…");
                try { await Task.Delay(350 * attempt, token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>
    /// 模型给的候选术语要过三道筛：长度合理、确实出现在本批原文里、对应的中文也在译文里。
    /// 再要求它在不同批次里出现两次以上，避免 OCR 错字生成一辈子只用一次的"术语"。
    /// </summary>
    private List<string> ValidateTerms(string raw, List<(string Jp, string Zh)> batch)
    {
        var jpAll = string.Join("\n", batch.Select(b => b.Jp));
        var zhAll = string.Join("\n", batch.Select(b => b.Zh));
        var accepted = new List<string>();

        foreach (var line in raw.Split('\n'))
        {
            var text = line.Trim();
            var sep = text.IndexOf('=');
            if (sep <= 0 || sep >= text.Length - 1) continue;

            var jp = text[..sep].Trim().Trim('「', '」', '"', '『', '』');
            var zh = text[(sep + 1)..].Trim().Trim('「', '」', '"', '『', '』');

            if (jp.Length < 2 || jp.Length > 10) continue;
            if (zh.Length < 1 || zh.Length > 12) continue;
            if (!jpAll.Contains(jp, StringComparison.Ordinal)) continue;   // 不是 OCR 里真有的词
            if (!zhAll.Contains(zh, StringComparison.Ordinal)) continue;   // 中文也得对得上

            var count = _termVotes.TryGetValue(jp, out var v) ? v.Count : 0;
            _termVotes[jp] = (zh, count + 1);

            if (count + 1 >= 2 && _termEmitted.Add(jp))
                accepted.Add(jp + "=" + zh);
        }

        return accepted;
    }

    private void CommitEntry(string jp, string zh)
    {
        lock (_history)
        {
            _history.Add((jp, zh));
            if (_history.Count > 80) _history.RemoveAt(0);
        }

        if (!_settings().AutoGlossary || jp == "（图像）") return;

        lock (_history)
        {
            _pendingTerms.Add((jp, zh));
            if (_pendingTerms.Count < TermsBatch) return;
        }

        List<(string Jp, string Zh)> batch;
        lock (_history)
        {
            batch = _pendingTerms.ToList();
            _pendingTerms.Clear();
        }

        _ = Task.Run(() => ExtractTermsAsync(batch));
    }

    private async Task ExtractTermsAsync(List<(string Jp, string Zh)> batch)
    {
        try
        {
            var s = _settings();
            var sb = new StringBuilder();
            foreach (var (jp, zh) in batch)
            {
                sb.AppendLine("日：" + jp.Replace("\n", " "));
                sb.AppendLine("中：" + zh.Replace("\n", " "));
            }

            var result = new StringBuilder();
            await foreach (var d in new Translator().ExtractTermsAsync(
                sb.ToString(), s.BaseUrl, s.ApiKey, s.Model, CancellationToken.None))
            {
                result.Append(d);
            }

            var terms = ValidateTerms(result.ToString(), batch);
            if (terms.Count > 0)
                _ = _ui.BeginInvoke(() => TermsFound?.Invoke(terms));
        }
        catch { /* 术语提取失败不影响主流程 */ }
    }

    private void TrimContext()
    {
        while (_context.Count > 3) _context.RemoveAt(0);
    }
}