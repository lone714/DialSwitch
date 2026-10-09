namespace DialSwitch;

/// <summary>
/// 模式可選的圖示：Segoe Fluent Icons / Segoe MDL2 Assets 字型的字碼。
/// 兩種字型 Windows 10/11 都內建，這些常用圖示的字碼相同。
/// </summary>
public static class Icons
{
    public const string Headphones = "E7F6", Headset = "E95B", Monitor = "E7F4", Speaker = "E7F5",
        Volume = "E767", Microphone = "E720", Check = "E73E", Warning = "E7BA";

    public static readonly IReadOnlyList<(string Code, string Label)> Choices =
    [
        (Headphones, "耳機"), (Headset, "耳麥"), (Monitor, "螢幕"), (Speaker, "喇叭"),
        (Volume, "音量"), (Microphone, "麥克風"), ("E7FC", "遊戲"), ("E714", "視訊"),
        ("E8D6", "音樂"), ("E93E", "直播"), ("E702", "藍牙"), ("E716", "通話"),
        ("E717", "電話"), ("E80F", "居家"), ("E821", "工作"), ("E7F8", "筆電"),
        ("E977", "桌機"), ("E708", "夜間"),
    ];

    /// <summary>WPF 用：有 Fluent 就用 Fluent，否則退回 MDL2。</summary>
    public const string WpfFontFamily = "Segoe Fluent Icons, Segoe MDL2 Assets";

    /// <summary>WinForms 不支援字型後援清單，挑一個實際有安裝的。</summary>
    public static readonly string WinFormsFontFamily = IsInstalled("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static string Glyph(string code) =>
        int.TryParse(code, System.Globalization.NumberStyles.HexNumber, null, out int value) ? char.ConvertFromUtf32(value) : "";

    public static string Guess(string deviceName)
    {
        string n = deviceName.ToLowerInvariant();
        if (n.Contains("headset") || n.Contains("耳麥")) return Headset;
        if (n.Contains("headphone") || n.Contains("耳機") || n.Contains("hyperx") || n.Contains("airpods") || n.Contains("buds")) return Headphones;
        if (n.Contains("nvidia") || n.Contains("hdmi") || n.Contains("display") || n.Contains("amd high definition") || n.Contains("intel(r) display")) return Monitor;
        return Speaker;
    }

    static bool IsInstalled(string family)
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        return fonts.Families.Any(f => f.Name.Equals(family, StringComparison.OrdinalIgnoreCase));
    }
}
