using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Interop;

namespace DialSwitch;

/// <summary>
/// 產生 README 用的截圖：DialSwitch.exe --doc-shots [資料夾]
/// 使用示範模式（耳機 / 螢幕喇叭 / 直播），裝置取自本機實際裝置。
/// 不註冊快速鍵、不建立系統匣圖示、不切換裝置、不寫入任何設定；設定視窗與選單會短暫出現在畫面上。
/// </summary>
static class DocShots
{
    public static AppSettings DemoSettings()
    {
        var outputs = AudioDevices.List(DataFlow.Output);
        var inputs = AudioDevices.List(DataFlow.Input);
        static DeviceRef? Pick(List<AudioDevice> list, string nameContains)
        {
            var d = list.FirstOrDefault(x => x.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
            return d is null ? null : new DeviceRef(d.Id, d.Name);
        }

        return new AppSettings
        {
            Modes =
            [
                new ModeConfig
                {
                    Name = "耳機", Icon = Icons.Headphones, IncludeCommunications = true,
                    Output = Pick(outputs, "HyperX"), Input = Pick(inputs, "HyperX"),
                    Hotkey = new Hotkey(Hotkey.MOD_CONTROL | Hotkey.MOD_ALT, 0x31), // Ctrl+Alt+1
                },
                new ModeConfig
                {
                    Name = "螢幕喇叭", Icon = Icons.Monitor, IncludeCommunications = false,
                    Output = Pick(outputs, "BenQ"),
                },
                new ModeConfig
                {
                    Name = "直播", Icon = "E93E", IncludeCommunications = true,
                    Output = Pick(outputs, "HyperX"), Input = Pick(inputs, "Fifine"),
                    Hotkey = new Hotkey(Hotkey.MOD_CONTROL | Hotkey.MOD_ALT, 0x33), // Ctrl+Alt+3
                },
            ],
        };
    }

    public static async Task Run(App app, string folder)
    {
        Directory.CreateDirectory(folder);
        var modes = app.Settings.Modes;
        bool dark = SystemTheme.IsDark;
        const float scale = 1.5f;

        void SaveToast(string file, string icon, string title, string detail, bool error = false)
        {
            using var bmp = Toast.RenderPreview(icon, title, detail, error, dark, scale);
            bmp.Save(Path.Combine(folder, file));
        }
        SaveToast("toast-mode.png", modes[0].Icon, modes[0].Name, Describe(modes[0]));
        SaveToast("toast-mode-monitor.png", modes[1].Icon, modes[1].Name, Describe(modes[1]));
        SaveToast("toast-dial-volume.png", Icons.Volume, "旋鈕：音量調整", "旋轉調整音量，再按一下改為切換音訊模式");
        SaveToast("toast-dial-switch.png", modes[0].Icon, "旋鈕：音訊切換", $"目前：{modes[0].Name}，旋轉切換模式");
        SaveToast("toast-error.png", Icons.Warning, $"{modes[1].Name}：找不到裝置", $"{modes[1].Output?.Name} 未連接或已停用", error: true);

        // 設定視窗三個分頁
        var window = new SettingsWindow(app, app.Settings.Clone());
        window.Show();
        await Task.Delay(1200);
        string[] tabFiles = ["settings-modes.png", "settings-dial.png", "settings-general.png"];
        for (int i = 0; i < tabFiles.Length; i++)
        {
            window.Tabs.SelectedIndex = i;
            await Task.Delay(600);
            Capture(new WindowInteropHelper(window).Handle, Path.Combine(folder, tabFiles[i]));

            // 旋鈕分頁下方的「設定 Windows 滾輪」區塊另外捲到底拍一張
            if (i == 1 && window.Tabs.SelectedContent is System.Windows.Controls.ScrollViewer scroller)
            {
                scroller.ScrollToEnd();
                await Task.Delay(400);
                Capture(new WindowInteropHelper(window).Handle, Path.Combine(folder, "settings-wheel.png"));
            }
        }
        window.Close();

        // 系統匣右鍵選單
        using var menu = new ContextMenuStrip();
        TrayIcon.Populate(menu, app);
        var area = Screen.PrimaryScreen!.WorkingArea;
        menu.Show(new Point(area.Left + area.Width / 2, area.Top + area.Height / 3));
        await Task.Delay(500);
        Capture(menu.Handle, Path.Combine(folder, "tray-menu.png"));
        menu.Close();

        Log.Write("文件截圖已輸出到 " + Path.GetFullPath(folder));
    }

    static string Describe(ModeConfig mode)
    {
        var parts = new List<string>();
        if (mode.Output is not null) parts.Add("輸出 " + mode.Output.Name);
        if (mode.Input is not null) parts.Add("輸入 " + mode.Input.Name);
        return string.Join("　", parts);
    }

    /// <summary>用 PrintWindow 擷取單一視窗（含 DWM 背景），裁掉 Windows 11 外圍看不見的縮放邊框。</summary>
    static void Capture(IntPtr hwnd, string path)
    {
        GetWindowRect(hwnd, out var outer);
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var visible, Marshal.SizeOf<RECT>()) != 0) visible = outer;

        using var full = new Bitmap(outer.Right - outer.Left, outer.Bottom - outer.Top);
        using (var g = Graphics.FromImage(full))
        {
            var hdc = g.GetHdc();
            PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
        }
        var crop = new Rectangle(visible.Left - outer.Left, visible.Top - outer.Top, visible.Right - visible.Left, visible.Bottom - visible.Top);
        using var cropped = full.Clone(crop, full.PixelFormat);
        cropped.Save(path);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    const uint PW_RENDERFULLCONTENT = 2;
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
}
