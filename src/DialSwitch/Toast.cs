using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DialSwitch;

/// <summary>
/// 切換時顯示的提示卡片：出現在滑鼠所在螢幕的下方中央，淡入、停留、淡出。
/// 不搶焦點、滑鼠可穿透、跟隨系統深淺色，整個程式只建立一個，重複使用。
/// </summary>
sealed class Toast : Form
{
    // 以 96 DPI 為基準的尺寸，實際繪製時乘上螢幕縮放比例。
    const int CardWidth = 320, CardHeight = 76, BottomMargin = 96;
    const int FadeInMs = 140, HoldMs = 1600, FadeOutMs = 260, SlidePx = 10;
    // 不讓不透明度到 1.0：WinForms 在 1.0 時會拿掉分層樣式，視窗會整個消失直到重繪。
    const double FullOpacity = 0.99;

    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    readonly Stopwatch _clock = new();

    string _glyph = "", _title = "", _detail = "";
    bool _error, _skipFadeIn, _dark;
    float _scale = 1;
    Font? _titleFont, _detailFont, _glyphFont;
    int _baseY;

    public Toast()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        AllowTransparency = true;
        Opacity = 0;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        _timer.Tick += (_, _) => Animate();
    }

    /// <param name="icon">圖示字碼，見 <see cref="Icons"/>。</param>
    public void ShowMessage(string icon, string title, string detail, bool error = false)
    {
        _glyph = Icons.Glyph(icon);
        _title = title;
        _detail = detail;
        _error = error;
        _dark = SystemTheme.IsDark;

        var screen = Screen.FromPoint(Cursor.Position);
        SetScale(DpiOf(screen) / 96f);
        var area = screen.WorkingArea;
        var size = new Size(S(CardWidth), S(CardHeight));
        _baseY = area.Bottom - size.Height - S(BottomMargin);

        // 已經在顯示中（例如連轉好幾格）就只更新內容、重新計時，不重播淡入。
        bool alreadyVisible = Visible && Opacity > 0;
        Bounds = new Rectangle(area.Left + (area.Width - size.Width) / 2, alreadyVisible ? _baseY : _baseY + S(SlidePx), size.Width, size.Height);
        ApplyTheme();
        Invalidate();

        if (!Visible) Show();
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        _skipFadeIn = alreadyVisible;
        _clock.Restart();
        _timer.Start();
    }

    void Animate()
    {
        double t = _clock.Elapsed.TotalMilliseconds + (_skipFadeIn ? FadeInMs : 0);
        if (t < FadeInMs)
        {
            double p = EaseOut(t / FadeInMs);
            Opacity = p * FullOpacity;
            Top = _baseY + (int)Math.Round(S(SlidePx) * (1 - p));
        }
        else if (t < FadeInMs + HoldMs)
        {
            Opacity = FullOpacity;
            Top = _baseY;
        }
        else if (t < FadeInMs + HoldMs + FadeOutMs)
        {
            Opacity = FullOpacity * (1 - (t - FadeInMs - HoldMs) / FadeOutMs);
        }
        else
        {
            _timer.Stop();
            _clock.Reset();
            Opacity = 0;
            Hide();
        }
    }

    static double EaseOut(double x) => 1 - Math.Pow(1 - x, 3);

    // ---- 繪製 ----

    Color Background => _dark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(250, 250, 250);
    Color Foreground => _dark ? Color.White : Color.FromArgb(26, 26, 26);
    Color Secondary => _dark ? Color.FromArgb(190, 190, 190) : Color.FromArgb(96, 96, 96);
    Color Border => _dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(222, 222, 222);

    void ApplyTheme()
    {
        BackColor = Background;
        int border = ColorTranslator.ToWin32(Border);
        DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    protected override void OnPaint(PaintEventArgs e) => PaintCard(e.Graphics, ClientSize);

    void PaintCard(Graphics g, Size size)
    {
        int Width = size.Width, Height = size.Height;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Background);

        // 左側圓形徽章：藍紫漸層（錯誤時紅色）+ 白色圖示
        float d = S(46), x = S(16), y = (Height - d) / 2;
        var badge = new RectangleF(x, y, d, d);
        using (Brush brush = _error
            ? new SolidBrush(Color.FromArgb(232, 72, 85))
            : new LinearGradientBrush(badge, Color.FromArgb(56, 132, 255), Color.FromArgb(124, 77, 255), 45f))
            g.FillEllipse(brush, badge);
        TextRenderer.DrawText(g, _glyph, _glyphFont, Rectangle.Round(badge), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        // 右側文字
        int textLeft = (int)(x + d + S(14));
        int textWidth = Width - textLeft - S(16);
        var flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        int titleH = _titleFont!.Height, detailH = _detailFont!.Height, gap = S(2);
        int top = (Height - titleH - gap - detailH) / 2;
        TextRenderer.DrawText(g, _title, _titleFont, new Rectangle(textLeft, top, textWidth, titleH), Foreground, Background, flags);
        TextRenderer.DrawText(g, _detail, _detailFont, new Rectangle(textLeft, top + titleH + gap, textWidth, detailH), Secondary, Background, flags);
    }

    /// <summary>把提示卡片畫成圓角圖片（文件截圖用），不顯示視窗。</summary>
    public static Bitmap RenderPreview(string icon, string title, string detail, bool error, bool dark, float scale)
    {
        using var toast = new Toast { _glyph = Icons.Glyph(icon), _title = title, _detail = detail, _error = error, _dark = dark };
        toast.SetScale(scale);
        var size = new Size(toast.S(CardWidth), toast.S(CardHeight));

        // GDI 文字畫在透明底上會破圖，先畫在不透明底上，再用圓角路徑貼到透明圖片。
        using var opaque = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(opaque)) toast.PaintCard(g, size);

        var result = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        using (var path = RoundRect(new RectangleF(0.5f, 0.5f, size.Width - 1, size.Height - 1), toast.S(8)))
        using (var brush = new TextureBrush(opaque))
        using (var pen = new Pen(toast.Border))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        return result;
    }

    static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    void SetScale(float scale)
    {
        if (_titleFont is not null && Math.Abs(scale - _scale) < 0.01f) return;
        _scale = scale;
        _titleFont?.Dispose();
        _detailFont?.Dispose();
        _glyphFont?.Dispose();
        _titleFont = new Font("Microsoft JhengHei UI", 17.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _detailFont = new Font("Microsoft JhengHei UI", 12.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _glyphFont = new Font(Icons.WinFormsFontFamily, 20f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    int S(int value) => (int)Math.Round(value * _scale);

    // ---- 視窗行為 ----

    // 顯示時不要把焦點從使用者正在用的程式搶走。
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;
            const int CS_DROPSHADOW = 0x20000;
            var cp = base.CreateParams;
            // TRANSPARENT：滑鼠點擊直接穿透到底下的視窗，提示不會擋到操作。
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    // 尺寸由 ShowMessage 依目標螢幕 DPI 自己算，不讓 WinForms 再縮放一次。
    protected override void OnDpiChanged(DpiChangedEventArgs e) => e.Cancel = true;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _titleFont?.Dispose();
            _detailFont?.Dispose();
            _glyphFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    static uint DpiOf(Screen screen)
    {
        var center = new Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2);
        var monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi : 96;
    }

    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34, DWMWCP_ROUND = 2;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, MONITOR_DEFAULTTONEAREST = 2;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}

/// <summary>讀取 Windows「應用程式模式」深淺色設定。</summary>
static class SystemTheme
{
    public static bool IsDark
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
    }
}
