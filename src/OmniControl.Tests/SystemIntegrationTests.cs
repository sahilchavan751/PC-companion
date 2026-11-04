using OmniControl.Core.Models.Environment;
using OmniControl.Infrastructure.Admin;
using OmniControl.Infrastructure.EnvironmentVars;
using OmniControl.Infrastructure.Hardware;
using OmniControl.Infrastructure.Network;
using OmniControl.Infrastructure.Persistence;
using OmniControl.Infrastructure.Processes;
using OmniControl.Infrastructure.Security;
using OmniControl.Infrastructure.Services;
using OmniControl.Infrastructure.Startup;
using OmniControl.Infrastructure.Storage;
using Xunit;

namespace OmniControl.Tests;

public class SystemIntegrationTests
{
    private readonly AdminElevationService _elevationService;
    private readonly SqliteOperationHistoryService _historyService;

    public SystemIntegrationTests()
    {
        _elevationService = new AdminElevationService();
        _historyService = new SqliteOperationHistoryService(_elevationService);
    }

    [Fact]
    public async Task SystemInfoService_RetrievesRealSystemMetrics()
    {
        var service = new SystemInfoService();
        var overview = await service.GetSystemOverviewAsync();

        Assert.False(string.IsNullOrWhiteSpace(overview.DeviceName));
        Assert.False(string.IsNullOrWhiteSpace(overview.OsName));
        Assert.True(overview.Cpu.LogicalProcessorCount > 0);
        Assert.True(overview.Ram.TotalPhysicalBytes > 0);
        Assert.NotEmpty(overview.Disks);
    }

    [Fact]
    public async Task ProcessManagerService_EnumeratesRealProcesses()
    {
        var service = new ProcessManagerService(_elevationService);
        var processes = await service.GetProcessesAsync();

        Assert.NotEmpty(processes);
        Assert.Contains(processes, p => p.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase) || p.Id > 0);
    }

    [Fact]
    public async Task StartupManagerService_DiscoversStartupEntries()
    {
        var service = new StartupManagerService(_elevationService, _historyService);
        var entries = await service.GetStartupEntriesAsync();

        // Should execute without throwing and return a list
        Assert.NotNull(entries);
    }

    [Fact]
    public async Task EnvironmentManagerService_ReadsRealUserVariablesAndPath()
    {
        var service = new EnvironmentManagerService(_elevationService, _historyService);
        var vars = await service.GetVariablesAsync(EnvironmentVariableScope.User);
        var paths = await service.GetPathItemsAsync(EnvironmentVariableScope.User);

        Assert.NotNull(vars);
        Assert.NotNull(paths);
    }

    [Fact]
    public async Task WindowsServicesService_EnumeratesInstalledServices()
    {
        var service = new WindowsServicesService(_elevationService, _historyService);
        var services = await service.GetServicesAsync();

        Assert.NotEmpty(services);
        Assert.Contains(services, s => s.ServiceName.Equals("RpcSs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StorageManagerService_ReadsDrivesAndRecycleBin()
    {
        var service = new StorageManagerService();
        var drives = await service.GetDrivesAsync();
        var rb = await service.GetRecycleBinInfoAsync();

        Assert.NotEmpty(drives);
        Assert.NotNull(rb);
    }

    [Fact]
    public async Task StorageManagerService_AnalyzesDirectoryContentsWithHiddenAndSystem()
    {
        var service = new StorageManagerService();
        var tempDir = Path.GetTempPath();
        var results = await service.AnalyzeDirectoryContentsAsync(tempDir, includeHiddenAndSystem: true);

        Assert.NotNull(results);
    }

    [Fact]
    public async Task SecurityCenterService_InspectsLocalPosture()
    {
        var service = new SecurityCenterService();
        var status = await service.GetSecurityStatusAsync();

        Assert.NotNull(status);
        Assert.False(string.IsNullOrWhiteSpace(status.AntivirusProductName));
    }

    [Fact]
    public async Task NetworkManagerService_EnumeratesAdaptersAndPings()
    {
        var service = new NetworkManagerService();
        var adapters = await service.GetNetworkAdaptersAsync();

        Assert.NotNull(adapters);

        var pingResult = await service.RunPingTestAsync("127.0.0.1");
        Assert.True(pingResult.IsSuccess);
    }

    [Fact]
    public async Task AudioControlService_ReadsVolumeAndDevices()
    {
        var service = new OmniControl.Infrastructure.AudioDisplay.AudioControlService();
        var vol = await service.GetMasterVolumeAsync();
        var devices = await service.GetAudioDevicesAsync();
        var sessions = await service.GetAudioSessionsAsync();

        Assert.True(vol >= 0 && vol <= 100);
        Assert.NotNull(devices);
        Assert.NotNull(sessions);

        var setRes = await service.SetMasterVolumeAsync(vol);
        Assert.True(setRes.IsSuccess, $"SetMasterVolume failed: {setRes.Message}");
    }

    [Fact]
    public async Task DisplayControlService_ReadsMonitorsAndBrightness()
    {
        var service = new OmniControl.Infrastructure.AudioDisplay.DisplayControlService();
        var monitors = await service.GetMonitorsAsync();

        Assert.NotNull(monitors);
        if (monitors.Count > 0)
        {
            var res = await service.SetBrightnessAsync(monitors[0].DeviceId, 80);
            Assert.True(res.IsSuccess, $"SetBrightness failed: {res.Message}");
        }
    }
}
