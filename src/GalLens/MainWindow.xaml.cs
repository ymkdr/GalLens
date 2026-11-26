using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GalLens;

public partial class MainWindow : Window
{
    internal sealed class WindowItem
    {
        public IntPtr Hwnd { get; init; }
        public string Title { get; init; } = "";
        public string Process { get; init; } = "";
        public int Area { get; init; }
        public string Label => Process + "  ·  " + Title;
    }

    private readonly AppSettings _settings = AppStorage.LoadSettings();
    private readonly Dictionary<string, string> _cache = AppStorage.LoadCache();

    private OcrService? _ocr;
    private Pipeline? _pipeline;
    private OverlayWindow? _overlay;
    private IntPtr _gameHwnd;
    private IntPtr _myHwnd;
    private HwndSource? _source;
    private DispatcherTimer? _followTimer;
    private DispatcherTimer? _saveTimer;
    private TrayIcon? _tray;
    private bool _loading = true;
    private bool _running;
    private bool _reallyExit;
    private bool _balloonShown;
    private bool _overlayVisible = true;
    private bool _glossaryExpanded;
    private bool _glossaryEditing;
    private string _gameProcess = "";

    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyToggle = 0x0B01;
    private const int HotkeyRegion = 0x0B02;
    private const int HotkeyMove = 0x0B03;
    private const int HotkeyRetranslate = 0x0B04;
    private const int HotkeyVision = 0x0B05;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _myHwnd = new WindowInteropHelper(this).Handle;
        Native.UseDarkTitleBar(_myHwnd);
        _source = HwndSource.FromHwnd(_myHwnd);
        _source?.AddHook(WndProc);

        RegisterHotKey(_myHwnd, HotkeyToggle, MOD_ALT, 0x54);                    // Alt+T
        RegisterHotKey(_myHwnd, HotkeyRegion, MOD_ALT, 0x52);                    // Alt+R
        RegisterHotKey(_myHwnd, HotkeyMove, MOD_ALT, 0x4D);                      // Alt+M
        RegisterHotKey(_myHwnd, HotkeyRetranslate, MOD_CONTROL | MOD_ALT, 0x52); // Ctrl+Alt+R
        RegisterHotKey(_myHwnd, HotkeyVision, MOD_CONTROL | MOD_ALT, 0x56);      // Ctrl+Alt+V

        _tray = new TrayIcon(_myHwnd, Environment.ProcessPath ?? "", "GalLens — 屏幕实时汉化")
        {
            OnShow = ShowFromTray,
            OnToggle = () => StartStop_Click(this, new RoutedEventArgs()),
            OnRetranslate = () => Retranslate_Click(this, new RoutedEventArgs()),
            OnVision = () => Vision_Click(this, new RoutedEventArgs()),
            OnExit = ExitApp,
            ToggleState = () => (_running ? "停止翻译" : "开始翻译",
                                 !_running && _gameHwnd != IntPtr.Zero && _settings.HasRegion)
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_tray?.HandleMessage(msg, wParam, lParam) == true)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyToggle) { StartStop_Click(this, new RoutedEventArgs()); handled = true; }
            else if (id == HotkeyRegion) { PickRegion_Click(this, new RoutedEventArgs()); handled = true; }
            else if (id == HotkeyMove) { ToggleMove(); handled = true; }
            else if (id == HotkeyRetranslate) { Retranslate_Click(this, new RoutedEventArgs()); handled = true; }
            else if (id == HotkeyVision) { Vision_Click(this, new RoutedEventArgs()); handled = true; }
        }
        return IntPtr.Zero;
    }

    // ---------------- 初始化 ----------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;
        Width = Math.Min(Width, wa.Width - 24);
        Height = Math.Min(Height, wa.Height - 24);

        var fonts = SystemFonts.GetCjkFonts();
        FontCombo.ItemsSource = fonts;
        var current = fonts.FirstOrDefault(f =>
                          f.Equals(_settings.Style.FontFamily, StringComparison.OrdinalIgnoreCase))
                      ?? fonts.FirstOrDefault() ?? "Microsoft YaHei UI";
        FontCombo.SelectedItem = current;
        _settings.Style.FontFamily = current;

        if (_settings.Model is "deepseek-chat" or "deepseek-reasoner" or "")
            _settings.Model = "deepseek-flash";
        ModelBox.Text = _settings.Model;

        BaseUrlBox.Text = _settings.BaseUrl;
        ApiKeyBox.Password = _settings.ApiKey;
        FontSlider.Value = _settings.Style.FontSize;
        OpacitySlider.Value = _settings.Style.BackgroundOpacity;
        OutlineCheck.IsChecked = _settings.Style.Outline;
        VisionCheck.IsChecked = _settings.VisionFallback;
        FontSizeText.Text = ((int)_settings.Style.FontSize).ToString();
        OpacityText.Text = _settings.Style.BackgroundOpacity.ToString("0.00");
        UpdateSwatchSelection();

        _loading = false;

        _ocr = OcrService.TryCreate("ja", "ja-JP");
        if (_ocr == null)
        {
            StartButton.IsEnabled = false;
            SetStatus("未找到日语 OCR 语言包，请先在系统设置里添加日语");
        }
        else
        {
            SetStatus("就绪");
        }

        RefreshWindowList();
        UpdateRegionText();
        RefreshGlossaryUi();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyExit)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        StopRun();
        UnregisterHotKey(_myHwnd, HotkeyToggle);
        UnregisterHotKey(_myHwnd, HotkeyRegion);
        UnregisterHotKey(_myHwnd, HotkeyMove);
        UnregisterHotKey(_myHwnd, HotkeyRetranslate);
        UnregisterHotKey(_myHwnd, HotkeyVision);
        _tray?.Dispose();
        AppStorage.SaveSettings(_settings);
        AppStorage.SaveCache(_cache);
    }

    private void HideToTray()
    {
        Hide();
        if (_balloonShown) return;
        _balloonShown = true;
        _tray?.ShowBalloon(
            "GalLens 仍在后台运行",
            _running ? "翻译继续进行。单击托盘图标可以重新打开窗口。"
                     : "单击托盘图标可以重新打开窗口，右键菜单可以退出。");
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void ExitApp()
    {
        _reallyExit = true;
        Close();
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        new HelpWindow { Owner = this }.ShowDialog();
    }

    /// <summary>状态栏只显示运行状态 + 当前模型，不塞窗口标题。</summary>
    private void SetStatus(string state)
    {
        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "未设置模型" : _settings.Model;
        StatusText.Text = state + "　·　" + model;
    }

    // ---------------- 窗口列表 ----------------

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshWindowList();

    private void RefreshWindowList()
    {
        var list = new List<WindowItem>();
        Native.EnumWindowsProc cb = (h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.IsCloaked(h)) return true;
            var title = Native.GetTitle(h);
            if (title.Length == 0) return true;
            if (!Native.GetWindowRect(h, out var r)) return true;
            if (r.Width < 400 || r.Height < 300) return true;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)Environment.ProcessId) return true;
            string proc;
            try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { return true; }
            list.Add(new WindowItem { Hwnd = h, Title = title, Process = proc, Area = r.Width * r.Height });
            return true;
        };
        Native.EnumWindows(cb, IntPtr.Zero);

        var ordered = list.OrderBy(w => w.Process).ThenBy(w => w.Title).ToList();
        WindowCombo.ItemsSource = ordered;

        WindowItem? match = null;
        if (!string.IsNullOrEmpty(_settings.TargetProcess))
        {
            var sameProcess = ordered.Where(w => w.Process == _settings.TargetProcess).ToList();
            match = sameProcess.FirstOrDefault(w => w.Title == _settings.TargetTitle)
                    ?? sameProcess.OrderByDescending(w => w.Area).FirstOrDefault();
        }

        if (match != null)
        {
            WindowCombo.SelectedItem = match;
        }
        else
        {
            WindowCombo.SelectedIndex = -1;
            _gameHwnd = IntPtr.Zero;
            WindowText.Text = "窗口：未选择";
            SetStatus(ordered.Count == 0
                ? "没找到可翻译的窗口，先把游戏切成窗口化再刷新"
                : "请选择一个游戏窗口");
        }
    }

    private void WindowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (WindowCombo.SelectedItem is not WindowItem w) return;

        _gameHwnd = w.Hwnd;
        _gameProcess = w.Process;
        _settings.TargetProcess = w.Process;
        _settings.TargetTitle = w.Title;

        if (_settings.Regions.TryGetValue(w.Process, out var saved))
        {
            _settings.RoiX = saved.X;
            _settings.RoiY = saved.Y;
            _settings.RoiW = saved.W;
            _settings.RoiH = saved.H;
        }
        else
        {
            _settings.RoiX = -1;
            _settings.RoiY = -1;
            _settings.RoiW = 0;
            _settings.RoiH = 0;
        }

        _settings.Glossary = _settings.Glossaries.TryGetValue(w.Process, out var g) ? g : "";
        GlossaryBox.Text = _settings.Glossary;
        RefreshGlossaryUi();

        ScheduleSave();
        WindowText.Text = "窗口：" + w.Label;
        UpdateRegionText();
        SetStatus(_settings.HasRegion ? "已选择窗口" : "已选择窗口，请框选台词区域");
    }

    // ---------------- 区域 ----------------

    private void PickRegion_Click(object sender, RoutedEventArgs e)
    {
        if (_gameHwnd == IntPtr.Zero || !Native.IsWindow(_gameHwnd))
        {
            SetStatus("请先选择一个游戏窗口");
            return;
        }

        bool wasRunning = _running;
        if (wasRunning) StopRun();

        var frame = ScreenCapture.Capture(_gameHwnd);
        if (frame == null)
        {
            SetStatus("截取窗口失败，试试无边框窗口模式");
            return;
        }

        var picker = new RegionPicker(frame) { Owner = this };
        picker.ShowDialog();
        if (picker.Result is Int32Rect r)
        {
            _settings.RoiX = r.X;
            _settings.RoiY = r.Y;
            _settings.RoiW = r.Width;
            _settings.RoiH = r.Height;

            if (!string.IsNullOrEmpty(_gameProcess))
                _settings.Regions[_gameProcess] = new RegionRect
                {
                    X = r.X, Y = r.Y, W = r.Width, H = r.Height
                };

            ScheduleSave();
            UpdateRegionText();
            SetStatus("台词区域已保存");
        }
        else
        {
            SetStatus("已取消框选");
        }

        if (wasRunning) StartRun();
    }

    private void UpdateRegionText()
    {
        RegionText.Text = _settings.HasRegion
            ? $"区域：{_settings.RoiW} × {_settings.RoiH} @ ({_settings.RoiX}, {_settings.RoiY})"
            : "区域：未设置";
    }

    // ---------------- 样式 ----------------

    private void FontCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (FontCombo.SelectedItem is not string name) return;
        _settings.Style.FontFamily = name;
        ApplyStyle();
        ScheduleSave();
    }

    private void FontSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FontSizeText == null) return;
        FontSizeText.Text = ((int)e.NewValue).ToString();
        _settings.Style.FontSize = e.NewValue;
        ApplyStyle();
        if (!_loading) ScheduleSave();
    }

    private void OpacitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityText == null) return;
        OpacityText.Text = e.NewValue.ToString("0.00");
        _settings.Style.BackgroundOpacity = e.NewValue;
        ApplyStyle();
        if (!_loading) ScheduleSave();
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string hex) return;
        _settings.Style.TextColor = hex;
        UpdateSwatchSelection();
        ApplyStyle();
        ScheduleSave();
    }

    private void UpdateSwatchSelection()
    {
        var buttons = new[] { Sw0, Sw1, Sw2, Sw3, Sw4 };
        foreach (var b in buttons)
        {
            bool on = string.Equals(b.Tag as string, _settings.Style.TextColor, StringComparison.OrdinalIgnoreCase);
            b.BorderBrush = on ? new SolidColorBrush(Color.FromRgb(0x7C, 0x9C, 0xFF)) : Brushes.Transparent;
            b.BorderThickness = new Thickness(on ? 2 : 0);
        }
    }

    private void Vision_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.VisionFallback = VisionCheck.IsChecked == true;
        ScheduleSave();
    }

    private void Outline_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Style.Outline = OutlineCheck.IsChecked == true;
        ApplyStyle();
        ScheduleSave();
    }

    private void ApplyStyle()
    {
        _overlay?.ApplyStyle(_settings.Style);
    }

    // ---------------- 字幕移动 ----------------

    private void MoveOverlay_Click(object sender, RoutedEventArgs e) => ToggleMove();

    private void ToggleMove()
    {
        if (_overlay == null)
        {
            SetStatus("先点「开始翻译」，再调整字幕位置");
            return;
        }

        _overlay.Interactive = !_overlay.Interactive;
        MoveButton.Content = _overlay.Interactive ? "完成移动" : "移动字幕位置";

        if (_overlay.Interactive)
        {
            StatusText.Text = "拖动模式：把字幕拖到想要的位置，再点「完成移动」";
        }
        else
        {
            SaveOverlayOffset();
            SetStatus("字幕位置已保存");
        }
    }

    private void Overlay_DragFinished(object? sender, EventArgs e)
    {
        SaveOverlayOffset();
        MoveButton.Content = "移动字幕位置";
        SetStatus("字幕位置已保存");
    }

    private void SaveOverlayOffset()
    {
        if (_overlay == null || _gameHwnd == IntPtr.Zero) return;
        var rect = _overlay.GetScreenRect();
        var p = new Native.POINT { X = _settings.RoiX, Y = _settings.RoiY };
        Native.ClientToScreen(_gameHwnd, ref p);
        _settings.Style.OffsetX = rect.X - p.X;
        _settings.Style.OffsetY = rect.Y - p.Y;
        ScheduleSave();
        PositionOverlay();
    }

    // ---------------- 术语表 ----------------

    private void GlossaryToggle_Click(object sender, RoutedEventArgs e)
    {
        _glossaryExpanded = !_glossaryExpanded;
        if (!_glossaryExpanded) _glossaryEditing = false;
        RefreshGlossaryUi();
    }

    private void GlossaryEdit_Click(object sender, RoutedEventArgs e)
    {
        _glossaryEditing = !_glossaryEditing;
        if (_glossaryEditing) _glossaryExpanded = true;
        RefreshGlossaryUi();
        if (_glossaryEditing) GlossaryBox.Focus();
    }

    private static List<(string Jp, string Zh)> ParseGlossary(string? text)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var sep = line.IndexOf('=');
            if (sep < 0) sep = line.IndexOf('＝');
            if (sep <= 0 || sep >= line.Length - 1) continue;
            var jp = line[..sep].Trim();
            var zh = line[(sep + 1)..].Trim();
            if (jp.Length > 0 && zh.Length > 0) list.Add((jp, zh));
        }
        return list;
    }

    private void RefreshGlossaryUi()
    {
        var entries = ParseGlossary(_settings.Glossary);
        GlossarySummary.Text = entries.Count + " 条";
        GlossaryList.ItemsSource = entries.Select(x => x.Jp + "   →   " + x.Zh).ToList();

        GlossaryToggleButton.Content = _glossaryExpanded ? "收起" : "展开";
        GlossaryEditButton.Visibility = _glossaryExpanded ? Visibility.Visible : Visibility.Collapsed;
        GlossaryEditButton.Content = _glossaryEditing ? "完成" : "编辑";

        GlossaryBox.Visibility = _glossaryEditing ? Visibility.Visible : Visibility.Collapsed;
        GlossaryList.Visibility = _glossaryExpanded && !_glossaryEditing
            ? Visibility.Visible : Visibility.Collapsed;
        GlossaryEmptyHint.Visibility = _glossaryExpanded && !_glossaryEditing && entries.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearGlossary_Click(object sender, RoutedEventArgs e)
    {
        GlossaryBox.Text = "";
        _settings.Glossary = "";
        if (!string.IsNullOrEmpty(_gameProcess))
            _settings.Glossaries[_gameProcess] = "";
        RefreshGlossaryUi();
        SetStatus("已清空这个游戏的术语表");
        ScheduleSave();
    }

    // ---------------- 配置 ----------------

    private void Config_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _settings.BaseUrl = BaseUrlBox.Text.Trim();
        if (ReferenceEquals(sender, ModelBox)) _settings.Model = ModelBox.Text.Trim();

        if (ReferenceEquals(sender, GlossaryBox))
        {
            _settings.Glossary = GlossaryBox.Text;
            if (!string.IsNullOrEmpty(_gameProcess))
                _settings.Glossaries[_gameProcess] = _settings.Glossary;
            RefreshGlossaryUi();
        }

        if (!_running) SetStatus(_ocr == null ? "缺少 OCR 语言包" : "就绪");
        ScheduleSave();
    }

    private void ModelPreset_Click(object sender, RoutedEventArgs e)
    {
        var presets = new (string Id, string Note)[]
        {
            ("deepseek-flash",    "DeepSeek · 带视觉"),
            ("deepseek-v4-pro",   "DeepSeek · 更强"),
            ("qwen-plus",         "通义千问"),
            ("glm-4-flash",       "智谱"),
            ("moonshot-v1-8k",    "Kimi"),
            ("gpt-4o-mini",       "OpenAI")
        };

        var menu = new ContextMenu();
        foreach (var (id, note) in presets)
        {
            var item = new MenuItem { Header = id + "    " + note };
            item.Click += (_, _) =>
            {
                ModelBox.Text = id;
                ModelBox.CaretIndex = id.Length;
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = ModelPresetButton;
        menu.IsOpen = true;
    }

    private void ApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ApiKey = ApiKeyBox.Password;
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick -= SaveTick;
        _saveTimer.Tick += SaveTick;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTick(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        AppStorage.SaveSettings(_settings);
    }

    // ---------------- 运行 ----------------

    private void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_running) StopRun();
        else StartRun();
    }

    private void Retranslate_Click(object sender, RoutedEventArgs e)
    {
        if (_pipeline == null)
        {
            SetStatus("先点「开始翻译」");
            return;
        }
        _pipeline.Retranslate();
        SetStatus("正在重译当前句");
    }

    private void Vision_Click(object sender, RoutedEventArgs e)
    {
        if (_pipeline == null)
        {
            SetStatus("先点「开始翻译」");
            return;
        }
        _pipeline.ForceVision();
        SetStatus("用图像翻译当前画面");
    }

    private void StartRun()
    {
        if (_ocr == null) { SetStatus("缺少日语 OCR 语言包"); return; }
        if (_gameHwnd == IntPtr.Zero || !Native.IsWindow(_gameHwnd))
        {
            SetStatus("请先选择一个有效的游戏窗口");
            return;
        }
        if (!_settings.HasRegion)
        {
            SetStatus("还没框选台词区域");
            return;
        }
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            SetStatus("请先填写 API Key");
            return;
        }

        _overlay = new OverlayWindow();
        _overlay.ApplyStyle(_settings.Style);
        _overlay.DragFinished += Overlay_DragFinished;
        _overlay.SetText("……");
        _overlay.Show();
        PositionOverlay();

        _pipeline = new Pipeline(_gameHwnd, _myHwnd, _ocr, () => _settings, Dispatcher, _cache);
        _pipeline.Status += s => StatusText.Text = s;
        _pipeline.OcrUpdated += s => SourceText.Text = TextUtil.ForDisplay(s);
        _pipeline.TranslationUpdated += s =>
        {
            TargetText.Text = s;
            _overlay?.SetText(s);
        };
        _pipeline.LatencyMs += ms => LatencyText.Text = ms + " ms";
        _pipeline.Start();

        _overlayVisible = true;
        MoveButton.Content = "移动字幕位置";

        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _followTimer.Tick += (_, _) =>
        {
            if (_overlay == null) return;
            MoveButton.Content = _overlay.Interactive ? "完成移动" : "移动字幕位置";
            if (_overlay.Interactive) return;
            SyncOverlayVisibility();
            PositionOverlay();
        };
        _followTimer.Start();

        _running = true;
        StartButton.Content = "停止";
        RetranslateButton.IsEnabled = true;
        VisionButton.IsEnabled = true;
        SetStatus("运行中");
    }

    private void StopRun()
    {
        _followTimer?.Stop();
        _followTimer = null;

        _pipeline?.Stop();
        _pipeline = null;

        if (_overlay != null)
        {
            _overlay.DragFinished -= Overlay_DragFinished;
            _overlay.Close();
            _overlay = null;
        }

        MoveButton.Content = "移动字幕位置";
        RetranslateButton.IsEnabled = false;
        VisionButton.IsEnabled = false;

        if (_running)
        {
            _running = false;
            StartButton.Content = "开始翻译";
            SetStatus("已停止");
        }
        AppStorage.SaveCache(_cache);
    }

    /// <summary>字幕只在游戏或本程序窗口处于前台时出现。</summary>
    private void SyncOverlayVisibility()
    {
        if (_overlay == null) return;

        var fg = Native.GetForegroundWindow();
        bool active = fg == _gameHwnd || fg == _myHwnd;
        if (active == _overlayVisible) return;

        _overlayVisible = active;
        if (active)
        {
            _overlay.Show();
            PositionOverlay();
        }
        else
        {
            _overlay.Hide();
        }
    }

    private void PositionOverlay()
    {
        if (_overlay == null || _gameHwnd == IntPtr.Zero) return;
        var p = new Native.POINT { X = _settings.RoiX, Y = _settings.RoiY };
        Native.ClientToScreen(_gameHwnd, ref p);

        // 框选区域可能又窄又高（竖排台词），字幕框固定一个最小宽度，
        // 并以框的右边缘为锚点向左扩展，否则中文会一个字一行。
        int width = Math.Max(_settings.RoiW, 560);
        int height = Math.Max(_settings.RoiH, 120);
        int x = p.X + _settings.Style.OffsetX - (width - _settings.RoiW);
        if (x < 0) x = 0;

        _overlay.Place(x, p.Y + _settings.Style.OffsetY, width, height);
    }
}
