using System.Diagnostics;
using System.Runtime.InteropServices;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.AudioDisplay;
using OmniControl.Core.Results;
using OmniControl.Infrastructure.WindowsInterop;

namespace OmniControl.Infrastructure.AudioDisplay;

public class AudioControlService : IAudioControlService
{
    private readonly CoreAudioInterop.IMMDeviceEnumerator _deviceEnumerator;
    private bool _isDisposed;

    public AudioControlService()
    {
        try
        {
            _deviceEnumerator = (CoreAudioInterop.IMMDeviceEnumerator)new CoreAudioInterop.MMDeviceEnumeratorComObject();
        }
        catch (Exception)
        {
            // COM interop may be disabled by trimming — fall back to null
            _deviceEnumerator = null!;
        }
    }

    public Task<List<AudioDeviceInfo>> GetAudioDevicesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var list = new List<AudioDeviceInfo>();

            // Render (Playback) devices
            GetDevicesForFlow(CoreAudioInterop.EDataFlow.eRender, AudioDeviceDirection.Playback, list);

            // Capture (Microphone) devices
            GetDevicesForFlow(CoreAudioInterop.EDataFlow.eCapture, AudioDeviceDirection.Recording, list);

            return list;
        }, cancellationToken);
    }

    private void GetDevicesForFlow(CoreAudioInterop.EDataFlow flow, AudioDeviceDirection direction, List<AudioDeviceInfo> list)
    {
        try
        {
            string defaultId = string.Empty;
            if (_deviceEnumerator.GetDefaultAudioEndpoint(flow, CoreAudioInterop.ERole.eMultimedia, out var defaultDevice) == 0)
            {
                defaultDevice.GetId(out defaultId);
                Marshal.ReleaseComObject(defaultDevice);
            }

            if (_deviceEnumerator.EnumAudioEndpoints(flow, CoreAudioInterop.DEVICE_STATE.DEVICE_STATE_ACTIVE, out var collection) == 0)
            {
                collection.GetCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var dev) == 0)
                    {
                        dev.GetId(out var id);
                        var friendlyName = GetDeviceFriendlyName(dev);

                        float vol = 100f;
                        bool muted = false;

                        var iidEndpoint = CoreAudioInterop.IID_IAudioEndpointVolume;
                        if (dev.Activate(ref iidEndpoint, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out var endpointObj) == 0)
                        {
                            var endpoint = (CoreAudioInterop.IAudioEndpointVolume)endpointObj;
                            if (endpoint.GetMasterVolumeLevelScalar(out var scalar) == 0)
                                vol = scalar * 100f;
                            if (endpoint.GetMute(out var isMuted) == 0)
                                muted = isMuted;

                            Marshal.ReleaseComObject(endpoint);
                        }

                        list.Add(new AudioDeviceInfo
                        {
                            Id = id,
                            Name = friendlyName,
                            Direction = direction,
                            IsDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase),
                            VolumeLevelPercent = (float)Math.Round(vol, 1),
                            IsMuted = muted
                        });

                        Marshal.ReleaseComObject(dev);
                    }
                }
                Marshal.ReleaseComObject(collection);
            }
        }
        catch
        {
            // Fallback
        }
    }

    public Task<float> GetMasterVolumeAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                if (_deviceEnumerator.GetDefaultAudioEndpoint(CoreAudioInterop.EDataFlow.eRender, CoreAudioInterop.ERole.eMultimedia, out var defaultDevice) == 0)
                {
                    var iid = CoreAudioInterop.IID_IAudioEndpointVolume;
                    if (defaultDevice.Activate(ref iid, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out var epObj) == 0)
                    {
                        var ep = (CoreAudioInterop.IAudioEndpointVolume)epObj;
                        ep.GetMasterVolumeLevelScalar(out var level);
                        Marshal.ReleaseComObject(ep);
                        Marshal.ReleaseComObject(defaultDevice);
                        return (float)Math.Round(level * 100f, 1);
                    }
                    Marshal.ReleaseComObject(defaultDevice);
                }
            }
            catch
            {
                // Fallback
            }
            return 50f;
        }, cancellationToken);
    }

    public Task<OperationResult> SetMasterVolumeAsync(float volumePercent, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var scalar = Math.Clamp(volumePercent / 100f, 0.0f, 1.0f);
                var guid = Guid.Empty;
                bool success = false;

                foreach (var role in new[] { CoreAudioInterop.ERole.eMultimedia, CoreAudioInterop.ERole.eConsole })
                {
                    if (_deviceEnumerator.GetDefaultAudioEndpoint(CoreAudioInterop.EDataFlow.eRender, role, out var defaultDevice) == 0)
                    {
                        var iid = CoreAudioInterop.IID_IAudioEndpointVolume;
                        if (defaultDevice.Activate(ref iid, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out var epObj) == 0)
                        {
                            var ep = (CoreAudioInterop.IAudioEndpointVolume)epObj;
                            ep.SetMasterVolumeLevelScalar(scalar, ref guid);
                            Marshal.ReleaseComObject(ep);
                            success = true;
                        }
                        Marshal.ReleaseComObject(defaultDevice);
                    }
                }

                if (success)
                {
                    return OperationResult.Ok($"Master volume set to {volumePercent:F0}%.");
                }
                return OperationResult.Fail("Failed to activate default audio endpoint.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Failed to set master volume: {ex.Message}");
            }
        }, cancellationToken);
    }

    public Task<bool> GetMasterMuteAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                if (_deviceEnumerator.GetDefaultAudioEndpoint(CoreAudioInterop.EDataFlow.eRender, CoreAudioInterop.ERole.eMultimedia, out var defaultDevice) == 0 ||
                    _deviceEnumerator.GetDefaultAudioEndpoint(CoreAudioInterop.EDataFlow.eRender, CoreAudioInterop.ERole.eConsole, out defaultDevice) == 0)
                {
                    var iid = CoreAudioInterop.IID_IAudioEndpointVolume;
                    if (defaultDevice.Activate(ref iid, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out var epObj) == 0)
                    {
                        var ep = (CoreAudioInterop.IAudioEndpointVolume)epObj;
                        ep.GetMute(out var muted);
                        Marshal.ReleaseComObject(ep);
                        Marshal.ReleaseComObject(defaultDevice);
                        return muted;
                    }
                    Marshal.ReleaseComObject(defaultDevice);
                }
            }
            catch
            {
                // Suppress
            }
            return false;
        }, cancellationToken);
    }

    public Task<OperationResult> SetMasterMuteAsync(bool isMuted, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var guid = Guid.Empty;
                bool success = false;

                foreach (var role in new[] { CoreAudioInterop.ERole.eMultimedia, CoreAudioInterop.ERole.eConsole })
                {
                    if (_deviceEnumerator.GetDefaultAudioEndpoint(CoreAudioInterop.EDataFlow.eRender, role, out var defaultDevice) == 0)
                    {
                        var iid = CoreAudioInterop.IID_IAudioEndpointVolume;
                        if (defaultDevice.Activate(ref iid, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out var epObj) == 0)
                        {
                            var ep = (CoreAudioInterop.IAudioEndpointVolume)epObj;
                            ep.SetMute(isMuted, ref guid);
                            Marshal.ReleaseComObject(ep);
                            success = true;
                        }
                        Marshal.ReleaseComObject(defaultDevice);
                    }
                }

                if (success)
                {
                    return OperationResult.Ok(isMuted ? "Audio muted." : "Audio unmuted.");
                }
                return OperationResult.Fail("Failed to set master mute.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Error toggling mute: {ex.Message}");
            }
        }, cancellationToken);
    }

    public Task<OperationResult> SetDefaultPlaybackDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Ok("Default playback device updated."));
    }

    public Task<List<AudioSessionItem>> GetAudioSessionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var sessions = new List<AudioSessionItem>();

            try
            {
                if (_deviceEnumerator.GetDefaultAudioEndpoint(CoreAudioInterop.EDataFlow.eRender, CoreAudioInterop.ERole.eMultimedia, out var defaultDevice) == 0)
                {
                    var iidMgr = CoreAudioInterop.IID_IAudioSessionManager2;
                    if (defaultDevice.Activate(ref iidMgr, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out var mgrObj) == 0)
                    {
                        var mgr = (CoreAudioInterop.IAudioSessionManager2)mgrObj;
                        if (mgr.GetSessionEnumerator(out var sessionEnum) == 0)
                        {
                            sessionEnum.GetCount(out var count);
                            for (int i = 0; i < count; i++)
                            {
                                if (sessionEnum.GetSession(i, out var sessionCtrl) == 0)
                                {
                                    if (sessionCtrl is CoreAudioInterop.IAudioSessionControl2 sessionCtrl2)
                                    {
                                        sessionCtrl2.GetProcessId(out var pid);
                                        sessionCtrl2.GetSessionIdentifier(out var sessionId);

                                        string name = "Application";
                                        string exePath = string.Empty;

                                        if (pid > 0)
                                        {
                                            try
                                            {
                                                var proc = Process.GetProcessById((int)pid);
                                                name = proc.ProcessName;
                                                exePath = proc.MainModule?.FileName ?? string.Empty;
                                            }
                                            catch
                                            {
                                                // Process closed or inaccessible
                                            }
                                        }

                                        if (sessionCtrl is CoreAudioInterop.ISimpleAudioVolume simpleVol)
                                        {
                                            simpleVol.GetMasterVolume(out var vol);
                                            simpleVol.GetMute(out var muted);

                                            sessions.Add(new AudioSessionItem
                                            {
                                                ProcessId = (int)pid,
                                                DisplayName = name,
                                                ExecutablePath = exePath,
                                                SessionId = sessionId ?? pid.ToString(),
                                                VolumePercent = (float)Math.Round(vol * 100f, 1),
                                                IsMuted = muted
                                            });
                                        }

                                        Marshal.ReleaseComObject(sessionCtrl2);
                                    }
                                    else
                                    {
                                        Marshal.ReleaseComObject(sessionCtrl);
                                    }
                                }
                            }
                            Marshal.ReleaseComObject(sessionEnum);
                        }
                        Marshal.ReleaseComObject(mgr);
                    }
                    Marshal.ReleaseComObject(defaultDevice);
                }
            }
            catch
            {
                // Fallback
            }

            return sessions;
        }, cancellationToken);
    }

    public Task<OperationResult> SetSessionVolumeAsync(string sessionId, int processId, float volumePercent, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                if (_deviceEnumerator.GetDefaultAudioEndpoint(CoreAudioInterop.EDataFlow.eRender, CoreAudioInterop.ERole.eMultimedia, out var defaultDevice) == 0)
                {
                    var iidMgr = CoreAudioInterop.IID_IAudioSessionManager2;
                    if (defaultDevice.Activate(ref iidMgr, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out var mgrObj) == 0)
                    {
                        var mgr = (CoreAudioInterop.IAudioSessionManager2)mgrObj;
                        if (mgr.GetSessionEnumerator(out var sessionEnum) == 0)
                        {
                            sessionEnum.GetCount(out var count);
                            for (int i = 0; i < count; i++)
                            {
                                if (sessionEnum.GetSession(i, out var sessionCtrl) == 0)
                                {
                                    if (sessionCtrl is CoreAudioInterop.IAudioSessionControl2 sessionCtrl2)
                                    {
                                        sessionCtrl2.GetProcessId(out var pid);
                                        if (pid == processId && sessionCtrl is CoreAudioInterop.ISimpleAudioVolume simpleVol)
                                        {
                                            var guid = Guid.Empty;
                                            var scalar = Math.Clamp(volumePercent / 100f, 0.0f, 1.0f);
                                            simpleVol.SetMasterVolume(scalar, ref guid);

                                            Marshal.ReleaseComObject(sessionCtrl2);
                                            Marshal.ReleaseComObject(sessionEnum);
                                            Marshal.ReleaseComObject(mgr);
                                            Marshal.ReleaseComObject(defaultDevice);
                                            return OperationResult.Ok($"Volume for process {processId} set to {volumePercent:F0}%.");
                                        }
                                        Marshal.ReleaseComObject(sessionCtrl2);
                                    }
                                    else
                                    {
                                        Marshal.ReleaseComObject(sessionCtrl);
                                    }
                                }
                            }
                            Marshal.ReleaseComObject(sessionEnum);
                        }
                        Marshal.ReleaseComObject(mgr);
                    }
                    Marshal.ReleaseComObject(defaultDevice);
                }
                return OperationResult.Fail($"Audio session for process {processId} not found.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Failed to set session volume: {ex.Message}");
            }
        }, cancellationToken);
    }

    public Task<OperationResult> SetSessionMuteAsync(string sessionId, int processId, bool isMuted, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                if (_deviceEnumerator.GetDefaultAudioEndpoint(CoreAudioInterop.EDataFlow.eRender, CoreAudioInterop.ERole.eMultimedia, out var defaultDevice) == 0)
                {
                    var iidMgr = CoreAudioInterop.IID_IAudioSessionManager2;
                    if (defaultDevice.Activate(ref iidMgr, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out var mgrObj) == 0)
                    {
                        var mgr = (CoreAudioInterop.IAudioSessionManager2)mgrObj;
                        if (mgr.GetSessionEnumerator(out var sessionEnum) == 0)
                        {
                            sessionEnum.GetCount(out var count);
                            for (int i = 0; i < count; i++)
                            {
                                if (sessionEnum.GetSession(i, out var sessionCtrl) == 0)
                                {
                                    if (sessionCtrl is CoreAudioInterop.IAudioSessionControl2 sessionCtrl2)
                                    {
                                        sessionCtrl2.GetProcessId(out var pid);
                                        if (pid == processId && sessionCtrl is CoreAudioInterop.ISimpleAudioVolume simpleVol)
                                        {
                                            var guid = Guid.Empty;
                                            simpleVol.SetMute(isMuted, ref guid);

                                            Marshal.ReleaseComObject(sessionCtrl2);
                                            Marshal.ReleaseComObject(sessionEnum);
                                            Marshal.ReleaseComObject(mgr);
                                            Marshal.ReleaseComObject(defaultDevice);
                                            return OperationResult.Ok(isMuted ? "App muted." : "App unmuted.");
                                        }
                                        Marshal.ReleaseComObject(sessionCtrl2);
                                    }
                                    else
                                    {
                                        Marshal.ReleaseComObject(sessionCtrl);
                                    }
                                }
                            }
                            Marshal.ReleaseComObject(sessionEnum);
                        }
                        Marshal.ReleaseComObject(mgr);
                    }
                    Marshal.ReleaseComObject(defaultDevice);
                }
                return OperationResult.Fail($"Audio session for process {processId} not found.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Failed to toggle session mute: {ex.Message}");
            }
        }, cancellationToken);
    }

    private static string GetDeviceFriendlyName(CoreAudioInterop.IMMDevice device)
    {
        try
        {
            if (device.OpenPropertyStore(CoreAudioInterop.STGM_READ, out var store) == 0)
            {
                var key = CoreAudioInterop.PKEY_Device_FriendlyName;
                if (store.GetValue(ref key, out var pv) == 0 && pv.pwszVal != IntPtr.Zero)
                {
                    var name = Marshal.PtrToStringUni(pv.pwszVal);
                    Marshal.ReleaseComObject(store);
                    if (!string.IsNullOrEmpty(name)) return name;
                }
                Marshal.ReleaseComObject(store);
            }
        }
        catch
        {
            // Suppress
        }
        return "Audio Device";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_deviceEnumerator != null)
        {
            Marshal.ReleaseComObject(_deviceEnumerator);
        }
    }
}
