using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DialSwitch;

/// <summary>
/// 全域快速鍵。用一個訊息專用的隱形視窗接收 WM_HOTKEY；
/// 釋放、暫停或重新註冊時一定會先取消舊的註冊，不會殘留。
/// </summary>
sealed class HotkeyManager : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_NOREPEAT = 0x4000;
    static readonly IntPtr HWND_MESSAGE = new(-3);

    readonly Dictionary<int, Hotkey> _wanted = [];
    readonly HashSet<int> _registered = [];
    bool _suspended;

    public event Action<int>? Pressed;

    public HotkeyManager() => CreateHandle(new CreateParams { Parent = HWND_MESSAGE });

    /// <summary>以新的一組快速鍵取代全部；回傳註冊失敗（被其他程式佔用）的 id。</summary>
    public List<int> SetAll(IReadOnlyDictionary<int, Hotkey> hotkeys)
    {
        UnregisterAll();
        _wanted.Clear();
        foreach (var (id, key) in hotkeys) _wanted[id] = key;
        return _suspended ? [] : RegisterAll();
    }

    /// <summary>暫停：設定視窗在錄製快速鍵時呼叫，否則按下的組合鍵會被這裡攔走。</summary>
    public void Suspend()
    {
        _suspended = true;
        UnregisterAll();
    }

    public void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        RegisterAll();
    }

    List<int> RegisterAll()
    {
        var failed = new List<int>();
        foreach (var (id, key) in _wanted)
        {
            if (RegisterHotKey(Handle, id, key.Modifiers | MOD_NOREPEAT, key.Key)) _registered.Add(id);
            else failed.Add(id);
        }
        return failed;
    }

    void UnregisterAll()
    {
        foreach (var id in _registered) UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
            Pressed?.Invoke((int)m.WParam);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        UnregisterAll();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>模擬按下鍵盤的音量鍵：Windows 會照常調整音量並顯示音量提示。</summary>
static class VolumeKeys
{
    const ushort VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF;
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2;

    public static void Step(int direction)
    {
        ushort vk = direction > 0 ? VK_VOLUME_UP : VK_VOLUME_DOWN;
        INPUT[] inputs =
        [
            new() { Type = INPUT_KEYBOARD, Key = new KEYBDINPUT { Vk = vk } },
            new() { Type = INPUT_KEYBOARD, Key = new KEYBDINPUT { Vk = vk, Flags = KEYEVENTF_KEYUP } },
        ];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint Type;
        public KEYBDINPUT Key;
        // INPUT 是 union，最大的成員是 MOUSEINPUT，補足大小。
        readonly long _pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
