using System.Runtime.InteropServices;

namespace OmniControl.Infrastructure.WindowsInterop;

public static class CoreAudioInterop
{
    public const int CLSCTX_ALL = 23;
    public const int STGM_READ = 0;

    public enum EDataFlow
    {
        eRender,
        eCapture,
        eAll
    }

    public enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    public enum DEVICE_STATE : uint
    {
        DEVICE_STATE_ACTIVE = 0x00000001,
        DEVICE_STATE_DISABLED = 0x00000002,
        DEVICE_STATE_NOTPRESENT = 0x00000004,
        DEVICE_STATE_UNPLUGGED = 0x00000008,
        DEVICE_STATEMASK_ALL = 0x0000000F
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumeratorComObject
    {
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, DEVICE_STATE stateMask, out IMMDeviceCollection devices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);
        [PreserveSig]
        int Item(uint index, out IMMDevice device);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        [PreserveSig]
        int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig]
        int GetState(out DEVICE_STATE state);
    }

    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);
        [PreserveSig]
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        [PreserveSig]
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct PROPVARIANT
    {
        [FieldOffset(0)]
        public ushort vt;
        [FieldOffset(8)]
        public IntPtr pwszVal;
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr client);
        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr client);
        [PreserveSig]
        int GetChannelCount(out uint channelCount);
        [PreserveSig]
        int SetMasterVolumeLevel(float levelDB, ref Guid eventContext);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig]
        int GetMasterVolumeLevel(out float levelDB);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig]
        int SetChannelVolumeLevel(uint channelNumber, float levelDB, ref Guid eventContext);
        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channelNumber, float level, ref Guid eventContext);
        [PreserveSig]
        int GetChannelVolumeLevel(uint channelNumber, out float levelDB);
        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channelNumber, out float level);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
    }

    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(ref Guid audioSessionGuid, uint streamFlags, out IntPtr sessionControl);
        [PreserveSig]
        int GetSimpleAudioVolume(ref Guid audioSessionGuid, uint streamFlags, out ISimpleAudioVolume audioVolume);
        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        [PreserveSig]
        int RegisterSessionNotification(IntPtr newSessionNotification);
        [PreserveSig]
        int UnregisterSessionNotification(IntPtr newSessionNotification);
    }

    [Guid("E2F5E9F1-1470-4DC0-AC90-D9F216560E77"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);
        [PreserveSig]
        int GetSession(int sessionIndex, out IAudioSessionControl session);
    }

    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out int state);
        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        [PreserveSig]
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        [PreserveSig]
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
        [PreserveSig]
        int GetGroupingParam(out Guid groupingParam);
        [PreserveSig]
        int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr newNotifications);
        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr newNotifications);
    }

    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl2 : IAudioSessionControl
    {
        [PreserveSig]
        new int GetState(out int state);
        [PreserveSig]
        new int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        [PreserveSig]
        new int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        [PreserveSig]
        new int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        [PreserveSig]
        new int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
        [PreserveSig]
        new int GetGroupingParam(out Guid groupingParam);
        [PreserveSig]
        new int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        [PreserveSig]
        new int RegisterAudioSessionNotification(IntPtr newNotifications);
        [PreserveSig]
        new int UnregisterAudioSessionNotification(IntPtr newNotifications);

        [PreserveSig]
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
        [PreserveSig]
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
        [PreserveSig]
        int GetProcessId(out uint retVal);
        [PreserveSig]
        int IsSystemSoundsSession();
        [PreserveSig]
        int SetDuckingPreference(bool optOut);
    }

    [Guid("87CE5498-68D6-44E5-AC21-AB2490770F90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISimpleAudioVolume
    {
        [PreserveSig]
        int SetMasterVolume(float fLevel, ref Guid eventContext);
        [PreserveSig]
        int GetMasterVolume(out float pfLevel);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid eventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    public static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    public static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    public static readonly PROPERTYKEY PKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 14
    };
}
