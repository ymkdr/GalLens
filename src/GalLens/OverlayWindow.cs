using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GalLens;

public sealed class OverlayWindow : Window
{
    private readonly StackPanel _actions;
    private readonly Button _historyBtn;
    private readonly Button _retranslateBtn;
    private readonly Button _visionBtn;
    private readonly Border _border;
    private readonly Grid _grid;
    private readonly TextBlock _main;
    private readonly List<TextBlock> _outlines = new();
    private readonly bool _excludeFromCapture;
    private readonly DispatcherTimer _hoverTimer;

    private bool _interactive;
    private bool _manual;          // 由按钮锁定的拖动模式
    private int _x = int.MinValue, _y = int.MinValue, _w, _h;

    private const int VK_MENU = 0x12;

    /// <summary>松开 Alt、拖动结束时触发，让主窗口保存新位置。</summary>
    public event EventHandler? DragFinished;
    public event EventHandler? HistoryRequested;
    public event EventHandler? RetranslateRequested;
    public event EventHandler? VisionRequested;
    public event EventHandler? Moved;

    public OverlayWindow(bool excludeFromCapture = true)
    {
        _excludeFromCapture = excludeFromCapture;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = false;
        IsHitTestVisible = false;

        _grid = new Grid();

        // 四向偏移的暗色副本做出描边（WPF 的 TextBlock 没有原生 stroke）
        double[,] offsets = { { -1.2, 0 }, { 1.2, 0 }, { 0, -1.2 }, { 0, 1.2 } };
        for (int i = 0; i < offsets.GetLength(0); i++)
        {
            var tb = new TextBlock
            {
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 28,
                FontWeight = FontWeights.Medium,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
                RenderTransform = new TranslateTransform(offsets[i, 0], offsets[i, 1])
            };
            _outlines.Add(tb);
            _grid.Children.Add(tb);
        }

        _main = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 28,
            FontWeight = FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };
        _grid.Children.Add(_main);

        Button MakeAction(string text)
        {
            var b = new Button
            {
                Content = text,
                FontSize = 11,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromArgb(140, 20, 20, 28)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xE8)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            return b;
        }

        _historyBtn = MakeAction("历史");
        _retranslateBtn = MakeAction("重译");
        _visionBtn = MakeAction("图像");

        _historyBtn.Click += (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty);
        _retranslateBtn.Click += (_, _) => RetranslateRequested?.Invoke(this, EventArgs.Empty);
        _visionBtn.Click += (_, _) => VisionRequested?.Invoke(this, EventArgs.Empty);

        _actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 6),
            Visibility = Visibility.Collapsed
        };
        _actions.Children.Add(_historyBtn);
        _actions.Children.Add(_retranslateBtn);
        _actions.Children.Add(_visionBtn);

        var stack = new StackPanel();
        stack.Children.Add(_actions);
        stack.Children.Add(_grid);

        _border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(168, 8, 8, 12)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 8),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = stack
        };

        Content = _border;

        MouseLeftButtonDown += (_, e) =>
        {
            if (!_interactive) return;
            if (e.OriginalSource is DependencyObject d && IsInside(d)) return;   // 点的是按钮
            DragMove();
        };
        LocationChanged += (_, _) => { if (_interactive) Moved?.Invoke(this, EventArgs.Empty); };

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        _hoverTimer.Tick += (_, _) => HoverTick();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.MakeClickThrough(hwnd);
        if (!_excludeFromCapture)
        {
            try { Native.SetWindowDisplayAffinity(hwnd, 0); } catch { }
        }
        _hoverTimer.Start();
    }

    private bool IsInside(DependencyObject? node)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, _actions)) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>鼠标停在字幕上按住 Alt 时，临时变可拖动；松开自动恢复穿透。</summary>
    private void HoverTick()
    {
        if (_manual || !IsVisible) return;

        bool alt = (Native.GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        bool inside = false;

        if (alt && Native.GetCursorPos(out var p))
        {
            var r = GetScreenRect();
            inside = p.X >= r.X && p.X < r.X + r.W && p.Y >= r.Y && p.Y < r.Y + r.H;
        }

        bool want = alt && inside;
        if (want != _interactive) SetInteractive(want);
    }

    /// <summary>按钮模式：锁定拖动，直到再次点按钮。</summary>
    public bool Interactive
    {
        get => _interactive;
        set
        {
            _manual = value;
            SetInteractive(value);
        }
    }

    private void SetInteractive(bool value)
    {
        _interactive = value;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        IsHitTestVisible = value;

        if (value)
        {
            ex &= ~(Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE);
            _border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x9C, 0xFF));
            _border.BorderThickness = new Thickness(2);
            _actions.Visibility = Visibility.Visible;
            Cursor = Cursors.SizeAll;
            Activate();
        }
        else
        {
            ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;
            _border.BorderBrush = null;
            _border.BorderThickness = new Thickness(0);
            _actions.Visibility = Visibility.Collapsed;
            Cursor = Cursors.Arrow;
            DragFinished?.Invoke(this, EventArgs.Empty);
        }
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);
    }

    public void SetText(string text)
    {
        _main.Text = text;
        foreach (var tb in _outlines) tb.Text = text;
    }

    public void ApplyStyle(SubtitleStyle s)
    {
        var family = new FontFamily(string.IsNullOrWhiteSpace(s.FontFamily) ? "Microsoft YaHei UI" : s.FontFamily);
        var fg = Parse(s.TextColor, Colors.White);
        var outlineColor = Parse(s.OutlineColor, Colors.Black);

        _main.FontFamily = family;
        _main.FontSize = s.FontSize;
        _main.LineHeight = s.FontSize * 1.42;
        _main.Foreground = new SolidColorBrush(fg);

        foreach (var tb in _outlines)
        {
            tb.FontFamily = family;
            tb.FontSize = s.FontSize;
            tb.LineHeight = s.FontSize * 1.42;
            tb.Foreground = new SolidColorBrush(outlineColor);
            tb.Visibility = s.Outline ? Visibility.Visible : Visibility.Collapsed;
        }

        byte alpha = (byte)Math.Clamp(s.BackgroundOpacity * 255, 0, 255);
        _border.Background = new SolidColorBrush(Color.FromArgb(alpha, 8, 8, 12));
        _border.CornerRadius = new CornerRadius(s.CornerRadius);
    }

    private static Color Parse(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color c)
                return c;
        }
        catch { }
        return fallback;
    }

    public void Place(int x, int y, int w, int h)
    {
        w = Math.Max(60, w);
        h = Math.Max(40, h);

        // 位置和尺寸都没变就什么都不做：每 400ms 重复 SetWindowPos 会让
        // 分层窗口整块重绘，看起来就是闪一下。
        if (x == _x && y == _y && w == _w && h == _h) return;

        _x = x; _y = y; _w = w; _h = h;

        // 显式设定 WPF 尺寸，否则窗口会随文字内容撑开
        var dpi = VisualTreeHelper.GetDpi(this);
        double dw = _w / dpi.DpiScaleX;
        double dh = _h / dpi.DpiScaleY;
        if (Math.Abs(Width - dw) > 0.5) Width = dw;
        if (Math.Abs(Height - dh) > 0.5) Height = dh;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _x, _y, _w, _h, Native.SWP_NOACTIVATE);
    }

    public (int X, int Y, int W, int H) GetScreenRect()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && Native.GetWindowRect(hwnd, out var r))
            return (r.Left, r.Top, r.Width, r.Height);
        return (_x, _y, _w, _h);
    }

    // 尺寸由 Place() 统一管理，这里不再额外 SetWindowPos，
    // 否则一次 Place 会触发两次窗口移动，加重闪烁。
}