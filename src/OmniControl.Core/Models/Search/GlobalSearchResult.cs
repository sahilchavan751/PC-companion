namespace OmniControl.Core.Models.Search;

public enum SearchCategory
{
    Navigation,
    Process,
    Service,
    StartupApp,
    EnvironmentVariable,
    Hardware,
    Setting,
    Action
}

public class GlobalSearchResult
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "\uE721"; // Search icon default
    public SearchCategory Category { get; set; }
    public string NavigationTargetTag { get; set; } = string.Empty;
    public Action? DirectAction { get; set; }
    public object? TargetItem { get; set; }
}
