using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using OmniControl.App.Services;
using OmniControl.App.ViewModels;
using OmniControl.Infrastructure.WindowsInterop;
using WinRT.Interop;

namespace OmniControl.App;

public sealed partial class MainWindow : Window
{
    private readonly SystemTrayManager? _trayManager;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Hook window closing to handle Minimize-To-Tray
        AppWindow.Closing += AppWindow_Closing;

        // Initialize system tray integration
        try
        {
            _trayManager = new SystemTrayManager(this, RestoreWindow, ExitApplication);
        }
        catch
        {
            // Fallback gracefully if system tray registration fails
        }

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_isExiting) return;

        var settings = App.Services.GetService<SettingsViewModel>();
        if (settings != null && settings.MinimizeToTray)
        {
            args.Cancel = true;
            AppWindow.Hide();
        }
        else
        {
            _trayManager?.Dispose();
        }
    }

    private void RestoreWindow()
    {
        AppWindow.Show();
        var hWnd = WindowNative.GetWindowHandle(this);
        NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(hWnd);
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _trayManager?.Dispose();
        Application.Current.Exit();
    }
}
