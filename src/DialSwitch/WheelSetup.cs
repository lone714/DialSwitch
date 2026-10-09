using Microsoft.Win32;

namespace DialSwitch;

/// <summary>
/// 自動設定 Windows「滾輪」：把工具 1 設為「自訂工具」，並寫入向右轉 / 向左轉 / 按一下的快速鍵。
///
/// Windows 把滾輪設定存在 CloudStore 的一筆二進位資料，格式沒有公開文件，以下是實際觀察到的結構：
///   檔頭 43 42 01 00 0A 02 01 00 | 2A 06 &lt;修改時間（Unix 秒，varint）&gt; | 2A 2B 0E &lt;內容長度 varint&gt; &lt;內容&gt; | 00 00 00
///   內容 43 42 01 00
///        CA 14 CB 0A 10 &lt;數量&gt; &lt;工具清單（zigzag varint）&gt;      7 = 自訂工具、1 = 捲動、2 = 縮放、0 / -1 = 無
///        CA 1E CB 0A 10 &lt;數量&gt; &lt;按鍵（zigzag varint 的虛擬鍵碼）&gt;  向左轉
///              CB 14 10 &lt;數量&gt; &lt;按鍵&gt;                            向右轉
///              CB 1E 10 &lt;數量&gt; &lt;按鍵&gt;                            按一下
///        00 00 00
/// 因為格式可能隨 Windows 更新改變，寫入前一定先備份原本的資料，並提供還原；手動設定的方式也保留。
/// </summary>
static class WheelSetup
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.input.radialcontrollersettings\windows.data.input.radialcontrollersettings";
    const string ValueName = "Data";
    const int CustomTool = 7;
    static readonly int[] DefaultTools = [CustomTool, 1, 2, 0, -1, -1];
    static readonly byte[] Magic = [0x43, 0x42, 0x01, 0x00];

    static string BackupPath => Path.Combine(Log.Folder, "wheel-settings-backup.bin");

    public static bool HasBackup => File.Exists(BackupPath);

    public static byte[]? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as byte[];
    }

    /// <summary>寫入設定。第一次寫入前會備份 Windows 原本的資料（之後不覆蓋備份，保留最原始的那份）。</summary>
    public static void Apply(Hotkey left, Hotkey right, Hotkey press)
    {
        var current = Read();
        if (current is not null && !HasBackup)
        {
            Directory.CreateDirectory(Log.Folder);
            File.WriteAllBytes(BackupPath, current);
        }

        var tools = (current is null ? null : ParseTools(current)) ?? [.. DefaultTools];
        // 自訂工具放第一個（旋鈕叫不出選單時，永遠用第一個工具），其他工具維持原本順序。
        tools.RemoveAll(t => t == CustomTool);
        tools.Insert(0, CustomTool);
        if (tools.Count > DefaultTools.Length) tools.RemoveRange(DefaultTools.Length, tools.Count - DefaultTools.Length);

        Write(Build(tools, left, right, press, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        Log.Write($"已自動設定 Windows 滾輪：左 {left}、右 {right}、按 {press}");
    }

    /// <summary>還原成第一次自動設定前的資料。</summary>
    public static bool Restore()
    {
        if (!HasBackup) return false;
        Write(File.ReadAllBytes(BackupPath));
        File.Delete(BackupPath);
        Log.Write("已還原 Windows 滾輪設定");
        return true;
    }

    static void Write(byte[] data)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, data, RegistryValueKind.Binary);
    }

    public static byte[] Build(IReadOnlyList<int> tools, Hotkey left, Hotkey right, Hotkey press, long unixSeconds)
    {
        var body = new List<byte>(Magic);
        body.AddRange([0xCA, 0x14, 0xCB, 0x0A, 0x10]);
        WriteVarint(body, (ulong)tools.Count);
        foreach (var t in tools) WriteVarint(body, ZigZag(t));
        body.AddRange([0xCA, 0x1E]);
        AddShortcut(body, [0xCB, 0x0A], left);
        AddShortcut(body, [0xCB, 0x14], right);
        AddShortcut(body, [0xCB, 0x1E], press);
        body.AddRange([0x00, 0x00, 0x00]);

        var blob = new List<byte>(Magic);
        blob.AddRange([0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06]);
        WriteVarint(blob, (ulong)unixSeconds);
        blob.AddRange([0x2A, 0x2B, 0x0E]);
        WriteVarint(blob, (ulong)body.Count);
        blob.AddRange(body);
        blob.AddRange([0x00, 0x00, 0x00]);
        return [.. blob];
    }

    static void AddShortcut(List<byte> body, byte[] field, Hotkey hotkey)
    {
        // 依 Windows 自己寫入的順序：Ctrl、Alt、Shift、Win、主要按鍵（虛擬鍵碼）
        var keys = new List<int>();
        if ((hotkey.Modifiers & Hotkey.MOD_CONTROL) != 0) keys.Add(0x11);
        if ((hotkey.Modifiers & Hotkey.MOD_ALT) != 0) keys.Add(0x12);
        if ((hotkey.Modifiers & Hotkey.MOD_SHIFT) != 0) keys.Add(0x10);
        if ((hotkey.Modifiers & Hotkey.MOD_WIN) != 0) keys.Add(0x5B);
        keys.Add((int)hotkey.Key);

        body.AddRange(field);
        body.Add(0x10);
        WriteVarint(body, (ulong)keys.Count);
        foreach (var k in keys) WriteVarint(body, ZigZag(k));
    }

    /// <summary>從現有資料讀出工具清單；格式對不上就回傳 null。</summary>
    public static List<int>? ParseTools(byte[] blob)
    {
        ReadOnlySpan<byte> marker = [0xCA, 0x14, 0xCB, 0x0A, 0x10];
        int at = blob.AsSpan().IndexOf(marker);
        if (at < 0) return null;
        int pos = at + marker.Length;
        if (!TryReadVarint(blob, ref pos, out ulong count) || count > 16) return null;
        var tools = new List<int>();
        for (ulong i = 0; i < count; i++)
        {
            if (!TryReadVarint(blob, ref pos, out ulong v)) return null;
            tools.Add(UnZigZag(v));
        }
        return tools;
    }

    /// <summary>從現有資料讀出修改時間（自我測試用來比對格式）。</summary>
    public static long? ParseTimestamp(byte[] blob)
    {
        if (blob.Length < 11 || blob[8] != 0x2A || blob[9] != 0x06) return null;
        int pos = 10;
        return TryReadVarint(blob, ref pos, out ulong v) ? (long)v : null;
    }

    static ulong ZigZag(int n) => (uint)((n << 1) ^ (n >> 31));
    static int UnZigZag(ulong v) => (int)(v >> 1) ^ -(int)(v & 1);

    static void WriteVarint(List<byte> buffer, ulong value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            buffer.Add(value != 0 ? (byte)(b | 0x80) : b);
        } while (value != 0);
    }

    static bool TryReadVarint(byte[] data, ref int pos, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64 && pos < data.Length; shift += 7)
        {
            byte b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }
}
