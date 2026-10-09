using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialSwitch;

/// <summary>全部設定，存在 %APPDATA%\DialSwitch\settings.json。</summary>
public sealed class AppSettings
{
    public List<ModeConfig> Modes { get; set; } = [];

    /// <summary>旋鈕向右轉（Windows 滾輪自訂工具送出的快速鍵）。</summary>
    public Hotkey? RotateRight { get; set; } = Hotkey.CtrlAltShift(Hotkey.VK_F10);
    /// <summary>旋鈕向左轉。</summary>
    public Hotkey? RotateLeft { get; set; } = Hotkey.CtrlAltShift(Hotkey.VK_F9);
    /// <summary>按一下旋鈕：在「音量調整」與「音訊切換」之間切換旋鈕功能。</summary>
    public Hotkey? Press { get; set; } = Hotkey.CtrlAltShift(Hotkey.VK_F11);

    /// <summary>啟動時旋鈕的功能。</summary>
    public DialFunction StartupFunction { get; set; } = DialFunction.Volume;
    /// <summary>轉到最後一個模式後是否繞回第一個。關閉時連轉好幾格會停在頭尾，不會來回跳。</summary>
    public bool Wrap { get; set; }
    public bool ShowToast { get; set; } = true;

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, SettingsStore.Json), SettingsStore.Json)!;
}

/// <summary>旋鈕目前的功能：按一下旋鈕在兩者之間切換。</summary>
public enum DialFunction { Volume, Switch }

/// <summary>一個音訊模式：切換時套用的輸出與輸入裝置。</summary>
public sealed class ModeConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新模式";
    /// <summary>Segoe 圖示字型的字碼（十六進位），見 <see cref="Icons"/>。</summary>
    public string Icon { get; set; } = Icons.Speaker;
    /// <summary>null 表示切換到這個模式時不變更。</summary>
    public DeviceRef? Output { get; set; }
    public DeviceRef? Input { get; set; }
    /// <summary>是否也設為「預設通訊裝置」（Discord、Teams 等通話軟體使用）。</summary>
    public bool IncludeCommunications { get; set; } = true;
    /// <summary>直接切到這個模式的專屬快速鍵（選填）。</summary>
    public Hotkey? Hotkey { get; set; }
}

/// <summary>裝置參照：先用 ID 找，找不到（例如驅動重裝後 ID 變了）再用名稱找。</summary>
public sealed record DeviceRef(string Id, string Name);

public sealed record Hotkey(uint Modifiers, uint Key)
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;
    public const uint VK_F9 = 0x78, VK_F10 = 0x79, VK_F11 = 0x7A;

    public static Hotkey CtrlAltShift(uint key) => new(MOD_CONTROL | MOD_ALT | MOD_SHIFT, key);

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Modifiers & MOD_WIN) != 0) parts.Add("Win");
        parts.Add(System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)Key).ToString());
        return string.Join("+", parts);
    }
}

static class SettingsStore
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DialSwitch", "settings.json");

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // 中文直接寫出來，打開檔案看得懂。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>讀取設定；檔案不存在或壞掉時依目前的裝置建立預設值。</summary>
    public static AppSettings Load(out bool isNew)
    {
        isNew = false;
        MigrateFromOldName();
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? throw new JsonException("空的設定檔");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Write("設定檔讀取失敗，改用預設值: " + ex.Message);
            TryBackup();
        }
        isNew = true;
        return CreateDefaults();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // 先寫暫存檔再取代，寫到一半當機也不會留下壞掉的設定檔。
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>第一次執行：每個啟用中的輸出裝置建立一個模式，名稱與圖示用猜的，使用者再自行修改。</summary>
    static AppSettings CreateDefaults()
    {
        var settings = new AppSettings();
        try
        {
            foreach (var device in AudioDevices.List(DataFlow.Output))
                settings.Modes.Add(new ModeConfig
                {
                    Name = ShortName(device.Name),
                    Icon = Icons.Guess(device.Name),
                    Output = new DeviceRef(device.Id, device.Name),
                });
        }
        catch (Exception ex)
        {
            Log.Write("列舉裝置失敗: " + ex.Message);
        }
        return settings;
    }

    /// <summary>「喇叭 (HyperX Cloud Stinger)」這類名稱取括號內的部分，比較好認。</summary>
    static string ShortName(string name)
    {
        int open = name.IndexOf('('), close = name.LastIndexOf(')');
        return open > 0 && close > open + 1 ? name[(open + 1)..close].Trim() : name;
    }

    /// <summary>1.0.0 以前的測試版叫 AudioOutputSwitcher，設定檔在舊資料夾；沿用舊設定（只複製，不刪除舊檔）。</summary>
    static void MigrateFromOldName()
    {
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioOutputSwitcher", "settings.json");
        if (File.Exists(FilePath) || !File.Exists(old)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.Copy(old, FilePath);
            Log.Write("已沿用舊版設定: " + old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("沿用舊版設定失敗: " + ex.Message);
        }
    }

    static void TryBackup()
    {
        try { File.Copy(FilePath, FilePath + ".bad", overwrite: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
