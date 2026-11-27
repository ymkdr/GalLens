using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GalLens;

/// <summary>最近的字幕历史，只存在内存里，退出即清。</summary>
public sealed class HistoryWindow : Window
{
    public HistoryWindow(IReadOnlyList<(string Jp, string Zh)> entries)
    {
        Title = "字幕历史";
        Width = 620;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Bg"];
        FontFamily = new FontFamily("Microsoft YaHei UI");
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var list = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xE8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3D)),
            FontSize = 13,
            Margin = new Thickness(16, 16, 16, 0),
            Padding = new Thickness(6)
        };

        foreach (var (jp, zh) in entries)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
            panel.Children.Add(new TextBlock
            {
                Text = jp.Replace("\n", " "),
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x99)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = zh.Replace("\n", " "),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xF0)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            list.Items.Add(panel);
        }

        if (entries.Count == 0)
            list.Items.Add(new TextBlock
            {
                Text = "还没有记录。翻译几句之后再看。",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x99))
            });

        var close = new Button
        {
            Content = "关闭",
            Width = 100,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 16, 14)
        };
        close.Click += (_, _) => Close();

        var dock = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        dock.Children.Add(close);
        dock.Children.Add(list);
        Content = dock;

        SourceInitialized += (_, _) =>
            Native.UseDarkTitleBar(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }
}
