using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GalLens;

public sealed class OverlayWindow : Window
{
    private readonly Border _border;
    private readonly Grid _grid;
    private readonly TextBlock _main;
    private readonly List<TextBlock> _outlines = new();
    private readonly bool _excludeFromCapture;
    private readonly DispatcherTimer _hoverTimer;

    private bool _interactive;
    private bool _manual;          // 由按钮锁定的拖动模式
    private int _x, _y, _w, _h;

    private const int VK_MENU = 0x12;

    /// <summary>松开 Alt、拖动结束时触发，让主窗口保存新位置。</summary>
    public event EventHandler? DragFinished;
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

        _border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(168, 8, 8, 12)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 8),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = _grid
        };

        Content = _border;

        MouseLeftButtonDown += (_, _) => { if (_interactive) DragMove(); };
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
        if (value)
        {
            ex &= ~(Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE);
            _border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x9C, 0xFF));
            _border.BorderThickness = new Thickness(2);
            Cursor = Cursors.SizeAll;
            Activate();
        }
        else
        {
            ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;
            _border.BorderBrush = null;
            _border.BorderThickness = new Thickness(0);
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
        _x = x; _y = y; _w = Math.Max(60, w); _h = Math.Max(40, h);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _x, _y, _w, _h,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    public (int X, int Y, int W, int H) GetScreenRect()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && Native.GetWindowRect(hwnd, out var r))
            return (r.Left, r.Top, r.Width, r.Height);
        return (_x, _y, _w, _h);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && _w > 0)
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _x, _y, _w, _h,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }
}
