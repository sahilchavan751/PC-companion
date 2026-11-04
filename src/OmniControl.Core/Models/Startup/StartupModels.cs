namespace OmniControl.Core.Models.Startup;

public enum StartupScope
{
    CurrentUser,
    LocalMachine
}

public enum StartupSourceType
{
    RegistryRun,
    RegistryRunOnce,
    StartupFolder,
    TaskScheduler
}

public class StartupEntry : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public StartupScope Scope { get; set; }
    public StartupSourceType SourceType { get; set; }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string Impact { get; set; } = "Low";
    public bool IsProtectedSystemEntry { get; set; }
    public string RegistryKeyPath { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;

    private string? _iconPath;
    public string? IconPath
    {
        get => _iconPath;
        set
        {
            if (SetProperty(ref _iconPath, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => !string.IsNullOrEmpty(IconPath);
}
