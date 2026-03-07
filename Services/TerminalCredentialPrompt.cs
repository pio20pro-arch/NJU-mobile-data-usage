using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using NjuTrayApp.Models;

namespace NjuTrayApp.Services;

public static class TerminalCredentialPrompt
{
    public static async Task<LoginCredentials?> PromptAsync(FileLogger logger, CancellationToken cancellationToken)
    {
        logger.Info("Opening terminal credential prompt.");
        var tempDirectory = Path.Combine(Path.GetTempPath(), "NjuTrayApp");
        Directory.CreateDirectory(tempDirectory);

        var outputPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.json");
        var scriptPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.ps1");

        var escapedOutputPath = outputPath.Replace("'", "''");
        var script =
            "$u = Read-Host \"username\"" + Environment.NewLine +
            "$s = Read-Host \"password\" -AsSecureString" + Environment.NewLine +
            "$p = [System.Net.NetworkCredential]::new('', $s).Password" + Environment.NewLine +
            "@{ username = $u; password = $p } | ConvertTo-Json -Compress | Set-Content -LiteralPath '" + escapedOutputPath + "' -Encoding UTF8";
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });

            if (process is null)
            {
                logger.Error("Failed to start terminal login process.");
                return null;
            }

            await process.WaitForExitAsync(cancellationToken);
            logger.Info($"Terminal credential process exited with code {process.ExitCode}.");

            if (!File.Exists(outputPath))
            {
                logger.Error("Terminal login did not produce credentials output.");
                return null;
            }

            var json = await File.ReadAllTextAsync(outputPath, cancellationToken);
            logger.Debug($"Terminal credential raw payload: {json}");
            var dto = JsonSerializer.Deserialize<CredentialDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (dto is null || string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
            {
                logger.Error("Credentials JSON parsed but username/password are empty.");
                return null;
            }

            return new LoginCredentials
            {
                Username = dto.Username,
                Password = dto.Password
            };
        }
        finally
        {
            TryDelete(scriptPath);
            TryDelete(outputPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private sealed class CredentialDto
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }
}
