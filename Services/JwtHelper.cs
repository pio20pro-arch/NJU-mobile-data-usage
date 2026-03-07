using System.Text;
using System.Text.Json;

namespace NjuTrayApp.Services;

public static class JwtHelper
{
    public static DateTimeOffset? TryGetExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var bytes = Convert.FromBase64String(payload);
            using var document = JsonDocument.Parse(bytes);

            if (!document.RootElement.TryGetProperty("exp", out var expElement))
            {
                return null;
            }

            var expUnix = expElement.GetInt64();
            return DateTimeOffset.FromUnixTimeSeconds(expUnix);
        }
        catch
        {
            return null;
        }
    }
}
