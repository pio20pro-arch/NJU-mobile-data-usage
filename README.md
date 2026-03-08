<img width="417" height="247" alt="Screenshot 2026-03-07 184339" src="https://github.com/user-attachments/assets/1ba2ebe7-bfc0-4859-b2f7-d263a120c5de" /><img width="544" height="424" alt="Screenshot 2026-03-07 184416" src="https://github.com/user-attachments/assets/4eb80098-206a-4cb0-9704-1d634d4cc23b" />



##Yet another vibecoding application ;)

This app is intended for nju mobile users who have a subscription service (https://www.njumobile.pl/oferta/nju-z-subskrypcja).

Support for other accounts like prepaid or mobile internet: https://github.com/pio20pro-arch/Nju-mobile-prepaid-status

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
cd .\NjuTrayApp\
dotnet build
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

## Opis po polsku

To jest aplikacja typu tray dla systemu Windows napisana w C# (.NET 8 / WinForms), przeznaczona dla uzytkownikow nju mobile z usluga subskrypcji (https://www.njumobile.pl/oferta/nju-z-subskrypcja).

Wsparcie dla innych kont typu sybskrypcja lub mobilny internet: https://github.com/pio20pro-arch/Nju-mobile-prepaid-status

### Funkcje

- Logowanie przez terminal (`username` + `password`) do API IAM.
- Automatyczne odswiezanie tokenow (`access_token` / `refresh_token`).
- Pobieranie grup i produktow dla wszystkich memberow.
- Wyliczanie pozostalych danych z `OPL Data Asset (monthly)` (`balances.currentValue`).
- Odswiezanie w tle co 1 minute (konfigurowalne).
- Glowna ikona tray z suma danych.
- Menu po prawym kliku:
  - lista numerow (`MB | GB`),
  - `Zaloguj`,
  - `Ikonki numerow w tray`,
  - `Ukrywaj sekrety w logach`,
  - `Autostart z Windows`,
  - `Zmien API key`,
  - `Wyjdz`.
- Opcjonalne dodatkowe ikonki tray dla kazdego numeru (GB na ikonie).
- Bezpieczna persystencja loginu i tokenow przez Windows DPAPI (`CurrentUser`) oraz autologowanie po restarcie.
- Szczegolowe logi HTTP request/response.

### Wymagania

- Windows 10/11
- .NET SDK 8.0+

### Uruchomienie

```powershell
cd .\NjuTrayApp\
dotnet build
dotnet run --project ".\NjuTrayApp\NjuTrayApp.csproj"
```

### Konfiguracja

Plik konfiguracyjny:

`%AppData%\NjuTrayApp\config.json`

Wazne pola:

- `apiKey`
- `refreshIntervalSeconds`
- `perNumberTrayIconsEnabled`
- `hideSecretsInLogs`

Przy pierwszym uruchomieniu aplikacja zapisuje domyslny `apiKey`.

### Logi

Lokalizacja:

`%AppData%\NjuTrayApp\logs\`

Log zawiera:

- `HTTP-REQUEST` (metoda, URL, naglowki, body)
- `HTTP-RESPONSE` (status, naglowki, body)

Po wlaczeniu opcji `Ukrywaj sekrety w logach` wrazliwe dane (login, haslo, tokeny, auth/api key) sa maskowane jako `***`.

Przy starcie aplikacja usuwa logi starsze niz dzisiejszy.
