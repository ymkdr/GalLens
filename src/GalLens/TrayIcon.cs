using System.Runtime.InteropServices;

namespace GalLens;

/// <summary>系统托盘图标：左键单击/双击切主窗口，右键弹菜单。纯 Win32，不依赖 WinForms。</summary>
public sealed class TrayIcon : IDisposable
{
    public const int MessageId = 0x8001;

    private const uint NIM_ADD = 0x0;
    private const uint NIM_MODIFY = 0x1;
    private const uint NIM_DELETE = 0x2;

    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_INFO = 0x10;

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const int ID_SHOW = 1;
    private const int ID_TOGGLE = 2;
    private const int ID_EXIT = 3;
    private const int ID_RETRANSLATE = 4;
    private const int ID_VISION = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int index, IntPtr[] large, IntPtr[] small, uint count);

    [DllImport("user32.dll")] private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr id, string text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

    private readonly IntPtr _hwnd;
    private IntPtr _icon;
    private bool _added;

    public Action? OnShow { get; init; }
    public Action? OnToggle { get; init; }
    public Action? OnExit { get; init; }
    public Action? OnRetranslate { get; init; }
    public Action? OnVision { get; init; }

    /// <summary>菜单里「开始/停止」那一项当前该显示什么。</summary>
    public Func<(string Text, bool Enabled)>? ToggleState { get; init; }

    public TrayIcon(IntPtr hwnd, string exePath, string tooltip)
    {
        _hwnd = hwnd;
        _icon = LoadAppIcon(exePath);

        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = MessageId;
        data.hIcon = _icon;
        data.szTip = tooltip.Length > 120 ? tooltip[..120] : tooltip;
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
    }

    private NOTIFYICONDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        szTip = "",
        szInfo = "",
        szInfoTitle = ""
    };

    private static IntPtr LoadAppIcon(string exePath)
    {
        try
        {
            var small = new IntPtr[1];
            var large = new IntPtr[1];
            if (ExtractIconExW(exePath, 0, large, small, 1) > 0 && small[0] != IntPtr.Zero)
            {
                if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
                return small[0];
            }
        }
        catch { }
        return LoadIconW(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
    }

    /// <summary>处理托盘窗口消息；返回 true 表示已消费。</summary>
    public bool HandleMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != MessageId) return false;

        int mouse = lParam.ToInt32() & 0xFFFF;
        switch (mouse)
        {
            case WM_LBUTTONUP:
            case WM_LBUTTONDBLCLK:
                OnShow?.Invoke();
                return true;
            case WM_RBUTTONUP:
                ShowMenu();
                return true;
        }
        return true;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            var (toggleText, toggleEnabled) = ToggleState?.Invoke() ?? ("开始翻译", false);

            AppendMenuW(menu, MF_STRING, ID_SHOW, "显示主窗口");
            AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, "");
            if (toggleEnabled) AppendMenuW(menu, MF_STRING, ID_TOGGLE, toggleText);
            if (toggleEnabled)
            {
                AppendMenuW(menu, MF_STRING, ID_RETRANSLATE, "重译当前句");
                AppendMenuW(menu, MF_STRING, ID_VISION, "用图像翻译当前画面");
            }
            AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, "");
            AppendMenuW(menu, MF_STRING, ID_EXIT, "退出 GalLens");

            GetCursorPos(out var p);

            // 经典 workaround：不先抢前台，菜单点外面不消失
            SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, p.X, p.Y, 0, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, 0x0000, IntPtr.Zero, IntPtr.Zero);

            switch (cmd)
            {
                case ID_SHOW: OnShow?.Invoke(); break;
                case ID_TOGGLE: OnToggle?.Invoke(); break;
                case ID_RETRANSLATE: OnRetranslate?.Invoke(); break;
                case ID_VISION: OnVision?.Invoke(); break;
                case ID_EXIT: OnExit?.Invoke(); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void ShowBalloon(string title, string text)
    {
        var data = NewData();
        data.uFlags = NIF_INFO;
        data.szInfoTitle = title.Length > 60 ? title[..60] : title;
        data.szInfo = text.Length > 250 ? text[..250] : text;
        data.uTimeoutOrVersion = 4000;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void UpdateTooltip(string text)
    {
        var data = NewData();
        data.uFlags = NIF_TIP;
        data.szTip = text.Length > 120 ? text[..120] : text;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData();
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}
