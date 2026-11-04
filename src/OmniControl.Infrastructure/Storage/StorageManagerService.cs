using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Hardware;
using OmniControl.Core.Models.Storage;
using OmniControl.Core.Results;
using OmniControl.Infrastructure.WindowsInterop;

namespace OmniControl.Infrastructure.Storage;

public class StorageManagerService : IStorageManagerService
{
    public Task<List<DiskDriveInfo>> GetDrivesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var list = new List<DiskDriveInfo>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;

                list.Add(new DiskDriveInfo
                {
                    DriveLetter = drive.Name.TrimEnd('\\'),
                    VolumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                    FileSystem = drive.DriveFormat,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace,
                    IsSystemDrive = drive.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase)
                });
            }
            return list;
        }, cancellationToken);
    }

    public async Task<List<StorageCategoryBreakdown>> GetCategoryBreakdownAsync(string driveLetter = "C:", CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var userProfile = global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.UserProfile);
            var categories = new List<StorageCategoryBreakdown>();

            // Calculate key user categories
            AddCategoryIfDir(categories, "Downloads", Path.Combine(userProfile, "Downloads"), "#0078D4");
            AddCategoryIfDir(categories, "Documents", global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.MyDocuments), "#107C41");
            AddCategoryIfDir(categories, "Temporary Files", Path.GetTempPath(), "#D83B01");
            AddCategoryIfDir(categories, "AppData Local", global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData), "#8764B8");
            AddCategoryIfDir(categories, "Pictures & Media", global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.MyPictures), "#00B7C3");

            return categories;
        }, cancellationToken);
    }

    private static void AddCategoryIfDir(List<StorageCategoryBreakdown> list, string name, string path, string colorHex)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var dir = new DirectoryInfo(path);
                long size = 0;
                int count = 0;

                foreach (var file in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    size += file.Length;
                    count++;
                }

                list.Add(new StorageCategoryBreakdown
                {
                    CategoryName = name,
                    SizeBytes = size,
                    FileCount = count,
                    Description = path,
                    ColorHex = colorHex
                });
            }
        }
        catch
        {
            // Suppress access issues
        }
    }

    public async Task<List<CleanupCandidateItem>> GetCleanupCandidatesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var candidates = new List<CleanupCandidateItem>();

            // 1. User Temp Files
            var userTemp = Path.GetTempPath();
            AddCandidate(candidates, "User Temporary Files", "Scratch application data, extracted installers, and caches", userTemp);

            // 2. Windows Temp
            var winTemp = @"C:\Windows\Temp";
            AddCandidate(candidates, "System Temporary Files", "Windows update cache and system installer scratch files", winTemp);

            // 3. Windows Update Download Cache
            var updateCache = @"C:\Windows\SoftwareDistribution\Download";
            AddCandidate(candidates, "Windows Update Downloads", "Downloaded update patches already staged or installed", updateCache);

            // 4. User Crash Reports
            var crashReports = Path.Combine(global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData), @"CrashDumps");
            AddCandidate(candidates, "Application Crash Dumps", "Diagnostic crash memory dumps from past application errors", crashReports);

            // 5. Windows Error Reporting
            var werQueue = @"C:\ProgramData\Microsoft\Windows\WER\ReportQueue";
            AddCandidate(candidates, "Windows Error Reports", "Pending error telemetry reports ready for purging", werQueue);

            // 6. DirectX Shader Cache
            var dxCache = Path.Combine(global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData), @"D3DSCache");
            AddCandidate(candidates, "DirectX Shader Cache", "Precompiled graphics shader caches", dxCache);

            // 7. Thumbnail Cache
            var thumbCache = Path.Combine(global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
            AddCandidate(candidates, "Explorer Thumbnail Cache", "Cached image and icon thumbnails in local user profile", thumbCache);

            return candidates;
        }, cancellationToken);
    }

    private static void AddCandidate(List<CleanupCandidateItem> list, string title, string desc, string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var dir = new DirectoryInfo(path);
                long bytes = 0;
                int count = 0;

                foreach (var f in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    bytes += f.Length;
                    count++;
                }

                list.Add(new CleanupCandidateItem
                {
                    Title = title,
                    Description = desc,
                    Path = path,
                    SizeBytes = bytes,
                    ItemCount = count,
                    IsSelected = bytes > 0,
                    IsSafeToDelete = true
                });
            }
        }
        catch
        {
            // Suppress
        }
    }

    public async Task<OperationResult> ExecuteCleanupAsync(IEnumerable<CleanupCandidateItem> candidates, bool isDryRun = false, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            long freedBytes = 0;
            int deletedFiles = 0;

            foreach (var item in candidates.Where(c => c.IsSelected && c.IsSafeToDelete))
            {
                if (cancellationToken.IsCancellationRequested) break;

                if (!Directory.Exists(item.Path)) continue;

                var dir = new DirectoryInfo(item.Path);
                foreach (var file in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    if (isDryRun)
                    {
                        freedBytes += file.Length;
                        deletedFiles++;
                        continue;
                    }

                    try
                    {
                        var len = file.Length;
                        file.Delete();
                        freedBytes += len;
                        deletedFiles++;
                    }
                    catch
                    {
                        // File is currently locked by a running process, safe to skip
                    }
                }
            }

            var formatted = FormatBytes(freedBytes);
            return isDryRun
                ? OperationResult.Ok($"Dry-run simulation: {deletedFiles} files ({formatted}) eligible for cleanup.")
                : OperationResult.Ok($"Cleanup completed: Safely removed {deletedFiles} temporary files, freeing {formatted}.");
        }, cancellationToken);
    }

    public Task<RecycleBinInfo> GetRecycleBinInfoAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var info = new NativeMethods.SHQUERYRBINFO
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.SHQUERYRBINFO))
            };

            var hr = NativeMethods.SHQueryRecycleBin(null, ref info);
            if (hr == 0)
            {
                return new RecycleBinInfo
                {
                    TotalSizeBytes = info.i64Size,
                    ItemCount = info.i64NumItems
                };
            }

            return new RecycleBinInfo();
        }, cancellationToken);
    }

    public Task<OperationResult> EmptyRecycleBinAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var res = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND);
            return res == 0
                ? OperationResult.Ok("Recycle Bin was emptied successfully.")
                : OperationResult.Fail($"Failed to empty Recycle Bin. HRESULT: {res:X8}");
        }, cancellationToken);
    }

    public async Task<List<LargeFileItem>> FindLargeFilesAsync(string rootPath, long minSizeBytes = 100 * 1024 * 1024, int maxResults = 25, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<LargeFileItem>();
            if (!Directory.Exists(rootPath))
                return results;

            try
            {
                var dir = new DirectoryInfo(rootPath);
                foreach (var file in dir.EnumerateFiles("*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    MaxRecursionDepth = 4
                }))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    if (file.Length >= minSizeBytes)
                    {
                        results.Add(new LargeFileItem
                        {
                            FilePath = file.FullName,
                            FileName = file.Name,
                            Extension = file.Extension,
                            SizeBytes = file.Length,
                            LastModified = file.LastWriteTime
                        });
                    }
                }
            }
            catch
            {
                // Suppress
            }

            return results.OrderByDescending(f => f.SizeBytes).Take(maxResults).ToList();
        }, cancellationToken);
    }

    public async Task<List<StorageFolderItem>> AnalyzeDirectoryContentsAsync(string folderPath, bool includeHiddenAndSystem = true, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new ConcurrentBag<StorageFolderItem>();
            if (string.IsNullOrWhiteSpace(folderPath))
                return new List<StorageFolderItem>();

            var normalized = folderPath.Trim();
            if (normalized.Length == 2 && normalized[1] == ':')
            {
                normalized += "\\";
            }

            if (!Directory.Exists(normalized))
                return new List<StorageFolderItem>();

            var rootDir = new DirectoryInfo(normalized);

            // 1. Enumerate and analyze Subdirectories in parallel
            try
            {
                var subDirs = rootDir.GetDirectories();

                Parallel.ForEach(subDirs, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = cancellationToken
                }, dir =>
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    bool isHidden = (dir.Attributes & FileAttributes.Hidden) != 0;
                    bool isSystem = (dir.Attributes & FileAttributes.System) != 0;

                    if (!includeHiddenAndSystem && (isHidden || isSystem))
                        return;

                    long dirSizeBytes = 0;
                    int fileCount = 0;
                    int subFolderCount = 0;
                    bool accessDenied = false;

                    try
                    {
                        var options = new EnumerationOptions
                        {
                            IgnoreInaccessible = true,
                            RecurseSubdirectories = true,
                            AttributesToSkip = FileAttributes.ReparsePoint,
                            MaxRecursionDepth = 12
                        };

                        foreach (var f in dir.EnumerateFiles("*", options))
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            dirSizeBytes += f.Length;
                            fileCount++;
                        }

                        subFolderCount = dir.GetDirectories().Length;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        accessDenied = true;
                    }
                    catch
                    {
                        // Folder inspect error
                    }

                    results.Add(new StorageFolderItem
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        ItemType = StorageItemType.Folder,
                        SizeBytes = dirSizeBytes,
                        FileCount = fileCount,
                        SubFolderCount = subFolderCount,
                        IsHidden = isHidden,
                        IsSystem = isSystem,
                        HasAccessDenied = accessDenied
                    });
                });
            }
            catch
            {
                // Top-level directory read error
            }

            // 2. Enumerate Immediate Files in this directory (including system/hidden files like pagefile.sys, hiberfil.sys)
            try
            {
                var files = rootDir.GetFiles();
                foreach (var f in files)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    bool isHidden = (f.Attributes & FileAttributes.Hidden) != 0;
                    bool isSystem = (f.Attributes & FileAttributes.System) != 0;

                    if (!includeHiddenAndSystem && (isHidden || isSystem))
                        continue;

                    results.Add(new StorageFolderItem
                    {
                        Name = f.Name,
                        FullPath = f.FullName,
                        ItemType = StorageItemType.File,
                        SizeBytes = f.Length,
                        FileCount = 1,
                        SubFolderCount = 0,
                        IsHidden = isHidden,
                        IsSystem = isSystem,
                        HasAccessDenied = false
                    });
                }
            }
            catch
            {
                // File enumeration error
            }

            var list = results.ToList();
            long totalBytes = list.Sum(x => x.SizeBytes);
            foreach (var item in list)
            {
                item.PercentOfParent = totalBytes > 0 ? (double)item.SizeBytes / totalBytes * 100.0 : 0.0;
            }

            return list.OrderByDescending(x => x.SizeBytes).ToList();
        }, cancellationToken);
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
