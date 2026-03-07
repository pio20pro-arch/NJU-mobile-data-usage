namespace NjuTrayApp.Models;

public sealed class AppConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public int RefreshIntervalSeconds { get; set; } = 60;
    public bool PerNumberTrayIconsEnabled { get; set; }
    public bool HideSecretsInLogs { get; set; }
}
