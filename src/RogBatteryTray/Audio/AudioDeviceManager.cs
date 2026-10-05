using System.Runtime.InteropServices;
using RogBatteryTray.Hid;

namespace RogBatteryTray.Audio;

/// <summary>
/// Switches the Windows default audio render/capture endpoints.
/// Uses the (undocumented but widely used) IPolicyConfig COM interface,
/// the same approach as SoundSwitch / AudioSwitcher.
/// </summary>
public sealed class AudioDeviceManager
{
    public sealed record Endpoint(string Id, string InstanceId, string Name, bool IsRender);

    /// <summary>Current default endpoint ids per role, or null where unavailable.</summary>
    public sealed record DefaultIds(string? Render, string? Capture, string? RenderComm, string? CaptureComm);

    /// <summary>
    /// Headset endpoints are recognized by the VID/PID in their device instance id
    /// (robust against the user renaming the device), falling back to a name match
    /// when the instance id cannot be read.
    /// </summary>
    public static bool IsHeadset(Endpoint e) =>
        (e.InstanceId.Length > 0 && SupportedDevices.InstanceIdFragments.Any(
            f => e.InstanceId.Contains(f, StringComparison.OrdinalIgnoreCase)))
        || SupportedDevices.NameParts.Any(
            n => e.Name.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>Set Windows default render + capture to the ROG headset. Returns true on success.</summary>
    public bool SwitchToHeadset() => Switch(matchHeadset: true);

    /// <summary>
    /// Set Windows default render + capture back to the remembered non-headset devices.
    /// The console/multimedia roles go to renderId/captureId; the communications role
    /// goes to renderCommId/captureCommId (falling back to the console device).
    /// </summary>
    public bool SwitchToSpeakers(string? renderId, string? captureId, string? renderCommId, string? captureCommId)
    {
        bool ok = false;
        var endpoints = EnumActiveEndpoints();

        var render = endpoints.FirstOrDefault(e => e.IsRender && e.Id == renderId)
                  ?? endpoints.FirstOrDefault(e => e.IsRender && !IsHeadset(e));
        var capture = endpoints.FirstOrDefault(e => !e.IsRender && e.Id == captureId)
                   ?? endpoints.FirstOrDefault(e => !e.IsRender && !IsHeadset(e));
        var renderComm = endpoints.FirstOrDefault(e => e.IsRender && e.Id == renderCommId) ?? render;
        var captureComm = endpoints.FirstOrDefault(e => !e.IsRender && e.Id == captureCommId) ?? capture;

        if (render != null) ok |= SetDefault(render.Id, ERole.eConsole, ERole.eMultimedia);
        if (capture != null) ok |= SetDefault(capture.Id, ERole.eConsole, ERole.eMultimedia);
        if (renderComm != null) ok |= SetDefault(renderComm.Id, ERole.eCommunications);
        if (captureComm != null) ok |= SetDefault(captureComm.Id, ERole.eCommunications);
        return ok;
    }

    private bool Switch(bool matchHeadset)
    {
        bool ok = false;
        foreach (var e in EnumActiveEndpoints())
        {
            if (IsHeadset(e) == matchHeadset)
                ok |= SetDefault(e.Id, ERole.eConsole, ERole.eMultimedia, ERole.eCommunications);
        }
        return ok;
    }

    /// <summary>Current default render/capture endpoint ids for the console and communications roles.</summary>
    public DefaultIds GetDefaultIds()
    {
        return new DefaultIds(
            GetDefaultId(EDataFlow.eRender, ERole.eConsole),
            GetDefaultId(EDataFlow.eCapture, ERole.eConsole),
            GetDefaultId(EDataFlow.eRender, ERole.eCommunications),
            GetDefaultId(EDataFlow.eCapture, ERole.eCommunications));
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
                string name = ReadString(store, PkeyDeviceFriendlyName) ?? "";
                string instanceId = ReadString(store, DevpkeyDeviceInstanceId) ?? "";
                list.Add(new Endpoint(id, instanceId, name, flow == EDataFlow.eRender));
                Marshal.ReleaseComObject(store);
                Marshal.ReleaseComObject(device);
            }
            Marshal.ReleaseComObject(collection);
        }
        Marshal.ReleaseComObject(enumerator);
        return list;
    }

    private string? GetDefaultId(EDataFlow flow, ERole role)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        try
        {
            int hr = enumerator.GetDefaultAudioEndpoint(flow, role, out var device);
            if (hr != 0) return null;
            device.GetId(out string id);
            Marshal.ReleaseComObject(device);
            return id;
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    private static bool SetDefault(string deviceId, params ERole[] roles)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        int hr = 0;
        foreach (var role in roles)
            hr |= policy.SetDefaultEndpoint(deviceId, role);
        Marshal.ReleaseComObject(policy);
        return hr == 0;
    }

    // PKEY_Device_FriendlyName
    private static readonly PropertyKey PkeyDeviceFriendlyName =
        new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    // DEVPKEY_Device_InstanceId — contains e.g. "USB\VID_0B05&PID_1AFA\..."
    private static readonly PropertyKey DevpkeyDeviceInstanceId =
        new(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);

    private static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        store.GetValue(ref key, out PropVariant value);
        string? result = value.StringValue;
        value.Clear();
        return result;
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
        [PreserveSig] int GetShareMode(string pszDeviceName, bool bExclusive, IntPtr pMode);
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
