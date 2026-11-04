using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using OmniControl.Infrastructure.WindowsInterop;
using WinRT.Interop;

namespace OmniControl.App.Services;

public class SystemTrayManager : IDisposable
{
    private readonly Window _window;
    private readonly IntPtr _hWnd;
    private readonly Action _onRestoreRequested;
    private readonly Action _onExitRequested;
    private readonly NativeMethods.SubclassProc _subclassProc;
    private NativeMethods.NOTIFYICONDATA _nid;
    private bool _isCreated;
    private bool _disposed;

    private const uint IDM_OPEN = 1001;
    private const uint IDM_EXIT = 1004;

    public SystemTrayManager(
        Window window,
        Action onRestoreRequested,
        Action onExitRequested)
    {
        _window = window;
        _hWnd = WindowNative.GetWindowHandle(window);
        _onRestoreRequested = onRestoreRequested;
        _onExitRequested = onExitRequested;
        _subclassProc = OnSubclassMessage;

        CreateTrayIcon();
    }

    private void CreateTrayIcon()
    {
        if (_hWnd == IntPtr.Zero || _isCreated) return;

        // Subclass window to receive WM_TRAYICON
        NativeMethods.SetWindowSubclass(_hWnd, _subclassProc, (UIntPtr)999, UIntPtr.Zero);

        IntPtr hIcon = IntPtr.Zero;
        try
        {
            var appDir = AppContext.BaseDirectory;
            var iconPath = Path.Combine(appDir, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                hIcon = NativeMethods.LoadImage(IntPtr.Zero, iconPath, NativeMethods.IMAGE_ICON, 16, 16, NativeMethods.LR_LOADFROMFILE);
            }
        }
        catch { }

        _nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 101,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = (uint)NativeMethods.WM_TRAYICON,
            hIcon = hIcon,
            szTip = "OmniControl - Unified PC Control Center"
        };

        _isCreated = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _nid);
    }

    private IntPtr OnSubclassMessage(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == NativeMethods.WM_TRAYICON)
        {
            int eventId = lParam.ToInt32();
            if (eventId == NativeMethods.WM_LBUTTONUP || eventId == NativeMethods.WM_LBUTTONDBLCLK)
            {
                _window.DispatcherQueue.TryEnqueue(() => _onRestoreRequested());
                return IntPtr.Zero;
            }
            else if (eventId == NativeMethods.WM_RBUTTONUP)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var hMenu = NativeMethods.CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, (UIntPtr)IDM_OPEN, "Open OmniControl");
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, string.Empty);
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, (UIntPtr)IDM_EXIT, "Exit OmniControl");

            NativeMethods.GetCursorPos(out var pt);
            NativeMethods.SetForegroundWindow(_hWnd);

            uint cmd = NativeMethods.TrackPopupMenu(
                hMenu,
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON,
                pt.X,
                pt.Y,
                0,
                _hWnd,
                IntPtr.Zero);

            if (cmd == IDM_OPEN)
            {
                _window.DispatcherQueue.TryEnqueue(() => _onRestoreRequested());
            }
            else if (cmd == IDM_EXIT)
            {
                _window.DispatcherQueue.TryEnqueue(() => _onExitRequested());
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(hMenu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isCreated)
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _nid);
            _isCreated = false;
        }

        if (_hWnd != IntPtr.Zero)
        {
            NativeMethods.RemoveWindowSubclass(_hWnd, _subclassProc, (UIntPtr)999);
        }
    }
}
