using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DialSwitch;

/// <summary>
/// 自我測試：DialSwitch.exe --selftest [次數]
/// 在程式內部重複執行切換流程中「不會改變任何設定」的部分（列舉裝置、判斷目前模式、顯示提示卡片），
/// 每隔一段強制回收記憶體後記錄用量，用來確認沒有記憶體或控制代碼洩漏。
/// 不模擬任何按鍵、不切換音訊裝置、不寫入任何設定。結果寫到紀錄資料夾的 selftest.txt。
/// </summary>
static class SelfTest
{
    public static void Run(int rounds)
    {
        var report = new StringBuilder();
        report.AppendLine($"自我測試 {DateTime.Now:yyyy-MM-dd HH:mm:ss}，{rounds} 輪");

        var settings = SettingsStore.Load(out _);
        var switcher = new ModeSwitcher();
        using var toast = new Toast();

        Snapshot(report, "開始前");
        var timer = Stopwatch.StartNew();
        for (int i = 1; i <= rounds; i++)
        {
            var current = switcher.Current(settings.Modes);
            AudioDevices.GetDefault(DataFlow.Input);
            toast.ShowMessage(current?.Icon ?? Icons.Speaker, $"自我測試 {i}/{rounds}", current?.Name ?? "—");
            System.Windows.Forms.Application.DoEvents();
            if (i % Math.Max(1, rounds / 4) == 0) Snapshot(report, $"第 {i} 輪");
        }
        report.AppendLine($"平均每輪 {timer.Elapsed.TotalMilliseconds / rounds:F2} ms");

        // 等提示卡片淡出、計時器停止後再量一次
        var settle = Stopwatch.StartNew();
        while (settle.ElapsedMilliseconds < 2500) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(15); }
        Snapshot(report, "結束後");

        report.AppendLine();
        report.AppendLine(WheelFormatCheck(settings));

        var path = Path.Combine(Log.Folder, "selftest.txt");
        Directory.CreateDirectory(Log.Folder);
        File.WriteAllText(path, report.ToString());
    }

    static void Snapshot(StringBuilder report, string label)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var p = Process.GetCurrentProcess();
        report.AppendLine(string.Format("{0,-8} managed={1,7:N0}KB  private={2,7:N0}KB  handles={3,4}  gdi={4,3}  user={5,3}",
            label, GC.GetTotalMemory(false) / 1024, p.PrivateMemorySize64 / 1024, p.HandleCount,
            GetGuiResources(p.Handle, 0), GetGuiResources(p.Handle, 1)));
    }

    /// <summary>用目前的快速鍵重新產生滾輪設定，和 Windows 現有的資料比對，確認格式理解正確（不寫入）。</summary>
    static string WheelFormatCheck(AppSettings settings)
    {
        var current = WheelSetup.Read();
        if (current is null) return "滾輪格式比對：Windows 尚未有滾輪設定資料";
        if (settings.RotateLeft is null || settings.RotateRight is null || settings.Press is null) return "滾輪格式比對：快速鍵未設定完整，略過";
        var tools = WheelSetup.ParseTools(current);
        var time = WheelSetup.ParseTimestamp(current);
        if (tools is null || time is null) return "滾輪格式比對：無法解析現有資料";
        var rebuilt = WheelSetup.Build(tools, settings.RotateLeft, settings.RotateRight, settings.Press, time.Value);
        return rebuilt.AsSpan().SequenceEqual(current)
            ? $"滾輪格式比對：完全相同（{current.Length} bytes），工具 [{string.Join(", ", tools)}]"
            : $"滾輪格式比對：不同\n  現有 {Convert.ToHexString(current)}\n  產生 {Convert.ToHexString(rebuilt)}";
    }

    [DllImport("user32.dll")]
    static extern uint GetGuiResources(IntPtr process, uint flags);
}
