using System.Management;
using System.Runtime.InteropServices;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.AudioDisplay;
using OmniControl.Core.Results;
using OmniControl.Infrastructure.WindowsInterop;

namespace OmniControl.Infrastructure.AudioDisplay;

public class DisplayControlService : IDisplayControlService
{
    public async Task<List<DisplayMonitorInfo>> GetMonitorsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var monitors = new List<DisplayMonitorInfo>();

            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rc, IntPtr data) =>
            {
                var mi = new NativeMethods.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));

                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    var isPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                    var width = Math.Abs(mi.rcMonitor.right - mi.rcMonitor.left);
                    var height = Math.Abs(mi.rcMonitor.bottom - mi.rcMonitor.top);

                    var info = new DisplayMonitorInfo
                    {
                        DeviceId = mi.szDevice,
                        DeviceName = mi.szDevice,
                        FriendlyName = isPrimary ? "Primary Display" : "Secondary Display",
                        IsPrimary = isPrimary,
                        ResolutionWidth = width,
                        ResolutionHeight = height,
                        RefreshRateHz = 60
                    };

                    // Check WMI for internal laptop panels first (authoritative hardware backlight)
                    var wmiBrightness = GetWmiBrightness();
                    if (wmiBrightness.HasValue)
                    {
                        info.BrightnessPercent = wmiBrightness.Value;
                        info.IsBrightnessSupported = true;
                    }

                    // Query brightness via DXVA2 (for desktop external monitors)
                    if (NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var numPhys) && numPhys > 0)
                    {
                        var physArray = new NativeMethods.PHYSICAL_MONITOR[numPhys];
                        if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, numPhys, physArray))
                        {
                            var hPhys = physArray[0].hPhysicalMonitor;
                            if (!string.IsNullOrWhiteSpace(physArray[0].szPhysicalMonitorDescription))
                            {
                                info.FriendlyName = physArray[0].szPhysicalMonitorDescription;
                            }

                            if (!info.IsBrightnessSupported && NativeMethods.GetMonitorBrightness(hPhys, out _, out var curBright, out _))
                            {
                                info.BrightnessPercent = (int)curBright;
                                info.IsBrightnessSupported = true;
                            }

                            NativeMethods.DestroyPhysicalMonitors(numPhys, physArray);
                        }
                    }

                    // Universal fallback: Software Gamma Ramp
                    if (!info.IsBrightnessSupported)
                    {
                        info.BrightnessPercent = 100;
                        info.IsBrightnessSupported = true;
                    }

                    monitors.Add(info);
                }
                return true;
            }, IntPtr.Zero);

            return monitors;
        }, cancellationToken);
    }

    public async Task<OperationResult> SetBrightnessAsync(string deviceId, int brightnessPercent, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var clamped = Math.Clamp(brightnessPercent, 0, 100);
            bool updated = false;

            // 1. Try WMI first (Internal laptop screen backlight)
            if (SetWmiBrightness(clamped))
            {
                updated = true;
            }

            // 2. Try DXVA2 (DDC/CI for external desktop monitors)
            try
            {
                NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rc, IntPtr data) =>
                {
                    var mi = new NativeMethods.MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));

                    if (NativeMethods.GetMonitorInfo(hMonitor, ref mi) && 
                        (string.IsNullOrEmpty(deviceId) || string.Equals(mi.szDevice, deviceId, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var numPhys) && numPhys > 0)
                        {
                            var physArray = new NativeMethods.PHYSICAL_MONITOR[numPhys];
                            if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, numPhys, physArray))
                            {
                                var hPhys = physArray[0].hPhysicalMonitor;
                                if (NativeMethods.SetMonitorBrightness(hPhys, (uint)clamped))
                                {
                                    updated = true;
                                }
                                NativeMethods.DestroyPhysicalMonitors(numPhys, physArray);
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
                // Suppress DXVA2 errors
            }

            // 3. Fallback: software gamma ramp
            if (!updated)
            {
                updated = SetGammaBrightness(clamped);
            }

            return updated
                ? OperationResult.Ok($"Screen brightness set to {clamped}%.")
                : OperationResult.Unsupported("Display brightness control is not available on this display.");
        }, cancellationToken);
    }

    private static int? GetWmiBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (var obj in searcher.Get())
            {
                if (byte.TryParse(obj["CurrentBrightness"]?.ToString(), out var b))
                {
                    return b;
                }
            }
        }
        catch
        {
            // Suppress
        }
        return null;
    }

    private static bool SetWmiBrightness(int targetBrightness)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject obj in searcher.Get())
            {
                using var inParams = obj.GetMethodParameters("WmiSetBrightness");
                inParams["Timeout"] = (uint)1;
                inParams["Brightness"] = (byte)targetBrightness;
                obj.InvokeMethod("WmiSetBrightness", inParams, null);
                return true;
            }
        }
        catch
        {
            // Suppress
        }
        return false;
    }

    private static bool SetGammaBrightness(int brightnessPercent)
    {
        try
        {
            var hdc = NativeMethods.GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return false;

            try
            {
                var ramp = new NativeMethods.RAMP
                {
                    Red = new ushort[256],
                    Green = new ushort[256],
                    Blue = new ushort[256]
                };

                double factor = Math.Clamp(brightnessPercent / 100.0, 0.1, 1.0);

                for (int i = 0; i < 256; i++)
                {
                    var val = (ushort)Math.Clamp(Math.Round(i * 256.0 * factor), 0, 65535);
                    ramp.Red[i] = val;
                    ramp.Green[i] = val;
                    ramp.Blue[i] = val;
                }

                return NativeMethods.SetDeviceGammaRamp(hdc, ref ramp);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        catch
        {
            return false;
        }
    }
}
