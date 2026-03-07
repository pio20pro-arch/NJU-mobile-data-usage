namespace NjuTrayApp.Models;

public sealed class PersistedAuthData
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public PersistedTokenData? Tokens { get; set; }
}

public sealed class PersistedTokenData
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset AccessExpiresAt { get; set; }
    public DateTimeOffset RefreshExpiresAt { get; set; }
}
