namespace OmniControl.Core.Models.Security;

public class SecurityStatusInfo
{
    public bool AntivirusEnabled { get; set; }
    public string AntivirusProductName { get; set; } = "Windows Defender";
    public bool AntivirusUpToDate { get; set; } = true;
    public bool FirewallEnabled { get; set; }
    public string FirewallStatusText { get; set; } = "Active";
    public bool UacEnabled { get; set; } = true;
    public string UacLevelText { get; set; } = "Notify when apps try to make changes";
    public bool SecureBootEnabled { get; set; }
    public bool BitLockerActive { get; set; }
    public string WindowsUpdateStatus { get; set; } = "Up to date";
    public List<string> SecurityRecommendations { get; set; } = new();
}
