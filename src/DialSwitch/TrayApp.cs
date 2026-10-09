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

        _menu.Opening += (_, _) => RebuildMenu();
        // NotifyIcon 會自己處理 Explorer 重新啟動（TaskbarCreated），圖示不會消失。
        _tray = new NotifyIcon { Icon = _icon, Visible = true, ContextMenuStrip = _menu };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _app.ShowSettings(); };
    }

    public void SetTooltip(string text) =>
        _tray.Text = text.Length > 127 ? text[..127] : text; // NotifyIcon.Text 上限 127 字元

    void RebuildMenu()
    {
        // 舊的選單項目要 Dispose，否則每開一次選單就多留一批物件。
        var old = _menu.Items.Cast<ToolStripItem>().ToList();
        _menu.Items.Clear();
        old.ForEach(item => item.Dispose());

        var current = _app.CurrentMode();
        var dial = _app.Function == DialFunction.Volume ? "音量調整" : "音訊切換";
        _menu.Items.Add(new ToolStripMenuItem($"旋鈕功能：{dial}（按一下旋鈕切換）") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());

        foreach (var mode in _app.Settings.Modes)
        {
            var item = new ToolStripMenuItem(mode.Name) { Checked = mode == current };
            item.Click += (_, _) => _app.ApplyMode(mode);
            _menu.Items.Add(item);
        }
        if (_app.Settings.Modes.Count == 0)
            _menu.Items.Add(new ToolStripMenuItem("（尚未建立模式）") { Enabled = false });

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("設定…", null, (_, _) => _app.ShowSettings());
        _menu.Items.Add("開啟紀錄檔", null, (_, _) => Log.Open());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("結束", null, (_, _) => _app.Shutdown());
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
