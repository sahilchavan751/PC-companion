using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Models.Hardware;

namespace OmniControl.App.ViewModels;

public partial class HardwareDeviceGroup : ObservableObject
{
    public string DeviceKey { get; set; } = string.Empty;
    public string DeviceCategory { get; set; } = "SYSTEM"; // CPU, GPU, STORAGE, MOTHERBOARD
    public string DeviceName { get; set; } = string.Empty;
    public string Glyph { get; set; } = "\uE950";

    [ObservableProperty]
    private string _primaryMetricValue = "--";

    [ObservableProperty]
    private string _primaryMetricLabel = "Package Temperature";

    [ObservableProperty]
    private string _statusColor = "#00E676";

    [ObservableProperty]
    private string _statusText = "Normal";

    public ObservableCollection<SensorMetric> PrimarySensors { get; } = new();
    public ObservableCollection<SensorMetric> DetailSensors { get; } = new();

    [ObservableProperty]
    private bool _hasDetails;

    [ObservableProperty]
    private bool _showDetails;

    [ObservableProperty]
    private string _toggleButtonText = "Show Core Details";

    [RelayCommand]
    private void ToggleDetails()
    {
        ShowDetails = !ShowDetails;
        ToggleButtonText = ShowDetails ? "Hide Core Details" : "Show Core Details";
    }
}
