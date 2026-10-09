namespace DialSwitch;

/// <summary>紀錄檔：%LOCALAPPDATA%\DialSwitch\switcher.log，超過上限就輪替一份 .old。</summary>
static class Log
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DialSwitch");
    static readonly string FilePath = Path.Combine(Folder, "switcher.log");
    const long MaxBytes = 512 * 1024;
    static readonly Lock Gate = new();

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static void Open()
    {
        if (File.Exists(FilePath)) Shell.Open(FilePath);
    }
}

/// <summary>開機自動啟動：寫在 HKCU\...\Run，可在「工作管理員 → 開機應用程式」看到與停用。</summary>
static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "DialSwitch";

    public static bool Enabled
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string path
                && path.Trim('"').Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            if (value == Enabled) return;
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Write($"開機自動啟動: {(value ? "開啟" : "關閉")}");
        }
    }
}

static class Shell
{
    /// <summary>用系統預設程式開啟檔案或網址（例如 ms-settings:）。</summary>
    public static bool Open(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Write($"無法開啟 {target}: {ex.Message}");
            return false;
        }
    }
}
