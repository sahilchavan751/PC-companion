using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Environment;
using OmniControl.Core.Models.Search;

namespace OmniControl.Infrastructure.Search;

public class GlobalSearchService : IGlobalSearchService
{
    private readonly IProcessManagerService _processService;
    private readonly IWindowsServicesService _serviceService;
    private readonly IStartupManagerService _startupService;
    private readonly IEnvironmentManagerService _envService;

    private static readonly List<GlobalSearchResult> StaticNavigationItems = new()
    {
        new() { Title = "System Overview", Subtitle = "Dashboard, CPU, RAM, GPU, storage overview", Category = SearchCategory.Navigation, NavigationTargetTag = "Overview", IconGlyph = "\uE80F" },
        new() { Title = "Performance Control", Subtitle = "Power plans, power profiles, frequency monitoring", Category = SearchCategory.Navigation, NavigationTargetTag = "Performance", IconGlyph = "\uECAD" },
        new() { Title = "Process Manager", Subtitle = "Task manager, process list, resource usage, priorities", Category = SearchCategory.Navigation, NavigationTargetTag = "Processes", IconGlyph = "\uE9D9" },
        new() { Title = "Startup Manager", Subtitle = "Manage startup applications and scheduled login tasks", Category = SearchCategory.Navigation, NavigationTargetTag = "Startup", IconGlyph = "\uE7B5" },
        new() { Title = "Environment Variables", Subtitle = "Edit system & user environment variables, PATH manager", Category = SearchCategory.Navigation, NavigationTargetTag = "Environment", IconGlyph = "\uE7BE" },
        new() { Title = "Windows Services", Subtitle = "Start, stop, restart, change startup type for services", Category = SearchCategory.Navigation, NavigationTargetTag = "Services", IconGlyph = "\uE90F" },
        new() { Title = "Hardware & Sensors", Subtitle = "Temperatures, fan speeds, clock frequencies, motherboard", Category = SearchCategory.Navigation, NavigationTargetTag = "Hardware", IconGlyph = "\uE950" },
        new() { Title = "Audio & Display Control", Subtitle = "Master volume, per-app audio mixer, monitor brightness", Category = SearchCategory.Navigation, NavigationTargetTag = "AudioDisplay", IconGlyph = "\uE767" },
        new() { Title = "Network Manager", Subtitle = "Network adapters, live throughput, ping & DNS tests", Category = SearchCategory.Navigation, NavigationTargetTag = "Network", IconGlyph = "\uE839" },
        new() { Title = "Storage Manager", Subtitle = "Drive analysis, temporary file cleanup, large files", Category = SearchCategory.Navigation, NavigationTargetTag = "Storage", IconGlyph = "\uEDA2" },
        new() { Title = "Security Center", Subtitle = "Windows Defender, Firewall, and UAC status", Category = SearchCategory.Navigation, NavigationTargetTag = "Security", IconGlyph = "\uEA18" },
        new() { Title = "Operation History", Subtitle = "Audit log of all system changes and backup snapshots", Category = SearchCategory.Navigation, NavigationTargetTag = "History", IconGlyph = "\uE81C" },
        new() { Title = "Settings", Subtitle = "Themes, polling intervals, minimize to tray", Category = SearchCategory.Navigation, NavigationTargetTag = "Settings", IconGlyph = "\uE713" }
    };

    public GlobalSearchService(
        IProcessManagerService processService,
        IWindowsServicesService serviceService,
        IStartupManagerService startupService,
        IEnvironmentManagerService envService)
    {
        _processService = processService;
        _serviceService = serviceService;
        _startupService = startupService;
        _envService = envService;
    }

    public async Task<List<GlobalSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<GlobalSearchResult>();

        var q = query.Trim();
        var results = new List<GlobalSearchResult>();

        // 1. Navigation items
        foreach (var nav in StaticNavigationItems)
        {
            if (nav.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || nav.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(nav);
            }
        }

        // 2. Processes (top matches)
        try
        {
            var processes = await _processService.GetProcessesAsync(cancellationToken);
            var processMatches = processes
                .Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Id.ToString() == q)
                .Take(5);

            foreach (var p in processMatches)
            {
                results.Add(new GlobalSearchResult
                {
                    Title = $"{p.Name} (PID: {p.Id})",
                    Subtitle = $"Process • CPU: {p.CpuFormatted} • RAM: {p.MemoryFormatted}",
                    Category = SearchCategory.Process,
                    NavigationTargetTag = "Processes",
                    IconGlyph = "\uE9D9",
                    TargetItem = p
                });
            }
        }
        catch
        {
            // Suppress
        }

        // 3. Windows Services
        try
        {
            var services = await _serviceService.GetServicesAsync(cancellationToken);
            var serviceMatches = services
                .Where(s => s.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) || s.ServiceName.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(5);

            foreach (var s in serviceMatches)
            {
                results.Add(new GlobalSearchResult
                {
                    Title = s.DisplayName,
                    Subtitle = $"Service: {s.ServiceName} • State: {s.State} • Mode: {s.StartMode}",
                    Category = SearchCategory.Service,
                    NavigationTargetTag = "Services",
                    IconGlyph = "\uE90F",
                    TargetItem = s
                });
            }
        }
        catch
        {
            // Suppress
        }

        // 4. Startup entries
        try
        {
            var startups = await _startupService.GetStartupEntriesAsync(cancellationToken);
            var startupMatches = startups
                .Where(st => st.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || st.Command.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(4);

            foreach (var st in startupMatches)
            {
                results.Add(new GlobalSearchResult
                {
                    Title = st.Name,
                    Subtitle = $"Startup App • Status: {(st.IsEnabled ? "Enabled" : "Disabled")} • Scope: {st.Scope}",
                    Category = SearchCategory.StartupApp,
                    NavigationTargetTag = "Startup",
                    IconGlyph = "\uE7B5",
                    TargetItem = st
                });
            }
        }
        catch
        {
            // Suppress
        }

        // 5. Environment Variables
        try
        {
            var userVars = await _envService.GetVariablesAsync(EnvironmentVariableScope.User, cancellationToken);
            var sysVars = await _envService.GetVariablesAsync(EnvironmentVariableScope.Machine, cancellationToken);

            var envMatches = userVars.Concat(sysVars)
                .Where(v => v.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || v.Value.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(4);

            foreach (var v in envMatches)
            {
                results.Add(new GlobalSearchResult
                {
                    Title = v.Name,
                    Subtitle = $"Environment Variable ({v.Scope}) = {Truncate(v.Value, 60)}",
                    Category = SearchCategory.EnvironmentVariable,
                    NavigationTargetTag = "Environment",
                    IconGlyph = "\uE7BE",
                    TargetItem = v
                });
            }
        }
        catch
        {
            // Suppress
        }

        return results;
    }

    private static string Truncate(string str, int maxLen)
        => str.Length <= maxLen ? str : str[..maxLen] + "...";
}
