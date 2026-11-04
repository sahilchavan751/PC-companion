namespace OmniControl.Core.Models.Storage;

public enum StorageItemType
{
    Folder,
    File
}

public class StorageFolderItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public StorageItemType ItemType { get; set; } = StorageItemType.Folder;
    public bool IsFolder => ItemType == StorageItemType.Folder;
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public int SubFolderCount { get; set; }
    public double PercentOfParent { get; set; }
    public bool IsHidden { get; set; }
    public bool IsSystem { get; set; }
    public bool HasAccessDenied { get; set; }

    public string SizeFormatted => FormatBytes(SizeBytes);
    public string PercentFormatted => $"{PercentOfParent:F1}%";

    public string DetailsText => ItemType == StorageItemType.Folder
        ? (HasAccessDenied ? "Access restricted" : $"{FileCount:N0} files, {SubFolderCount:N0} folders")
        : "File";

    public string Glyph => ItemType == StorageItemType.Folder
        ? (IsSystem ? "\uE8B7" : (IsHidden ? "\uED43" : "\uED25"))
        : (IsSystem ? "\uE770" : "\uE8A5");

    public string TagText => (IsHidden, IsSystem) switch
    {
        (true, true) => "SYSTEM + HIDDEN",
        (true, false) => "HIDDEN",
        (false, true) => "SYSTEM",
        _ => string.Empty
    };

    public bool HasTag => !string.IsNullOrEmpty(TagText);

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
}

public class StorageCategoryBreakdown
{
    public string CategoryName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#00D4FF";
    public string SizeFormatted => FormatBytes(SizeBytes);

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
}

public class CleanupCandidateItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int ItemCount { get; set; }

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsSafeToDelete { get; set; } = true;
    public string SizeFormatted => FormatBytes(SizeBytes);

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
}

public class LargeFileItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string SizeFormatted => FormatBytes(SizeBytes);

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
}

public class RecycleBinInfo
{
    public long TotalSizeBytes { get; set; }
    public long ItemCount { get; set; }
    public string SizeFormatted => FormatBytes(TotalSizeBytes);

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
}
