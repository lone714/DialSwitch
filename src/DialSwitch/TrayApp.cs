using System.Drawing;
using System.Windows.Forms;

namespace DialSwitch;

/// <summary>系統匣圖示與右鍵選單。選單每次打開時重建，反映最新的模式與狀態。</summary>
sealed class TrayIcon : IDisposable
{
    readonly Icon _icon;
    readonly ContextMenuStrip _menu = new();
    readonly NotifyIcon _tray;
    readonly App _app;

    public TrayIcon(App app)
    {
        _app = app;
        using (var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("app.ico")!)
            _icon = new Icon(stream, SystemInformation.SmallIconSize);

        _menu.Opening += (_, _) => Populate(_menu, _app);
        // NotifyIcon 會自己處理 Explorer 重新啟動（TaskbarCreated），圖示不會消失。
        _tray = new NotifyIcon { Icon = _icon, Visible = true, ContextMenuStrip = _menu };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _app.ShowSettings(); };
    }

    public void SetTooltip(string text) =>
        _tray.Text = text.Length > 127 ? text[..127] : text; // NotifyIcon.Text 上限 127 字元

    /// <summary>依目前狀態重建選單內容。</summary>
    internal static void Populate(ContextMenuStrip menu, App app)
    {
        // 舊的選單項目要 Dispose，否則每開一次選單就多留一批物件。
        var old = menu.Items.Cast<ToolStripItem>().ToList();
        menu.Items.Clear();
        old.ForEach(item => item.Dispose());
        MenuTheme.Apply(menu, SystemTheme.IsDark);

        var current = app.CurrentMode();
        var dial = app.Function == DialFunction.Volume ? "音量調整" : "音訊切換";
        menu.Items.Add(new ToolStripMenuItem($"旋鈕功能：{dial}（按一下旋鈕切換）") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        foreach (var mode in app.Settings.Modes)
        {
            var item = new ToolStripMenuItem(mode.Name) { Checked = mode == current };
            item.Click += (_, _) => app.ApplyMode(mode);
            menu.Items.Add(item);
        }
        if (app.Settings.Modes.Count == 0)
            menu.Items.Add(new ToolStripMenuItem("（尚未建立模式）") { Enabled = false });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("設定…", null, (_, _) => app.ShowSettings());
        menu.Items.Add("開啟紀錄檔", null, (_, _) => Log.Open());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束", null, (_, _) => app.Shutdown());
    }

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
        foreach (ToolStripItem item in _menu.Items) item.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}

/// <summary>系統匣選單跟隨系統深淺色（WinForms 預設只有淺色）。</summary>
static class MenuTheme
{
    static readonly Font MenuFont = new("Microsoft JhengHei UI", 10f);

    public static void Apply(ContextMenuStrip menu, bool dark)
    {
        menu.Font = MenuFont;
        menu.ShowImageMargin = true;
        if (menu.Renderer is not Renderer r || r.Dark != dark)
            menu.Renderer = new Renderer(dark);
        menu.BackColor = dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249);
        menu.ForeColor = dark ? Color.White : Color.FromArgb(26, 26, 26);
    }

    sealed class Renderer(bool dark) : ToolStripProfessionalRenderer(new Colors(dark))
    {
        public bool Dark { get; } = dark;

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled
                ? (Dark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(120, 120, 120))
                : (Dark ? Color.White : Color.FromArgb(26, 26, 26));
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // 勾選記號：強調色的小圓點，深淺色都清楚
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int d = e.ImageRectangle.Height / 2;
            var dot = new Rectangle(e.ImageRectangle.Left + (e.ImageRectangle.Width - d) / 2, e.ImageRectangle.Top + (e.ImageRectangle.Height - d) / 2, d, d);
            using var brush = new SolidBrush(Color.FromArgb(76, 194, 255));
            g.FillEllipse(brush, dot);
        }
    }

    sealed class Colors(bool dark) : ProfessionalColorTable
    {
        Color Back => dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249);
        Color Hover => dark ? Color.FromArgb(61, 61, 61) : Color.FromArgb(232, 232, 232);
        Color Line => dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(222, 222, 222);

        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Back;
        public override Color CheckBackground => Back;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}
