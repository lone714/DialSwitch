using System.Runtime.InteropServices;

namespace DialSwitch;

enum DataFlow { Output = 0, Input = 1 }

sealed record AudioDevice(string Id, string Name);

/// <summary>
/// 透過 Windows Core Audio (MMDevice) 列舉裝置，並用 IPolicyConfig 設定預設裝置。
/// IPolicyConfig 沒有公開文件，但 Windows 自己的音效設定就是用它，各種切換工具也都靠它。
///
/// 每次呼叫都重新列舉、用完立即釋放所有 COM 物件，不留任何快取：
/// 螢幕切換輸入、耳機斷線等情況下裝置隨時會消失，舊的參照留著反而會出錯。
/// </summary>
static class AudioDevices
{
    /// <summary>目前啟用中的裝置。</summary>
    public static List<AudioDevice> List(DataFlow flow)
    {
        var result = new List<AudioDevice>();
        var enumerator = CreateEnumerator();
        try
        {
            Check(enumerator.EnumAudioEndpoints((EDataFlow)flow, DEVICE_STATE_ACTIVE, out var devices));
            try
            {
                Check(devices.GetCount(out uint count));
                for (uint i = 0; i < count; i++)
                {
                    Check(devices.Item(i, out var device));
                    try
                    {
                        Check(device.GetId(out var id));
                        result.Add(new AudioDevice(id, FriendlyName(device)));
                    }
                    finally { Release(device); }
                }
            }
            finally { Release(devices); }
        }
        finally { Release(enumerator); }
        return result;
    }

    /// <summary>在啟用中的裝置裡找出參照的那一個：先比 ID，再比名稱。</summary>
    public static AudioDevice? Resolve(IReadOnlyList<AudioDevice> active, DeviceRef? reference) =>
        reference is null ? null
        : active.FirstOrDefault(d => d.Id == reference.Id)
          ?? active.FirstOrDefault(d => d.Name.Equals(reference.Name, StringComparison.OrdinalIgnoreCase));

    public static string? GetDefaultId(DataFlow flow, bool communications = false)
    {
        var enumerator = CreateEnumerator();
        try { return DefaultId(enumerator, (EDataFlow)flow, communications ? ERole.Communications : ERole.Multimedia); }
        finally { Release(enumerator); }
    }

    public static AudioDevice? GetDefault(DataFlow flow)
    {
        var id = GetDefaultId(flow);
        return id is null ? null : List(flow).FirstOrDefault(d => d.Id == id);
    }

    /// <summary>
    /// 設為預設裝置（一般 + 多媒體），<paramref name="includeCommunications"/> 時也設為預設通訊裝置。
    /// 已經是預設的角色會略過；回傳是否真的有變更。
    /// </summary>
    public static bool SetDefault(DataFlow flow, string id, bool includeCommunications)
    {
        ERole[] roles = includeCommunications
            ? [ERole.Console, ERole.Multimedia, ERole.Communications]
            : [ERole.Console, ERole.Multimedia];

        var enumerator = CreateEnumerator();
        IPolicyConfig? policy = null;
        try
        {
            bool changed = false;
            foreach (var role in roles)
            {
                if (DefaultId(enumerator, (EDataFlow)flow, role) == id) continue;
                policy ??= (IPolicyConfig)new PolicyConfigComObject();
                Check(policy.SetDefaultEndpoint(id, role));
                changed = true;
            }
            return changed;
        }
        finally
        {
            Release(policy);
            Release(enumerator);
        }
    }

    static string? DefaultId(IMMDeviceEnumerator enumerator, EDataFlow flow, ERole role)
    {
        if (enumerator.GetDefaultAudioEndpoint(flow, role, out var device) != 0) return null;
        try { return device.GetId(out var id) == 0 ? id : null; }
        finally { Release(device); }
    }

    static string FriendlyName(IMMDevice device)
    {
        Check(device.OpenPropertyStore(STGM_READ, out var store));
        try
        {
            var key = PKEY_Device_FriendlyName;
            Check(store.GetValue(ref key, out var value));
            try { return value.VarType == VT_LPWSTR ? Marshal.PtrToStringUni(value.Data) ?? "" : ""; }
            finally { PropVariantClear(ref value); }
        }
        finally { Release(store); }
    }

    static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

    /// <summary>立即釋放 COM 物件，不等 GC。</summary>
    static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
            Marshal.FinalReleaseComObject(comObject);
    }

    static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    const int DEVICE_STATE_ACTIVE = 0x1;
    const int STGM_READ = 0;
    const ushort VT_LPWSTR = 31;
    static readonly PROPERTYKEY PKEY_Device_FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    // ---- COM 介面宣告：方法順序必須跟 Windows SDK 的 vtable 一致 ----

    enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    record struct PROPERTYKEY(Guid FormatId, int PropertyId);

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT
    {
        public ushort VarType;
        ushort _reserved1, _reserved2, _reserved3;
        public IntPtr Data;
        IntPtr _data2;
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PROPVARIANT value);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject;

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigComObject;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        // 前 10 個方法用不到，只佔位置讓 SetDefaultEndpoint 落在正確的 vtable 位置。
        [PreserveSig] int GetMixFormat(string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(string id, bool isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string id, bool isDefault, IntPtr defaultPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr period);
        [PreserveSig] int GetShareMode(string id, IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(string id, bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
    }
}
