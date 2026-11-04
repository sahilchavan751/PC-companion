using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Hardware;
using OmniControl.Core.Models.Storage;

namespace OmniControl.App.ViewModels;

public partial class StorageViewModel : ObservableObject
{
    private readonly IStorageManagerService _storageService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private string _recycleBinSize = "0 MB";

    [ObservableProperty]
    private long _recycleBinItemsCount;

    [ObservableProperty]
    private int _cleanupCandidatesCount;

    [ObservableProperty]
    private string _cleanupSummaryText = "Scan to find temporary files safe for removal.";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private DiskDriveInfo? _selectedDrive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTreeMode))]
    [NotifyPropertyChangedFor(nameof(IsCleanupMode))]
    [NotifyPropertyChangedFor(nameof(IsLargeFilesMode))]
    private string _selectedViewMode = "TreeAnalyzer"; // "TreeAnalyzer", "Cleanup", "LargeFiles"

    public bool IsTreeMode => SelectedViewMode == "TreeAnalyzer";
    public bool IsCleanupMode => SelectedViewMode == "Cleanup";
    public bool IsLargeFilesMode => SelectedViewMode == "LargeFiles";

    // Storage Tree Properties
    [ObservableProperty]
    private string _currentAnalyzedPath = @"C:\";

    [ObservableProperty]
    private bool _isTreeScanning;

    [ObservableProperty]
    private string _scanStatusText = "Ready to analyze storage allocation.";

    [ObservableProperty]
    private bool _includeHiddenAndSystem = true;

    [ObservableProperty]
    private bool _canNavigateUp;

    [ObservableProperty]
    private string _totalAnalyzedSizeFormatted = "0 B";

    [ObservableProperty]
    private int _totalAnalyzedItemCount;

    public ObservableCollection<DiskDriveInfo> Drives { get; } = new();
    public ObservableCollection<StorageCategoryBreakdown> Categories { get; } = new();
    public ObservableCollection<CleanupCandidateItem> CleanupItems { get; } = new();
    public ObservableCollection<LargeFileItem> LargeFiles { get; } = new();
    public ObservableCollection<StorageFolderItem> TreeItems { get; } = new();
    public ObservableCollection<string> Breadcrumbs { get; } = new();

    private readonly SynchronizationContext? _syncContext;
    private readonly Timer? _autoRefreshTimer;
    private CancellationTokenSource? _treeScanCts;
    private bool _isDisposed;

    public StorageViewModel(IStorageManagerService storageService, MainViewModel mainViewModel)
    {
        _storageService = storageService;
        _mainViewModel = mainViewModel;
        _syncContext = SynchronizationContext.Current;

        _ = LoadInitialDataAsync();

        // 3-second live auto-refresh for drive space & recycle bin metrics
        _autoRefreshTimer = new Timer(async _ =>
        {
            if (_isDisposed) return;
            try
            {
                var drives = await _storageService.GetDrivesAsync();
                var rb = await _storageService.GetRecycleBinInfoAsync();

                _syncContext?.Post(__ =>
                {
                    if (_isDisposed) return;
                    RecycleBinSize = rb.SizeFormatted;
                    RecycleBinItemsCount = rb.ItemCount;

                    if (drives.Count == Drives.Count)
                    {
                        for (int i = 0; i < drives.Count; i++)
                        {
                            Drives[i].FreeBytes = drives[i].FreeBytes;
                        }
                    }
                }, null);
            }
            catch
            {
                // Suppress background poll errors
            }
        }, null, 2500, 2500);
    }

    private async Task LoadInitialDataAsync()
    {
        IsLoading = true;
        try
        {
            await RefreshDrivesAsync();
            await RefreshRecycleBinAsync();
            await RefreshCleanupCandidatesAsync();

            // Default to first drive or C:\
            if (Drives.Count > 0)
            {
                SelectedDrive = Drives.FirstOrDefault(d => d.IsSystemDrive) ?? Drives[0];
                CurrentAnalyzedPath = SelectedDrive.DriveLetter.TrimEnd('\\') + "\\";
            }

            // Perform initial tree scan
            await AnalyzeCurrentPathAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshDrivesAsync()
    {
        var drives = await _storageService.GetDrivesAsync();
        Drives.Clear();
        foreach (var d in drives)
        {
            Drives.Add(d);
        }

        if (SelectedDrive == null && Drives.Count > 0)
        {
            SelectedDrive = Drives.FirstOrDefault(d => d.IsSystemDrive) ?? Drives[0];
        }
    }

    [RelayCommand]
    public async Task RefreshRecycleBinAsync()
    {
        var rb = await _storageService.GetRecycleBinInfoAsync();
        RecycleBinSize = rb.SizeFormatted;
        RecycleBinItemsCount = rb.ItemCount;
    }

    [RelayCommand]
    public async Task RefreshCleanupCandidatesAsync()
    {
        var candidates = await _storageService.GetCleanupCandidatesAsync();
        CleanupItems.Clear();
        foreach (var c in candidates)
        {
            CleanupItems.Add(c);
        }

        CleanupCandidatesCount = candidates.Count;
        var totalBytes = candidates.Sum(x => x.SizeBytes);
        CleanupSummaryText = $"{candidates.Sum(x => x.ItemCount)} temporary files found ({FormatBytes(totalBytes)}). Ready for safe removal.";
    }

    [RelayCommand]
    public void SelectDrive(DiskDriveInfo? drive)
    {
        if (drive == null) return;
        SelectedDrive = drive;
        var driveRoot = drive.DriveLetter.TrimEnd('\\') + "\\";
        CurrentAnalyzedPath = driveRoot;
        _ = AnalyzeCurrentPathAsync();
    }

    [RelayCommand]
    public void SelectViewMode(string mode)
    {
        SelectedViewMode = mode;
    }

    partial void OnIncludeHiddenAndSystemChanged(bool value)
    {
        _ = AnalyzeCurrentPathAsync();
    }

    [RelayCommand]
    public async Task AnalyzeCurrentPathAsync()
    {
        _treeScanCts?.Cancel();
        _treeScanCts = new CancellationTokenSource();
        var token = _treeScanCts.Token;

        IsTreeScanning = true;
        ScanStatusText = $"Scanning '{CurrentAnalyzedPath}' (including hidden & system files)...";

        try
        {
            UpdateBreadcrumbs();

            var items = await _storageService.AnalyzeDirectoryContentsAsync(
                CurrentAnalyzedPath,
                includeHiddenAndSystem: IncludeHiddenAndSystem,
                cancellationToken: token);

            if (token.IsCancellationRequested) return;

            TreeItems.Clear();
            foreach (var item in items)
            {
                TreeItems.Add(item);
            }

            long totalBytes = items.Sum(x => x.SizeBytes);
            TotalAnalyzedSizeFormatted = FormatBytes(totalBytes);
            TotalAnalyzedItemCount = items.Count;
            ScanStatusText = $"Scanned {items.Count} items in {CurrentAnalyzedPath} ({TotalAnalyzedSizeFormatted} total allocation)";
        }
        catch (OperationCanceledException)
        {
            ScanStatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            ScanStatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsTreeScanning = false;
        }
    }

    [RelayCommand]
    public async Task NavigateToFolderAsync(StorageFolderItem? item)
    {
        if (item == null || item.ItemType != StorageItemType.Folder) return;
        if (!Directory.Exists(item.FullPath)) return;

        CurrentAnalyzedPath = item.FullPath;
        await AnalyzeCurrentPathAsync();
    }

    [RelayCommand]
    public async Task NavigateUpAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentAnalyzedPath)) return;

        var parent = Directory.GetParent(CurrentAnalyzedPath);
        if (parent != null && parent.Exists)
        {
            CurrentAnalyzedPath = parent.FullName;
            await AnalyzeCurrentPathAsync();
        }
        else
        {
            // Might be drive root
            var root = Path.GetPathRoot(CurrentAnalyzedPath);
            if (!string.IsNullOrEmpty(root) && !CurrentAnalyzedPath.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                CurrentAnalyzedPath = root;
                await AnalyzeCurrentPathAsync();
            }
        }
    }

    [RelayCommand]
    public async Task QuickJumpAsync(string target)
    {
        string? targetPath = target switch
        {
            "DriveRoot" => SelectedDrive?.DriveLetter.TrimEnd('\\') + "\\" ?? @"C:\",
            "Users" => @"C:\Users",
            "ProgramFiles" => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ProgramData" => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Windows" => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "AppData" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp" => Path.GetTempPath(),
            _ => null
        };

        if (!string.IsNullOrEmpty(targetPath) && Directory.Exists(targetPath))
        {
            CurrentAnalyzedPath = targetPath;
            await AnalyzeCurrentPathAsync();
        }
    }

    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Clear();
        var path = CurrentAnalyzedPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            CanNavigateUp = false;
            return;
        }

        var root = Path.GetPathRoot(path);
        CanNavigateUp = !string.Equals(path.TrimEnd('\\'), root?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        var parts = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            Breadcrumbs.Add(p);
        }
    }

    [RelayCommand]
    public void OpenFileLocation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (File.Exists(path))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", $"\"{path}\"");
            }
        }
        catch (Exception ex)
        {
            _mainViewModel.ShowInfoBar($"Failed to open Explorer: {ex.Message}", "Error");
        }
    }

    [RelayCommand]
    public async Task EmptyRecycleBinAsync()
    {
        var res = await _storageService.EmptyRecycleBinAsync();
        if (res.IsSuccess)
        {
            RecycleBinSize = "0 B";
            RecycleBinItemsCount = 0;
            _mainViewModel.ShowInfoBar(res.Message, "Success");
        }
        else
        {
            _mainViewModel.ShowInfoBar(res.Message, "Error");
        }
    }

    [RelayCommand]
    public async Task RunDryRunSimulationAsync()
    {
        IsCleaning = true;
        try
        {
            var res = await _storageService.ExecuteCleanupAsync(CleanupItems, isDryRun: true);
            _mainViewModel.ShowInfoBar(res.Message, "Informational");
        }
        finally
        {
            IsCleaning = false;
        }
    }

    [RelayCommand]
    public async Task ExecuteCleanupAsync()
    {
        IsCleaning = true;
        try
        {
            var res = await _storageService.ExecuteCleanupAsync(CleanupItems, isDryRun: false);
            _mainViewModel.ShowInfoBar(res.Message, "Success");
            await RefreshCleanupCandidatesAsync();
            await RefreshRecycleBinAsync();
            await RefreshDrivesAsync();
        }
        finally
        {
            IsCleaning = false;
        }
    }

    [RelayCommand]
    public void ToggleSelectAllCleanup()
    {
        bool anyUnselected = CleanupItems.Any(c => !c.IsSelected);
        foreach (var c in CleanupItems)
        {
            c.IsSelected = anyUnselected;
        }
    }

    [RelayCommand]
    public async Task ScanLargeFilesAsync()
    {
        IsScanning = true;
        try
        {
            var root = SelectedDrive?.DriveLetter.TrimEnd('\\') + "\\" ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var files = await _storageService.FindLargeFilesAsync(root, minSizeBytes: 50 * 1024 * 1024, maxResults: 30);
            LargeFiles.Clear();
            foreach (var f in files)
            {
                LargeFiles.Add(f);
            }

            _mainViewModel.ShowInfoBar($"Identified {files.Count} large files exceeding 50 MB on {root}.", "Informational");
        }
        finally
        {
            IsScanning = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _treeScanCts?.Cancel();
        _treeScanCts?.Dispose();
        _autoRefreshTimer?.Dispose();
    }
}
