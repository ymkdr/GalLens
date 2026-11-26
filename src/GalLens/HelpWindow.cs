using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GalLens;

public sealed class HelpWindow : Window
{
    private const string Body = @"【三步开始】

① 选择游戏窗口
   下拉框里选你的游戏。列表没有就点「刷新列表」。
   游戏必须先切成「窗口」或「无边框窗口」，独占全屏截不到图。

② 框选台词区域
   点「框选台词区域」，在截图预览上拖一个框，只框文字。
   每个游戏会分别记住自己的框，下次自动恢复。

③ 填写翻译服务并开始
   接口地址、API Key、模型，填一次就记住。
   点「开始翻译」，切回游戏即可看到字幕。

【翻译中怎么操作】

· 按住 Alt 在字幕上拖动 —— 直接挪字幕位置，松手即保存
· 「重译」按钮 / Ctrl+Alt+R —— 文字没认错、但译文不满意时重来
· 「图像翻译」按钮 / Ctrl+Alt+V —— 让模型直接看图重翻这一屏
· 「移动字幕位置」按钮 / Alt+M —— 锁定拖动模式，适合精确微调
· Alt+T —— 开始 / 停止
· Alt+R —— 重新框选台词区域
· 点窗口的 X —— 收进托盘，翻译不中断；右键托盘图标可退出

【字幕外观】

字体、字号、颜色、描边、背景不透明度，改完立即生效。
描边在画面比较花的时候更好读；背景不透明度拉到 1 就是完全盖住原文。

【图像识别（OCR 认不准时）】

两条路：

· 自动 —— 当 OCR 认出来的东西明显是乱码（几乎没有假名/汉字，或者全是符号），
  程序自动把那一小块画面发给模型看图翻译。可在「翻译服务」里关掉。
· 手动 —— 按「图像翻译」或 Ctrl+Alt+V，直接把当前这一屏交给模型。
  适用于两种情况：文字认对了但译文不对；以及画面上根本不是可识别的文本
  （竖排、艺术字、标题、本身就是一张图）。

需要所选模型支持图像输入；换纯文本模型请关掉自动那条。

【术语表】

一行一个「日文=中文」，例如：

    遠野=远野
    ノエル=诺艾尔

它随每次请求一起发给模型，用来固定人名、地名、设定词的译法。
按游戏分别保存，关掉程序也不会丢；「清空」只清当前这个游戏。

【常见问题】

· 字幕不出现：游戏不在前台；或还没框选区域；或没填 API Key
· 全黑：游戏是独占全屏，改成无边框
· 识别乱：重新框选，框得紧一点，只框文字
· 报 401：Key 错了；报 402/429：余额不足或请求过频
";
    public HelpWindow()
    {
        Title = "GalLens 帮助";
        Width = 560;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Bg"];
        FontFamily = new FontFamily("Microsoft YaHei UI");
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var text = new TextBlock
        {
            Text = Body,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xEC)),
            FontSize = 13.5,
            LineHeight = 23,
            Margin = new Thickness(20)
        };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = text
        };

        var close = new Button
        {
            Content = "知道了",
            Width = 110,
            Height = 34,
            Margin = new Thickness(16, 0, 0, 0)
        };
        close.Click += (_, _) => Close();

        var authorLink = new TextBlock
        {
            Text = "作者 ymkdr　·　github.com/ymkdr/GalLens",
            Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x9C, 0xFF)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        authorLink.MouseLeftButtonUp += (_, _) => MainWindow.OpenUrl("https://github.com/ymkdr/GalLens");

        var footer = new DockPanel { Margin = new Thickness(20, 8, 20, 14) };
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(authorLink);

        var dock = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(scroll);
        Content = dock;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Native.UseDarkTitleBar(hwnd);
        };
    }
}
