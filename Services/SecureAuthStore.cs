using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NjuTrayApp.Models;

namespace NjuTrayApp.Services;

public sealed class SecureAuthStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NjuTrayApp.Auth.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly FileLogger _logger;
    private readonly string _filePath;

    public SecureAuthStore(FileLogger logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, "NjuTrayApp");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "auth.sec");
    }

    public PersistedAuthData? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var encryptedBytes = File.ReadAllBytes(_filePath);
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decryptedBytes);
            return JsonSerializer.Deserialize<PersistedAuthData>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.Error($"Secure auth load failed: {ex.Message}");
            return null;
        }
    }

    public void Save(PersistedAuthData data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_filePath, encryptedBytes);
        }
        catch (Exception ex)
        {
            _logger.Error($"Secure auth save failed: {ex.Message}");
        }
    }
}
