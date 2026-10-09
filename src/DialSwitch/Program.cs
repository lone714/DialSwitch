// 旋鈕切換
//
// 搭配 Windows「滾輪」的自訂工具：滾輪把旋轉、按下轉成快速鍵，這個常駐程式接收快速鍵後：
//   按一下旋鈕 → 切換「旋鈕功能」：音量調整 ⇄ 音訊切換
//   旋轉旋鈕   → 音量調整時：模擬音量鍵（Windows 照常調音量、顯示音量提示）
//                音訊切換時：在使用者自訂的「音訊模式」之間前後切換（每個模式有自己的輸出 / 輸入裝置）
// 每個音訊模式也可以另外綁定專屬快速鍵，直接跳到該模式。
using System.Windows;

namespace DialSwitch;

static class Program
{
    // 安裝程式（Inno Setup 的 AppMutex）也用這個名稱判斷程式是否在執行。
    public const string MutexName = "DialSwitch.SingleInstance";
    const string ShowSettingsEventName = "DialSwitch.ShowSettings";

    [STAThread]
    static void Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "--selftest":
                SelfTest.Run(args.Length > 1 && int.TryParse(args[1], out int n) && n > 0 ? n : 1000);
                return;
            case "--setup-wheel":
                // 安裝程式與懶人腳本用：依目前設定（沒有設定檔時用預設值）自動設定 Windows 滾輪
                var s = SettingsStore.Load(out _);
                if (s.RotateLeft is { } left && s.RotateRight is { } right && s.Press is { } press)
                    WheelSetup.Apply(left, right, press);
                return;
            case "--restore-wheel":
                WheelSetup.Restore();
                return;
        }

        using var mutex = new Mutex(true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            // 已經在執行：請正在執行的那一個打開設定視窗（例如從開始功能表再點一次）。
            try { using var signal = EventWaitHandle.OpenExisting(ShowSettingsEventName); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("程式即將結束，未處理的例外: " + e.ExceptionObject);
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        var app = new App(showSettings);
        app.Run();
    }
}

sealed class App : Application
{
    const int HotkeyRight = 1, HotkeyLeft = 2, HotkeyPress = 3, HotkeyModeBase = 100;

    readonly EventWaitHandle _showSettingsSignal;
    readonly ModeSwitcher _switcher = new();
    RegisteredWaitHandle? _signalWait;
    HotkeyManager? _hotkeys;
    TrayIcon? _tray;
    Toast? _toast;
    SettingsWindow? _settingsWindow;

    public AppSettings Settings { get; private set; } = new();
    public DialFunction Function { get; private set; }

    public App(EventWaitHandle showSettingsSignal)
    {
        _showSettingsSignal = showSettingsSignal;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ThemeMode = ThemeMode.System; // WPF Fluent 主題，跟隨系統深淺色
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("未處理的例外: " + args.Exception);
            args.Handled = true; // 記錄下來但不讓整個程式結束
        };

        Settings = SettingsStore.Load(out bool isNew);
        if (isNew) TrySave();
        Function = Settings.StartupFunction;

        _toast = new Toast();
        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += OnHotkey;
        _tray = new TrayIcon(this);
        RegisterHotkeys();
        UpdateTooltip();

        _signalWait = ThreadPool.RegisterWaitForSingleObject(_showSettingsSignal,
            (_, _) => Dispatcher.BeginInvoke(ShowSettings), null, Timeout.Infinite, executeOnlyOnce: false);

        Log.Write($"啟動 v{typeof(App).Assembly.GetName().Version}，{Settings.Modes.Count} 個模式");
        if (isNew) ShowSettings(); // 第一次執行直接打開設定，引導使用者設定
        else Notify(Icons.Check, "旋鈕切換已啟動", $"旋鈕功能：{FunctionName}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _signalWait?.Unregister(null);
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _toast?.Dispose();
        Log.Write("結束");
        base.OnExit(e);
    }

    // ---- 快速鍵 ----

    void RegisterHotkeys()
    {
        var map = new Dictionary<int, Hotkey>();
        var names = new Dictionary<int, string>();
        void Add(int id, Hotkey? key, string name)
        {
            if (key is null) return;
            map[id] = key;
            names[id] = $"{name}（{key}）";
        }
        Add(HotkeyRight, Settings.RotateRight, "向右轉");
        Add(HotkeyLeft, Settings.RotateLeft, "向左轉");
        Add(HotkeyPress, Settings.Press, "按一下");
        for (int i = 0; i < Settings.Modes.Count; i++)
            Add(HotkeyModeBase + i, Settings.Modes[i].Hotkey, Settings.Modes[i].Name);

        var failed = _hotkeys!.SetAll(map);
        foreach (var id in failed) Log.Write($"快速鍵註冊失敗（可能被其他程式佔用）: {names[id]}");
        if (failed.Count > 0)
            Notify(Icons.Warning, "部分快速鍵無法使用", string.Join("、", failed.Select(id => names[id])) + " 已被其他程式佔用", error: true);
    }

    public void SuspendHotkeys() => _hotkeys?.Suspend();
    public void ResumeHotkeys() => _hotkeys?.Resume();

    void OnHotkey(int id)
    {
        switch (id)
        {
            case HotkeyPress:
                ToggleFunction();
                break;
            case HotkeyRight or HotkeyLeft:
                int direction = id == HotkeyRight ? 1 : -1;
                if (Function == DialFunction.Volume) VolumeKeys.Step(direction);
                else Step(direction);
                break;
            case >= HotkeyModeBase when id - HotkeyModeBase < Settings.Modes.Count:
                ApplyMode(Settings.Modes[id - HotkeyModeBase]);
                break;
        }
    }

    // ---- 旋鈕功能 ----

    string FunctionName => Function == DialFunction.Volume ? "音量調整" : "音訊切換";

    void ToggleFunction()
    {
        Function = Function == DialFunction.Volume ? DialFunction.Switch : DialFunction.Volume;
        UpdateTooltip();
        if (Function == DialFunction.Volume)
            Notify(Icons.Volume, "旋鈕：音量調整", "旋轉調整音量，再按一下改為切換音訊模式");
        else
            Notify(CurrentMode()?.Icon ?? Icons.Headphones, "旋鈕：音訊切換", $"目前：{CurrentMode()?.Name ?? "（不符合任何模式）"}，旋轉切換模式");
    }

    // ---- 音訊模式 ----

    public ModeConfig? CurrentMode()
    {
        try { return _switcher.Current(Settings.Modes); }
        catch (Exception ex) { Log.Write("判斷目前模式失敗: " + ex.Message); return null; }
    }

    void Step(int direction)
    {
        if (Settings.Modes.Count == 0)
        {
            Notify(Icons.Warning, "尚未建立音訊模式", "在系統匣圖示上按右鍵 → 設定", error: true);
            return;
        }
        try
        {
            var target = _switcher.Step(Settings.Modes, direction, Settings.Wrap);
            if (target is not null) ApplyMode(target);
        }
        catch (Exception ex)
        {
            Log.Write("切換失敗: " + ex);
            Notify(Icons.Warning, "切換失敗", ex.Message, error: true);
        }
    }

    public void ApplyMode(ModeConfig mode)
    {
        try
        {
            var missing = _switcher.Apply(mode);
            UpdateTooltip();
            if (missing.Count > 0)
            {
                Log.Write($"[{mode.Name}] 找不到裝置: {string.Join("、", missing)}");
                Notify(Icons.Warning, $"{mode.Name}：找不到裝置", string.Join("、", missing) + " 未連接或已停用", error: true);
            }
            else
            {
                Notify(mode.Icon, mode.Name, Describe(mode));
            }
        }
        catch (Exception ex)
        {
            Log.Write("切換失敗: " + ex);
            Notify(Icons.Warning, "切換失敗", ex.Message, error: true);
        }
    }

    static string Describe(ModeConfig mode)
    {
        var parts = new List<string>();
        if (mode.Output is not null) parts.Add("輸出 " + mode.Output.Name);
        if (mode.Input is not null) parts.Add("輸入 " + mode.Input.Name);
        return string.Join("　", parts);
    }

    // ---- 設定 ----

    public void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this, Settings.Clone());
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            ResumeHotkeys(); // 保險：錄製快速鍵時直接關掉視窗也會恢復
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        TrySave();
        RegisterHotkeys();
        UpdateTooltip();
        Log.Write($"設定已儲存，{Settings.Modes.Count} 個模式");
    }

    void TrySave()
    {
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("設定儲存失敗: " + ex.Message);
            Notify(Icons.Warning, "設定儲存失敗", ex.Message, error: true);
        }
    }

    // ---- 提示 ----

    void Notify(string icon, string title, string detail, bool error = false)
    {
        if (Settings.ShowToast || error) _toast?.ShowMessage(icon, title, detail, error);
    }

    void UpdateTooltip() =>
        _tray?.SetTooltip($"旋鈕切換\n旋鈕：{FunctionName}\n模式：{CurrentMode()?.Name ?? "—"}");
}
