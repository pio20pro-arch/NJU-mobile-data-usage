[README.md](https://github.com/user-attachments/files/25816628/README.md)
# NjuTrayApp

Yet another vibecoding application.

This app is intended for nju mobile users who have a subscription service.

Windows tray application in C# (.NET 8 / WinForms) for monitoring remaining data packages for nju numbers.

## Features

- Terminal login (`username` + `password`) to IAM API.
- Automatic token refresh (`access_token` / `refresh_token`).
- Group and product fetch for all members.
- Remaining data calculation from `OPL Data Asset (monthly)` (`balances.currentValue`).
- Background refresh every 1 minute (configurable).
- Main tray icon with total usage.
- Right-click menu with:
  - numbers list (`MB | GB`),
  - `Zaloguj`,
  - `Ikonki numerow w tray`,
  - `Ukrywaj sekrety w logach`,
  - `Autostart z Windows`,
  - `Zmien API key`,
  - `Wyjdz`.
- Optional extra tray icons per number (GB on icon).
- Secure persistence of login + tokens using Windows DPAPI (`CurrentUser`) with auto-login after restart.
- Detailed HTTP request/response logging.

## Requirements

- Windows 10/11
- .NET SDK 8.0+

## Run

```powershell
dotnet build "VSCODE Workspace.sln"
dotnet run --project ".\NjuTrayApp\NjuTrayApp.csproj"
```

## Configuration

Config file:

`%AppData%\NjuTrayApp\config.json`

Important fields:

- `apiKey`
- `refreshIntervalSeconds`
- `perNumberTrayIconsEnabled`
- `hideSecretsInLogs`

On first run, the app writes default `apiKey`.

## Logs

Location:

`%AppData%\NjuTrayApp\logs\`

Contains:

- `HTTP-REQUEST` (method, URL, headers, body)
- `HTTP-RESPONSE` (status, headers, body)

If `Ukrywaj sekrety w logach` is enabled, sensitive values (login, password, tokens, auth/api keys) are masked as `***`.

On startup, logs older than today are removed.
