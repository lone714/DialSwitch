namespace DialSwitch;

/// <summary>套用模式、判斷目前是哪個模式、往前後切換。</summary>
sealed class ModeSwitcher
{
    string? _lastAppliedId;

    /// <summary>依系統目前的預設裝置判斷目前模式；沒有相符的就回傳 null。</summary>
    public ModeConfig? Current(IReadOnlyList<ModeConfig> modes)
    {
        var outputs = AudioDevices.List(DataFlow.Output);
        var inputs = AudioDevices.List(DataFlow.Input);
        string? outputId = AudioDevices.GetDefaultId(DataFlow.Output);
        string? inputId = AudioDevices.GetDefaultId(DataFlow.Input);

        bool Matches(ModeConfig m) =>
            (m.Output is not null || m.Input is not null)
            && (m.Output is null || AudioDevices.Resolve(outputs, m.Output)?.Id == outputId)
            && (m.Input is null || AudioDevices.Resolve(inputs, m.Input)?.Id == inputId);

        // 好幾個模式用同一組裝置時，以最後一次套用的為準。
        var last = modes.FirstOrDefault(m => m.Id == _lastAppliedId);
        return last is not null && Matches(last) ? last : modes.FirstOrDefault(Matches);
    }

    /// <summary>往前（-1）或往後（+1）一個模式。不繞回時，到頭尾就停在原處。</summary>
    public ModeConfig? Step(IReadOnlyList<ModeConfig> modes, int direction, bool wrap)
    {
        if (modes.Count == 0) return null;
        var current = Current(modes);
        if (current is null) return direction > 0 ? modes[0] : modes[^1];

        int next = IndexOf(modes, current) + direction;
        if (next >= 0 && next < modes.Count) return modes[next];
        return wrap ? modes[(next + modes.Count) % modes.Count] : current;
    }

    /// <summary>套用模式；回傳找不到的裝置清單（空的代表全部成功）。</summary>
    public List<string> Apply(ModeConfig mode)
    {
        var missing = new List<string>();
        Set(DataFlow.Output, mode.Output, mode, missing);
        Set(DataFlow.Input, mode.Input, mode, missing);
        _lastAppliedId = mode.Id;
        return missing;
    }

    static void Set(DataFlow flow, DeviceRef? reference, ModeConfig mode, List<string> missing)
    {
        if (reference is null) return;
        var device = AudioDevices.Resolve(AudioDevices.List(flow), reference);
        if (device is null)
        {
            missing.Add(reference.Name);
            return;
        }
        if (AudioDevices.SetDefault(flow, device.Id, mode.IncludeCommunications))
            Log.Write($"[{mode.Name}] {(flow == DataFlow.Output ? "輸出" : "輸入")} → {device.Name}");
    }

    static int IndexOf(IReadOnlyList<ModeConfig> modes, ModeConfig mode)
    {
        for (int i = 0; i < modes.Count; i++)
            if (ReferenceEquals(modes[i], mode)) return i;
        return -1;
    }
}
