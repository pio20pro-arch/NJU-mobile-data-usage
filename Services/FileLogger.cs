using System.Text;
using System.Text.RegularExpressions;

namespace NjuTrayApp.Services;

public sealed class FileLogger
{
    private readonly object _sync = new();
    private readonly string _logsDirectory;
    private bool _hideSecretsInLogs;

    public FileLogger()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logsDirectory = Path.Combine(appData, "NjuTrayApp", "logs");
        Directory.CreateDirectory(_logsDirectory);
        CleanupOldLogs();
        Info("=== DEBUG LOG STARTED ===");
    }

    public void Info(string message) => Write("INFO", message);
    public void Error(string message) => Write("ERROR", message);
    public void DebugPayload(string tag, string payload) => Write("DEBUG", $"{tag}{Environment.NewLine}{payload}");
    public void Debug(string message) => Write("DEBUG", message);
    public void SetHideSecretsInLogs(bool enabled) => _hideSecretsInLogs = enabled;

    public void LogHttpRequest(HttpMethod method, string url, IEnumerable<KeyValuePair<string, string>>? headers = null, string? body = null)
    {
        var command = new StringBuilder()
            .Append("xpire-X ")
            .Append(method.Method)
            .Append(" '")
            .Append(EscapeSingleQuotes(url))
            .Append("'");

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                command
                    .Append(" -H '")
                    .Append(EscapeSingleQuotes(header.Key))
                    .Append(": ")
                    .Append(EscapeSingleQuotes(header.Value))
                    .Append("'");
            }
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            command
                .Append(" --data '")
                .Append(EscapeSingleQuotes(body))
                .Append("'");
        }

        Write("HTTP-REQUEST", command.ToString());
    }

    public void LogHttpResponse(
        string url,
        int statusCode,
        IEnumerable<KeyValuePair<string, string>>? headers,
        string body)
    {
        var builder = new StringBuilder()
            .AppendLine($"URL: {url}")
            .AppendLine($"Status: {statusCode}");

        if (headers is not null)
        {
            builder.AppendLine("Headers:");
            foreach (var header in headers)
            {
                builder.AppendLine($"{header.Key}: {header.Value}");
            }
        }

        builder.AppendLine("Body:")
            .AppendLine(body)
            .Append("---");

        Write("HTTP-RESPONSE", builder.ToString());
    }

    private void Write(string level, string message)
    {
        WriteRaw($"[{level}] {message}");
    }

    private void WriteRaw(string message)
    {
        try
        {
            lock (_sync)
            {
                var filePath = Path.Combine(_logsDirectory, $"njutrayapp-{DateTime.Now:yyyyMMdd}.log");
                var safeMessage = _hideSecretsInLogs ? MaskSecrets(message) : message;
                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" ")
                    .AppendLine(safeMessage)
                    .ToString();
                File.AppendAllText(filePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Ignore logger failures to avoid crashing background app.
        }
    }

    private static string EscapeSingleQuotes(string value)
    {
        return value.Replace("'", "''");
    }

    private void CleanupOldLogs()
    {
        try
        {
            var todayName = $"njutrayapp-{DateTime.Now:yyyyMMdd}.log";
            foreach (var file in Directory.GetFiles(_logsDirectory, "njutrayapp-*.log"))
            {
                if (!string.Equals(Path.GetFileName(file), todayName, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static string MaskSecrets(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var output = input;

        // Form-url-encoded values in request body.
        output = Regex.Replace(
            output,
            @"(?i)(^|[&\s'\?])(username|login|password|access_token|refresh_token|id_token|token|client_secret|apikey)=([^&\s']+)",
            m => $"{m.Groups[1].Value}{m.Groups[2].Value}=***");

        // JSON key-value pairs.
        output = Regex.Replace(
            output,
            "(?i)\"(username|login|password|access_token|refresh_token|id_token|token|client_secret|apikey)\"\\s*:\\s*\"[^\"]*\"",
            m => $"\"{m.Groups[1].Value}\":\"***\"");

        // Authorization headers.
        output = Regex.Replace(output, @"(?im)(Authorization\s*:\s*Bearer\s+)[^\r\n]+", "$1***");
        output = Regex.Replace(output, @"(?im)(apikey\s*:\s*)[^\r\n]+", "$1***");

        return output;
    }
}
