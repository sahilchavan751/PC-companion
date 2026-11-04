using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using OmniControl.App.ViewModels;
using OmniControl.Core.AdminContract;
using OmniControl.Core.Interfaces;
using OmniControl.Infrastructure.Admin;
using OmniControl.Infrastructure.AudioDisplay;
using OmniControl.Infrastructure.EnvironmentVars;
using OmniControl.Infrastructure.Hardware;
using OmniControl.Infrastructure.Network;
using OmniControl.Infrastructure.Persistence;
using OmniControl.Infrastructure.Processes;
using OmniControl.Infrastructure.Search;
using OmniControl.Infrastructure.Security;
using OmniControl.Infrastructure.Services;
using OmniControl.Infrastructure.Startup;
using OmniControl.Infrastructure.Storage;

namespace OmniControl.App;

public partial class App : Application
{
    private Window? _window;
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniControl");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), $"[AppDomain] {DateTime.Now}: {e.ExceptionObject}\n");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniControl");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), $"[UnobservedTask] {DateTime.Now}: {e.Exception}\n");
            e.SetObserved();
        };

        try
        {
            InitializeComponent();

            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            UnhandledException += (s, e) =>
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniControl");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "crash.log"), $"[UnhandledException] {DateTime.Now}: {e.Exception} - {e.Message}\n");
                e.Handled = true;
            };
        }
        catch (Exception ex)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniControl");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "constructor_error.log"), ex.ToString());
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core & Infrastructure Services (Singletons)
        services.AddSingleton<IAdminElevationService, AdminElevationService>();
        services.AddSingleton<IOperationHistoryService, SqliteOperationHistoryService>();
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IHardwareMonitorService, HardwareMonitorService>();
        services.AddSingleton<IPerformanceControlService, PerformanceControlService>();
        services.AddSingleton<IProcessManagerService, ProcessManagerService>();
        services.AddSingleton<IStartupManagerService, StartupManagerService>();
        services.AddSingleton<IEnvironmentManagerService, EnvironmentManagerService>();
        services.AddSingleton<IWindowsServicesService, WindowsServicesService>();
        services.AddSingleton<IAudioControlService, AudioControlService>();
        services.AddSingleton<IDisplayControlService, DisplayControlService>();
        services.AddSingleton<INetworkManagerService, NetworkManagerService>();
        services.AddSingleton<IStorageManagerService, StorageManagerService>();
        services.AddSingleton<ISecurityCenterService, SecurityCenterService>();
        services.AddSingleton<IGlobalSearchService, GlobalSearchService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<OverviewViewModel>();
        services.AddTransient<PerformanceViewModel>();
        services.AddTransient<ProcessesViewModel>();
        services.AddTransient<StartupViewModel>();
        services.AddTransient<EnvironmentViewModel>();
        services.AddTransient<ServicesViewModel>();
        services.AddTransient<HardwareViewModel>();
        services.AddTransient<AudioDisplayViewModel>();
        services.AddTransient<NetworkViewModel>();
        services.AddTransient<StorageViewModel>();
        services.AddTransient<SecurityViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniControl");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "launched_error.log"), ex.ToString());
        }
    }
}
