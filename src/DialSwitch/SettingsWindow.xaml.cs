using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace DialSwitch;

public partial class SettingsWindow : Window
{
    public static IReadOnlyList<IconChoice> IconChoices { get; } =
        Icons.Choices.Select(c => new IconChoice(c.Code, Icons.Glyph(c.Code), c.Label)).ToList();

    readonly App _app;
    readonly SettingsViewModel _vm;

    internal SettingsWindow(App app, AppSettings settings)
    {
        _app = app;
        _vm = new SettingsViewModel(settings);
        DataContext = _vm;
        InitializeComponent();

        using var stream = typeof(SettingsWindow).Assembly.GetManifestResourceStream("app.ico")!;
        Icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    void OnAddMode(object sender, RoutedEventArgs e)
    {
        var mode = new ModeViewModel(new ModeConfig { Name = $"模式 {_vm.Modes.Count + 1}" }, _vm);
        _vm.Modes.Add(mode);
        _vm.SelectedMode = mode;
    }

    void OnRemoveMode(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedMode is not { } mode) return;
        int index = _vm.Modes.IndexOf(mode);
        _vm.Modes.Remove(mode);
        _vm.SelectedMode = _vm.Modes.Count == 0 ? null : _vm.Modes[Math.Min(index, _vm.Modes.Count - 1)];
    }

    void OnMoveUp(object sender, RoutedEventArgs e) => Move(-1);
    void OnMoveDown(object sender, RoutedEventArgs e) => Move(1);

    void Move(int offset)
    {
        if (_vm.SelectedMode is not { } mode) return;
        int index = _vm.Modes.IndexOf(mode), target = index + offset;
        if (target < 0 || target >= _vm.Modes.Count) return;
        _vm.Modes.Move(index, target);
        _vm.SelectedMode = mode;
    }

    void OnClearModeHotkey(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedMode is { } mode) mode.Hotkey = null;
    }

    void OnApplyNow(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedMode is { } mode) _app.ApplyMode(mode.ToConfig());
    }

    void OnRefreshDevices(object sender, RoutedEventArgs e) => _vm.RefreshDevices();

    void OnOpenWheelSettings(object sender, RoutedEventArgs e)
    {
        if (!Shell.Open("ms-settings:wheel")) Shell.Open("ms-settings:bluetooth");
    }

    void OnSetupWheel(object sender, RoutedEventArgs e)
    {
        if (_vm.RotateLeft is not { } left || _vm.RotateRight is not { } right || _vm.Press is not { } press)
        {
            MessageBox.Show(this, "請先設定向右轉、向左轉、按一下三組快速鍵。", "無法自動設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            WheelSetup.Apply(left, right, press);
            _vm.WheelStatus = "已寫入。轉動旋鈕試試看；若沒有反應，請登出再登入 Windows。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Write("自動設定滾輪失敗: " + ex.Message);
            _vm.WheelStatus = "寫入失敗：" + ex.Message + "，請改用手動設定。";
        }
        _vm.RefreshWheelBackup();
    }

    void OnRestoreWheel(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.WheelStatus = WheelSetup.Restore() ? "已還原成自動設定前的滾輪設定。" : "沒有可還原的備份。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Write("還原滾輪設定失敗: " + ex.Message);
            _vm.WheelStatus = "還原失敗：" + ex.Message;
        }
        _vm.RefreshWheelBackup();
    }

    void OnOpenLog(object sender, RoutedEventArgs e) => Log.Open();

    void OnOpenSettingsFolder(object sender, RoutedEventArgs e) =>
        Shell.Open(Path.GetDirectoryName(SettingsStore.FilePath)!);

    void OnSave(object sender, RoutedEventArgs e)
    {
        if (_vm.Validate() is { } problem)
        {
            MessageBox.Show(this, problem, "無法儲存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _app.SaveSettings(_vm.ToSettings());
        AutoStart.Enabled = _vm.AutoStart;
        Close();
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close();
}

public sealed record IconChoice(string Code, string Glyph, string Label);

/// <summary>下拉選單的一個選項。Ref 為 null 代表「不變更」。</summary>
public sealed record DeviceOption(string Display, DeviceRef? Ref);

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class SettingsViewModel : ObservableObject
{
    static readonly DeviceOption NoChange = new("（不變更）", null);

    readonly AppSettings _source;
    ModeViewModel? _selected;
    Hotkey? _right, _left, _press;

    public ObservableCollection<ModeViewModel> Modes { get; }
    public ObservableCollection<DeviceOption> OutputOptions { get; } = [];
    public ObservableCollection<DeviceOption> InputOptions { get; } = [];

    internal SettingsViewModel(AppSettings settings)
    {
        _source = settings;
        _right = settings.RotateRight;
        _left = settings.RotateLeft;
        _press = settings.Press;
        Wrap = settings.Wrap;
        ShowToast = settings.ShowToast;
        StartupFunctionIndex = settings.StartupFunction == DialFunction.Switch ? 1 : 0;
        AutoStart = AutoStart_Enabled();

        RefreshOptions(settings.Modes.Select(m => m.Output), settings.Modes.Select(m => m.Input));
        Modes = new(settings.Modes.Select(m => new ModeViewModel(m, this)));
        SelectedMode = Modes.FirstOrDefault();
    }

    static bool AutoStart_Enabled() => DialSwitch.AutoStart.Enabled;

    public ModeViewModel? SelectedMode
    {
        get => _selected;
        set { if (Set(ref _selected, value)) { Raise(nameof(HasSelection)); Raise(nameof(NoSelection)); } }
    }

    public bool HasSelection => _selected is not null;
    public bool NoSelection => _selected is null;

    public Hotkey? RotateRight { get => _right; set { if (Set(ref _right, value)) Raise(nameof(WheelSummary)); } }
    public Hotkey? RotateLeft { get => _left; set { if (Set(ref _left, value)) Raise(nameof(WheelSummary)); } }
    public Hotkey? Press { get => _press; set { if (Set(ref _press, value)) Raise(nameof(WheelSummary)); } }
    public bool Wrap { get; set; }
    public bool ShowToast { get; set; }
    public bool AutoStart { get; set; }
    public int StartupFunctionIndex { get; set; }

    public string WheelSummary =>
        $"   向右旋轉：{RotateRight?.ToString() ?? "（未設定）"}\n   向左旋轉：{RotateLeft?.ToString() ?? "（未設定）"}\n   按一下：　{Press?.ToString() ?? "（未設定）"}";

    string _wheelStatus = "";
    public string WheelStatus { get => _wheelStatus; set => Set(ref _wheelStatus, value); }
    public bool WheelHasBackup => WheelSetup.HasBackup;
    public void RefreshWheelBackup() => Raise(nameof(WheelHasBackup));

    public string VersionText => $"旋鈕切換 v{typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3)}";

    /// <summary>重新列舉裝置；已選的裝置如果目前沒連接，仍保留在清單裡並標示「未連接」。</summary>
    public void RefreshDevices()
    {
        var outputs = Modes.Select(m => m.Output?.Ref).ToList();
        var inputs = Modes.Select(m => m.Input?.Ref).ToList();
        RefreshOptions(outputs, inputs);
        for (int i = 0; i < Modes.Count; i++)
        {
            Modes[i].Output = Find(OutputOptions, outputs[i]);
            Modes[i].Input = Find(InputOptions, inputs[i]);
        }
    }

    void RefreshOptions(IEnumerable<DeviceRef?> wantedOutputs, IEnumerable<DeviceRef?> wantedInputs)
    {
        Fill(OutputOptions, DataFlow.Output, wantedOutputs);
        Fill(InputOptions, DataFlow.Input, wantedInputs);
    }

    static void Fill(ObservableCollection<DeviceOption> options, DataFlow flow, IEnumerable<DeviceRef?> wanted)
    {
        options.Clear();
        options.Add(NoChange);
        List<AudioDevice> active;
        try { active = AudioDevices.List(flow); }
        catch (Exception ex) { Log.Write("列舉裝置失敗: " + ex.Message); active = []; }

        foreach (var d in active) options.Add(new DeviceOption(d.Name, new DeviceRef(d.Id, d.Name)));
        foreach (var r in wanted.OfType<DeviceRef>().Distinct())
            if (AudioDevices.Resolve(active, r) is null && !options.Any(o => o.Ref == r))
                options.Add(new DeviceOption($"{r.Name}（未連接）", r));
    }

    internal DeviceOption FindOutput(DeviceRef? r) => Find(OutputOptions, r);
    internal DeviceOption FindInput(DeviceRef? r) => Find(InputOptions, r);

    static DeviceOption Find(IEnumerable<DeviceOption> options, DeviceRef? r)
    {
        if (r is null) return NoChange;
        return options.FirstOrDefault(o => o.Ref?.Id == r.Id)
            ?? options.FirstOrDefault(o => o.Ref is not null && o.Ref.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase))
            ?? NoChange;
    }

    /// <summary>檢查設定是否合理；有問題時回傳說明文字。</summary>
    public string? Validate()
    {
        if (Modes.Any(m => string.IsNullOrWhiteSpace(m.Name))) return "每個模式都要有名稱。";
        if (Modes.FirstOrDefault(m => m.Output?.Ref is null && m.Input?.Ref is null) is { } empty)
            return $"「{empty.Name}」沒有設定任何裝置，請至少選一個輸出或輸入裝置。";

        var keys = new List<(string Name, Hotkey Key)>();
        if (RotateRight is not null) keys.Add(("向右轉", RotateRight));
        if (RotateLeft is not null) keys.Add(("向左轉", RotateLeft));
        if (Press is not null) keys.Add(("按一下", Press));
        keys.AddRange(Modes.Where(m => m.Hotkey is not null).Select(m => (m.Name, m.Hotkey!)));
        var dup = keys.GroupBy(k => k.Key).FirstOrDefault(g => g.Count() > 1);
        return dup is null ? null : $"快速鍵 {dup.Key} 重複用在：{string.Join("、", dup.Select(k => k.Name))}";
    }

    internal AppSettings ToSettings()
    {
        var result = _source.Clone();
        result.Modes = Modes.Select(m => m.ToConfig()).ToList();
        result.RotateRight = RotateRight;
        result.RotateLeft = RotateLeft;
        result.Press = Press;
        result.Wrap = Wrap;
        result.ShowToast = ShowToast;
        result.StartupFunction = StartupFunctionIndex == 1 ? DialFunction.Switch : DialFunction.Volume;
        return result;
    }
}

public sealed class ModeViewModel : ObservableObject
{
    readonly string _id;
    string _name, _icon;
    DeviceOption? _output, _input;
    bool _includeCommunications;
    Hotkey? _hotkey;

    internal ModeViewModel(ModeConfig config, SettingsViewModel owner)
    {
        _id = config.Id;
        _name = config.Name;
        _icon = config.Icon;
        _output = owner.FindOutput(config.Output);
        _input = owner.FindInput(config.Input);
        _includeCommunications = config.IncludeCommunications;
        _hotkey = config.Hotkey;
    }

    public string Name { get => _name; set { if (Set(ref _name, value)) Raise(nameof(Summary)); } }
    public string Icon { get => _icon; set { if (Set(ref _icon, value)) Raise(nameof(Glyph)); } }
    public string Glyph => Icons.Glyph(_icon);
    public DeviceOption? Output { get => _output; set { if (Set(ref _output, value)) Raise(nameof(Summary)); } }
    public DeviceOption? Input { get => _input; set { if (Set(ref _input, value)) Raise(nameof(Summary)); } }
    public bool IncludeCommunications { get => _includeCommunications; set => Set(ref _includeCommunications, value); }
    public Hotkey? Hotkey { get => _hotkey; set { if (Set(ref _hotkey, value)) Raise(nameof(Summary)); } }

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (_output?.Ref is { } o) parts.Add(o.Name);
            if (_input?.Ref is { } i) parts.Add("🎤 " + i.Name);
            if (_hotkey is not null) parts.Add(_hotkey.ToString());
            return parts.Count == 0 ? "（未設定裝置）" : string.Join(" · ", parts);
        }
    }

    internal ModeConfig ToConfig() => new()
    {
        Id = _id,
        Name = _name.Trim(),
        Icon = _icon,
        Output = _output?.Ref,
        Input = _input?.Ref,
        IncludeCommunications = _includeCommunications,
        Hotkey = _hotkey,
    };
}

/// <summary>
/// 錄製快速鍵的輸入框：取得焦點後按下組合鍵就記錄下來，Backspace / Delete 清除。
/// 錄製期間暫停程式的全域快速鍵，否則按下的組合鍵會先被程式攔走。
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(Hotkey), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).UpdateText()));

    public Hotkey? Hotkey
    {
        get => (Hotkey?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public HotkeyBox()
    {
        // 子類別不會自動套用主題的 TextBox 樣式，要明確指定。
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        ContextMenu = null;
        UpdateText();
    }

    void UpdateText() => Text = Hotkey?.ToString() ?? "（未設定，點一下後按下組合鍵）";

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        (Application.Current as App)?.SuspendHotkeys();
        Text = "請按下組合鍵…";
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        (Application.Current as App)?.ResumeHotkeys();
        UpdateText();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.Back or Key.Delete) { Hotkey = null; UpdateText(); return; }
        if (key is Key.Tab or Key.Escape) { e.Handled = key == Key.Escape; MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // 只按了修飾鍵，等主要按鍵

        uint modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= DialSwitch.Hotkey.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= DialSwitch.Hotkey.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= DialSwitch.Hotkey.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= DialSwitch.Hotkey.MOD_WIN;

        // 沒有修飾鍵時只接受 F1–F24，避免把一般按鍵（例如字母）變成全域快速鍵。
        if (modifiers == 0 && key is not (>= Key.F1 and <= Key.F24))
        {
            Text = "請搭配 Ctrl / Alt / Shift / Win，或使用 F1–F24";
            return;
        }

        Hotkey = new Hotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        Text = Hotkey.ToString();
    }
}

public static class Converters
{
    public static readonly IValueConverter BoolToVisibility = new BoolToVisibilityConverter();

    sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is true ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
