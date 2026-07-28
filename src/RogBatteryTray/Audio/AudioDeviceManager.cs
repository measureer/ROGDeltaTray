using System.Runtime.InteropServices;

namespace RogBatteryTray.Audio;

/// <summary>
/// Switches the Windows default audio render/capture endpoints.
/// Uses the (undocumented but widely used) IPolicyConfig COM interface,
/// the same approach as SoundSwitch / AudioSwitcher.
/// </summary>
public sealed class AudioDeviceManager
{
    private const string HeadsetNamePart = "ROG DELTA II";

    public sealed record Endpoint(string Id, string Name, bool IsRender);

    /// <summary>Set Windows default render + capture to the ROG headset. Returns true on success.</summary>
    public bool SwitchToHeadset() => Switch(matchHeadset: true);

    /// <summary>Set Windows default render + capture back to the remembered non-headset devices.</summary>
    public bool SwitchToSpeakers(string? renderId, string? captureId)
    {
        bool ok = false;
        var endpoints = EnumActiveEndpoints();

        var render = endpoints.FirstOrDefault(e => e.IsRender && e.Id == renderId)
                  ?? endpoints.FirstOrDefault(e => e.IsRender && !e.Name.Contains(HeadsetNamePart, StringComparison.OrdinalIgnoreCase));
        var capture = endpoints.FirstOrDefault(e => !e.IsRender && e.Id == captureId)
                   ?? endpoints.FirstOrDefault(e => !e.IsRender && !e.Name.Contains(HeadsetNamePart, StringComparison.OrdinalIgnoreCase));

        if (render != null) ok |= SetDefault(render.Id);
        if (capture != null) ok |= SetDefault(capture.Id);
        return ok;
    }

    private bool Switch(bool matchHeadset)
    {
        bool ok = false;
        foreach (var e in EnumActiveEndpoints())
        {
            bool isHeadset = e.Name.Contains(HeadsetNamePart, StringComparison.OrdinalIgnoreCase);
            if (isHeadset == matchHeadset)
                ok |= SetDefault(e.Id);
        }
        return ok;
    }

    /// <summary>Current default render/capture endpoint ids (eConsole role), or null.</summary>
    public (string? renderId, string? captureId) GetDefaultIds()
    {
        string? render = GetDefaultId(EDataFlow.eRender);
        string? capture = GetDefaultId(EDataFlow.eCapture);
        return (render, capture);
    }

    public List<Endpoint> EnumActiveEndpoints()
    {
        var list = new List<Endpoint>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        foreach (var flow in new[] { EDataFlow.eRender, EDataFlow.eCapture })
        {
            enumerator.EnumAudioEndpoints(flow, 0x1 /* DEVICE_STATE_ACTIVE */, out var collection);
            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                device.GetId(out string id);
                device.OpenPropertyStore(0 /* STGM_READ */, out var store);
                string name = ReadFriendlyName(store);
                list.Add(new Endpoint(id, name, flow == EDataFlow.eRender));
                Marshal.ReleaseComObject(store);
                Marshal.ReleaseComObject(device);
            }
            Marshal.ReleaseComObject(collection);
        }
        Marshal.ReleaseComObject(enumerator);
        return list;
    }

    private string? GetDefaultId(EDataFlow flow)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        try
        {
            int hr = enumerator.GetDefaultAudioEndpoint(flow, ERole.eConsole, out var device);
            if (hr != 0) return null;
            device.GetId(out string id);
            Marshal.ReleaseComObject(device);
            return id;
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    private static bool SetDefault(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        int hr = 0;
        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
            hr |= policy.SetDefaultEndpoint(deviceId, role);
        Marshal.ReleaseComObject(policy);
        return hr == 0;
    }

    private static string ReadFriendlyName(IPropertyStore store)
    {
        // PKEY_Device_FriendlyName {a45c254e-df1c-4efd-8020-67d146a850e0}, 14
        var key = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        store.GetValue(ref key, out PropVariant value);
        string name = value.StringValue ?? "";
        value.Clear();
        return name;
    }

    // ---------------- COM interop ----------------

    private enum EDataFlow { eRender = 0, eCapture = 1 }
    private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant pv);
        [PreserveSig] int Commit();
    }

    [Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat(string pszDeviceName, bool bDefault, IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
        [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode(string pszDeviceName, IntPtr pMode);
        [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ERole role);
        [PreserveSig] int SetEndpointVisibility(string pszDeviceName, bool bVisible);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
        public PropertyKey(Guid fmtid, int pid) { this.fmtid = fmtid; this.pid = pid; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1, wReserved2, wReserved3;
        public IntPtr p;
        public int p2;

        public string? StringValue => vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(p) : null;

        public void Clear() => PropVariantClear(ref this);

        [DllImport("Ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);
    }
}
