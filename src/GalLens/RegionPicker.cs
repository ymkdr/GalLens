using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GalLens;

/// <summary>全屏截图 + 拖拽框选，返回以截图物理像素为单位的矩形。</summary>
public sealed class RegionPicker : Window
{
    private readonly Canvas _canvas;
    private readonly Image _image;
    private readonly System.Windows.Shapes.Rectangle _sel;
    private readonly TextBlock _hint;
    private readonly int _imgW, _imgH;
    private double _scale = 1, _ox, _oy;
    private Point _start;
    private bool _dragging;

    public Int32Rect? Result { get; private set; }

    public RegionPicker(Frame frame)
    {
        _imgW = frame.Width;
        _imgH = frame.Height;

        WindowStyle = WindowStyle.None;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;

        var bmp = BitmapSource.Create(_imgW, _imgH, 96, 96, PixelFormats.Bgra32, null,
            frame.Bgra, _imgW * 4);

        _image = new Image { Source = bmp, Stretch = Stretch.Fill };
        _sel = new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 190, 255)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(40, 0, 170, 255)),
            Visibility = Visibility.Collapsed
        };
        _hint = new TextBlock
        {
            Text = "拖动框选台词文字区域　·　Esc 取消",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 15,
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };

        _canvas = new Canvas { Background = Brushes.Black };
        _canvas.Children.Add(_image);
        _canvas.Children.Add(_sel);
        _canvas.Children.Add(_hint);
        Content = _canvas;

        Loaded += (_, _) => LayoutImage();
        SizeChanged += (_, _) => LayoutImage();

        _canvas.MouseLeftButtonDown += OnDown;
        _canvas.MouseMove += OnMove;
        _canvas.MouseLeftButtonUp += OnUp;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Result = null; Close(); }
        };
    }

    private void LayoutImage()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        _scale = Math.Min(w / _imgW, h / _imgH);
        _ox = (w - _imgW * _scale) / 2;
        _oy = (h - _imgH * _scale) / 2;
        _image.Width = _imgW * _scale;
        _image.Height = _imgH * _scale;
        Canvas.SetLeft(_image, _ox);
        Canvas.SetTop(_image, _oy);
        Canvas.SetLeft(_hint, 24);
        Canvas.SetTop(_hint, 24);
    }

    private Point ToImage(Point p) =>
        new(Math.Clamp((p.X - _ox) / _scale, 0, _imgW - 1),
            Math.Clamp((p.Y - _oy) / _scale, 0, _imgH - 1));

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _start = ToImage(e.GetPosition(_canvas));
        _sel.Visibility = Visibility.Visible;
        _sel.Width = 0; _sel.Height = 0;
        Canvas.SetLeft(_sel, _ox + _start.X * _scale);
        Canvas.SetTop(_sel, _oy + _start.Y * _scale);
        _canvas.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var cur = ToImage(e.GetPosition(_canvas));
        double x = Math.Min(cur.X, _start.X), y = Math.Min(cur.Y, _start.Y);
        double w = Math.Abs(cur.X - _start.X), h = Math.Abs(cur.Y - _start.Y);
        Canvas.SetLeft(_sel, _ox + x * _scale);
        Canvas.SetTop(_sel, _oy + y * _scale);
        _sel.Width = w * _scale;
        _sel.Height = h * _scale;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _canvas.ReleaseMouseCapture();
        var cur = ToImage(e.GetPosition(_canvas));
        int x = (int)Math.Min(cur.X, _start.X);
        int y = (int)Math.Min(cur.Y, _start.Y);
        int w = (int)Math.Abs(cur.X - _start.X);
        int h = (int)Math.Abs(cur.Y - _start.Y);
        if (w < 16 || h < 16) { _sel.Visibility = Visibility.Collapsed; return; }
        Result = new Int32Rect(x, y, w, h);
        Close();
    }
}
