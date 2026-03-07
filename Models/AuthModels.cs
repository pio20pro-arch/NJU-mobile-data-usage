using System.Text.Json.Serialization;

namespace NjuTrayApp.Models;

public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_expires_in")]
    public int RefreshExpiresIn { get; set; }
}

public sealed class TokenState
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset AccessExpiresAt { get; init; }
    public required DateTimeOffset RefreshExpiresAt { get; init; }
}

public sealed class LoginCredentials
{
    public required string Username { get; init; }
    public required string Password { get; init; }
}
