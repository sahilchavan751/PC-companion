using OmniControl.Core.Models.Hardware;
using OmniControl.Core.Models.Processes;
using OmniControl.Core.Models.Startup;
using OmniControl.Core.Models.Environment;
using OmniControl.Core.Models.Services;
using OmniControl.Core.Models.AudioDisplay;
using OmniControl.Core.Models.Storage;
using OmniControl.Core.Models.Security;
using OmniControl.Core.Models.History;
using OmniControl.Core.Models.Search;
using OmniControl.Core.Results;

namespace OmniControl.Core.Interfaces;

public interface ISystemInfoService
{
    Task<SystemOverviewInfo> GetSystemOverviewAsync(CancellationToken cancellationToken = default);
    Task<CpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken = default);
    Task<List<GpuInfo>> GetGpuInfoAsync(CancellationToken cancellationToken = default);
    Task<RamInfo> GetRamInfoAsync(CancellationToken cancellationToken = default);
    Task<List<DiskDriveInfo>> GetDisksAsync(CancellationToken cancellationToken = default);
    Task<List<NetworkAdapterMetric>> GetNetworkAdaptersAsync(CancellationToken cancellationToken = default);
    Task<BatteryInfo> GetBatteryInfoAsync(CancellationToken cancellationToken = default);
    bool IsRunningAsAdministrator();
}

public interface IHardwareMonitorService : IDisposable
{
    event EventHandler<HardwareUpdateEventArgs>? MetricsUpdated;
    void StartMonitoring(int intervalMilliseconds = 1000);
    void StopMonitoring();
    IReadOnlyList<SensorMetric> GetAllSensors();
}

public class HardwareUpdateEventArgs : EventArgs
{
    public CpuInfo Cpu { get; init; } = new();
    public List<GpuInfo> Gpus { get; init; } = new();
    public RamInfo Ram { get; init; } = new();
    public List<DiskDriveInfo> Disks { get; init; } = new();
    public List<NetworkAdapterMetric> NetworkAdapters { get; init; } = new();
    public IReadOnlyList<SensorMetric> Sensors { get; init; } = Array.Empty<SensorMetric>();
}

public interface IPerformanceControlService
{
    Task<List<PowerPlanInfo>> GetPowerPlansAsync(CancellationToken cancellationToken = default);
    Task<PowerStatusSummary> GetPowerStatusSummaryAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SetActivePowerPlanAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<OperationResult> ApplyPerformanceProfileAsync(string profileName, CancellationToken cancellationToken = default);
}

public interface IProcessManagerService
{
    Task<List<ProcessItem>> GetProcessesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> TerminateProcessAsync(int processId, bool force = false, CancellationToken cancellationToken = default);
    Task<OperationResult> RestartProcessAsync(int processId, CancellationToken cancellationToken = default);
    Task<OperationResult> SetPriorityAsync(int processId, System.Diagnostics.ProcessPriorityClass priority, CancellationToken cancellationToken = default);
}

public interface IStartupManagerService
{
    Task<List<StartupEntry>> GetStartupEntriesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SetEntryEnabledAsync(StartupEntry entry, bool isEnabled, CancellationToken cancellationToken = default);
}

public interface IEnvironmentManagerService
{
    Task<List<EnvVariableItem>> GetVariablesAsync(EnvironmentVariableScope scope, CancellationToken cancellationToken = default);
    Task<OperationResult> SetVariableAsync(string name, string value, EnvironmentVariableScope scope, CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteVariableAsync(string name, EnvironmentVariableScope scope, CancellationToken cancellationToken = default);
    Task<List<PathItem>> GetPathItemsAsync(EnvironmentVariableScope scope, CancellationToken cancellationToken = default);
    Task<OperationResult> SavePathItemsAsync(List<PathItem> items, EnvironmentVariableScope scope, CancellationToken cancellationToken = default);
    void BroadcastEnvironmentChange();
}

public interface IWindowsServicesService
{
    Task<List<WindowsServiceItem>> GetServicesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> StartServiceAsync(string serviceName, CancellationToken cancellationToken = default);
    Task<OperationResult> StopServiceAsync(string serviceName, CancellationToken cancellationToken = default);
    Task<OperationResult> RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default);
    Task<OperationResult> ChangeStartupTypeAsync(string serviceName, WindowsServiceStartMode startMode, CancellationToken cancellationToken = default);
}

public interface IAudioControlService : IDisposable
{
    Task<List<AudioDeviceInfo>> GetAudioDevicesAsync(CancellationToken cancellationToken = default);
    Task<float> GetMasterVolumeAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SetMasterVolumeAsync(float volumePercent, CancellationToken cancellationToken = default);
    Task<bool> GetMasterMuteAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SetMasterMuteAsync(bool isMuted, CancellationToken cancellationToken = default);
    Task<OperationResult> SetDefaultPlaybackDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<List<AudioSessionItem>> GetAudioSessionsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SetSessionVolumeAsync(string sessionId, int processId, float volumePercent, CancellationToken cancellationToken = default);
    Task<OperationResult> SetSessionMuteAsync(string sessionId, int processId, bool isMuted, CancellationToken cancellationToken = default);
}

public interface IDisplayControlService
{
    Task<List<DisplayMonitorInfo>> GetMonitorsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SetBrightnessAsync(string deviceId, int brightnessPercent, CancellationToken cancellationToken = default);
}

public interface INetworkManagerService
{
    Task<List<NetworkAdapterMetric>> GetNetworkAdaptersAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<long>> RunPingTestAsync(string host = "1.1.1.1", CancellationToken cancellationToken = default);
    Task<OperationResult<string>> RunDnsLookupAsync(string host = "microsoft.com", CancellationToken cancellationToken = default);
    Task<OperationResult> SetAdapterEnabledAsync(string adapterId, bool isEnabled, CancellationToken cancellationToken = default);
}

public interface IStorageManagerService
{
    Task<List<DiskDriveInfo>> GetDrivesAsync(CancellationToken cancellationToken = default);
    Task<List<StorageCategoryBreakdown>> GetCategoryBreakdownAsync(string driveLetter = "C:", CancellationToken cancellationToken = default);
    Task<List<CleanupCandidateItem>> GetCleanupCandidatesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> ExecuteCleanupAsync(IEnumerable<CleanupCandidateItem> candidates, bool isDryRun = false, CancellationToken cancellationToken = default);
    Task<RecycleBinInfo> GetRecycleBinInfoAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> EmptyRecycleBinAsync(CancellationToken cancellationToken = default);
    Task<List<LargeFileItem>> FindLargeFilesAsync(string rootPath, long minSizeBytes = 100 * 1024 * 1024, int maxResults = 25, CancellationToken cancellationToken = default);
    Task<List<StorageFolderItem>> AnalyzeDirectoryContentsAsync(string folderPath, bool includeHiddenAndSystem = true, CancellationToken cancellationToken = default);
}

public interface ISecurityCenterService
{
    Task<SecurityStatusInfo> GetSecurityStatusAsync(CancellationToken cancellationToken = default);
}

public interface IOperationHistoryService
{
    Task RecordOperationAsync(ChangeHistoryRecord record);
    Task<List<ChangeHistoryRecord>> GetHistoryAsync(int limit = 100);
    Task<OperationResult> UndoOperationAsync(long recordId);
    Task<OperationResult> CreateBackupSnapshotAsync(string name, string category, string payloadJson);
    Task<List<BackupSnapshot>> GetSnapshotsAsync(string? category = null);
    Task<OperationResult> RestoreSnapshotAsync(long snapshotId);
}

public interface IGlobalSearchService
{
    Task<List<GlobalSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
