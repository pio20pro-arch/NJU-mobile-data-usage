using NjuTrayApp.Models;
using NjuTrayApp.Services;
using System.Security.Cryptography;
using System.Text;

namespace NjuTrayApp.UI;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _loginMenuItem;
    private readonly ToolStripMenuItem _hideSecretsInLogsMenuItem;
    private readonly ToolStripMenuItem _autostartMenuItem;
    private readonly ToolStripMenuItem _changeApiKeyMenuItem;
    private readonly ToolStripMenuItem _exitMenuItem;
    private readonly ToolStripSeparator _numbersSeparator;

    private readonly ConfigService _configService;
    private readonly AuthService _authService;
    private readonly NjuApiClient _apiClient;
    private readonly AutostartService _autostartService;
    private readonly FileLogger _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly SynchronizationContext _uiContext;

    private AppConfig _config;
    private List<MemberUsage> _currentUsage = [];
    private List<GroupMember>? _cachedActiveMembers;
    private readonly Icon _defaultStatusIcon;
    private Icon? _currentCustomIcon;
    private Task? _backgroundLoopTask;
    private readonly Dictionary<string, NumberTrayIconState> _numberTrayIcons = [];
    private readonly Dictionary<string, bool> _perNumberTrayIconEnabled = new(StringComparer.Ordinal);
    private string? _lastUsageSignature;

    public TrayApplicationContext(
        ConfigService configService,
        AuthService authService,
        NjuApiClient apiClient,
        AutostartService autostartService,
        FileLogger logger)
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _configService = configService;
        _authService = authService;
        _apiClient = apiClient;
        _autostartService = autostartService;
        _logger = logger;
        _defaultStatusIcon = LoadDefaultTrayIcon();
        _config = _configService.Load();
        _logger.SetHideSecretsInLogs(_config.HideSecretsInLogs);
        _perNumberTrayIconEnabled.Clear();
        foreach (var pair in _config.PerNumberTrayIconEnabled ?? new Dictionary<string, bool>())
        {
            _perNumberTrayIconEnabled[pair.Key] = pair.Value;
        }

        _contextMenu = new ContextMenuStrip();
        _loginMenuItem = new ToolStripMenuItem("Zaloguj", null, async (_, _) => await LoginAsync());
        _hideSecretsInLogsMenuItem = new ToolStripMenuItem("Ukrywaj sekrety w logach", null, (_, _) => ToggleHideSecretsInLogs())
        {
            Checked = _config.HideSecretsInLogs,
            CheckOnClick = false
        };
        _autostartMenuItem = new ToolStripMenuItem("Autostart z Windows", null, (_, _) => ToggleAutostart())
        {
            Checked = _autostartService.IsEnabled(),
            CheckOnClick = false
        };
        _changeApiKeyMenuItem = new ToolStripMenuItem("Zmien API key", null, (_, _) => ChangeApiKey());
        _exitMenuItem = new ToolStripMenuItem("Wyjdz", null, (_, _) => ExitApplication());
        _numbersSeparator = new ToolStripSeparator();
        _contextMenu.Items.AddRange([_numbersSeparator, _loginMenuItem, _hideSecretsInLogsMenuItem, _autostartMenuItem, _changeApiKeyMenuItem, _exitMenuItem]);

        _notifyIcon = new NotifyIcon
        {
            Text = "Nju Tray: niezalogowany",
            Icon = _defaultStatusIcon,
            ContextMenuStrip = _contextMenu,
            Visible = true
        };

        _notifyIcon.BalloonTipTitle = "Nju Tray";
        _notifyIcon.DoubleClick += async (_, _) => await LoginAsync();

        _logger.Info("Application started.");
        _logger.Info("Checking auto-login from secure storage.");
        StartAutoLogin();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _defaultStatusIcon.Dispose();
            _refreshLock.Dispose();
            _cts.Dispose();
            _currentCustomIcon?.Dispose();
            ClearAllPerNumberTrayIcons();
        }

        base.Dispose(disposing);
    }

    private async Task LoginAsync()
    {
        try
        {
            _logger.Info("Login action started from tray.");
            SetMenuEnabled(false);
            ShowInfo("Logowanie", "Otwieram terminal do logowania...");

            var credentials = await TerminalCredentialPrompt.PromptAsync(_logger, _cts.Token);
            if (credentials is null)
            {
                _logger.Info("Login canceled or credentials were not captured from terminal.");
                ShowInfo("Logowanie", "Logowanie anulowane lub nieudane.");
                return;
            }

            _logger.Info("Credentials captured. Requesting auth token...");
            await _authService.LoginAsync(credentials, _cts.Token);
            _logger.Info("Auth token acquired. Refreshing API data...");
            await RefreshDataAsync(_cts.Token);
            StartBackgroundLoopIfNeeded();
            ShowInfo("Logowanie", "Zalogowano pomyslnie.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Login flow failed: {ex}");
            ShowError("Logowanie", "Logowanie nie powiodlo sie. Sprawdz logi.");
        }
        finally
        {
            SetMenuEnabled(true);
        }
    }

    private void StartAutoLogin()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                SetMenuEnabled(false);

                if (_authService.HasStoredToken)
                {
                    _logger.Info("Stored token found. Verifying token by refreshing data.");
                    try
                    {
                        await RefreshDataAsync(_cts.Token);
                        StartBackgroundLoopIfNeeded();
                        _logger.Info("Auto-login by stored token succeeded.");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Stored token validation failed: {ex.Message}");
                    }
                }

                if (_authService.HasStoredCredentials)
                {
                    _logger.Info("Stored credentials found. Attempting automatic login.");
                    var loggedIn = await _authService.LoginWithStoredCredentialsAsync(_cts.Token);
                    if (loggedIn)
                    {
                        await RefreshDataAsync(_cts.Token);
                        StartBackgroundLoopIfNeeded();
                        _logger.Info("Auto-login by stored credentials succeeded.");
                        return;
                    }
                }

                _logger.Info("Auto-login not available. Manual login required.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Auto-login failed: {ex}");
            }
            finally
            {
                SetMenuEnabled(true);
            }
        }, _cts.Token);
    }

    private void ChangeApiKey()
    {
        var newKey = InputDialog.Show("Zmien API key", "Podaj nowy API key:", _config.ApiKey);
        if (newKey is null)
        {
            return;
        }

        _config.ApiKey = newKey.Trim();
        _configService.Save(_config);
        _logger.Info("API key updated from tray menu.");
        ShowInfo("Konfiguracja", "API key zapisany.");
    }

    private void ToggleAutostart()
    {
        try
        {
            var nextState = !_autostartMenuItem.Checked;
            _autostartService.SetEnabled(nextState);
            _autostartMenuItem.Checked = _autostartService.IsEnabled();
            _logger.Info($"Autostart changed. Enabled={_autostartMenuItem.Checked}.");
            ShowInfo("Autostart", _autostartMenuItem.Checked ? "Autostart wlaczony." : "Autostart wylaczony.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Autostart toggle failed: {ex}");
            ShowError("Autostart", "Nie udalo sie zmienic ustawienia autostartu.");
        }
    }

    private void ToggleHideSecretsInLogs()
    {
        _config.HideSecretsInLogs = !_config.HideSecretsInLogs;
        _hideSecretsInLogsMenuItem.Checked = _config.HideSecretsInLogs;
        _configService.Save(_config);
        _logger.SetHideSecretsInLogs(_config.HideSecretsInLogs);
        _logger.Info($"HideSecretsInLogs changed. Enabled={_config.HideSecretsInLogs}.");
        ShowInfo("Logi", _config.HideSecretsInLogs ? "Ukrywanie sekretow wlaczone." : "Ukrywanie sekretow wylaczone.");
    }

    private void ExitApplication()
    {
        _logger.Info("Application exit requested.");
        _cts.Cancel();
        ExitThread();
    }

    private void StartBackgroundLoopIfNeeded()
    {
        if (_backgroundLoopTask is { IsCompleted: false })
        {
            return;
        }

        _backgroundLoopTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var delaySeconds = _config.RefreshIntervalSeconds;
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _cts.Token);
                    await RefreshDataAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ReauthRequiredException ex)
                {
                    _logger.Error($"Reauth required: {ex}");
                    _authService.ClearTokens();
                    SetLoggedOutState("Token wygasl. Wymagane ponowne logowanie.");
                    break;
                }
                catch (ApiUnauthorizedException ex) when (ex.Target == ApiUnauthorizedTarget.Products)
                {
                    _logger.Error($"Products unauthorized. Verify API key. Body: {ex.ResponseBody}");
                    SetLoggedOutState("Products unauthorized. Sprawdz API key.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Background refresh failed: {ex}");
                }
            }
        }, _cts.Token);
    }

    private async Task RefreshDataAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            _logger.Info("Starting data refresh cycle.");
            if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                _logger.Error("Data refresh skipped: API key is empty.");
                SetLoggedOutState("Brak API key. Ustaw w menu.");
                return;
            }

            _currentUsage = await LoadUsageWithRetryAsync(cancellationToken);
            var currentSignature = CreateUsageSignature(_currentUsage);
            if (string.Equals(_lastUsageSignature, currentSignature, StringComparison.Ordinal))
            {
                _logger.Info("Data unchanged. Skipping tray icon refresh.");
                return;
            }

            _lastUsageSignature = currentSignature;
            UpdateTrayUi(_currentUsage);
            _logger.Info($"Data refresh completed. Members: {_currentUsage.Count}.");
        }
        catch (ApiUnauthorizedException ex) when (ex.Target == ApiUnauthorizedTarget.Groups)
        {
            _logger.Error($"Groups unauthorized during refresh: {ex}");
            throw new ReauthRequiredException("Unauthorized API response.", ex);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<List<MemberUsage>> LoadUsageWithRetryAsync(CancellationToken cancellationToken)
    {
        var accessToken = await _authService.GetValidAccessTokenAsync(cancellationToken);
        try
        {
            return await LoadUsageForTokenAsync(accessToken, cancellationToken);
        }
        catch (ApiUnauthorizedException ex) when (ex.Target == ApiUnauthorizedTarget.Products)
        {
            _logger.Error("Products returned unauthorized. Trying one forced token refresh before failing.");
            var refreshedToken = await _authService.ForceRefreshAccessTokenAsync(cancellationToken);
            return await LoadUsageForTokenAsync(refreshedToken, cancellationToken);
        }
    }

    private async Task<List<MemberUsage>> LoadUsageForTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        var members = await GetCachedActiveMembersAsync(accessToken, cancellationToken);

        var tasks = members.Select(async member =>
        {
            var productsJson = await _apiClient.GetProductsJsonAsync(accessToken, _config.ApiKey, member.Id, cancellationToken);
            var usage = ProductParser.ParseUsageFromProducts(productsJson);
            return new MemberUsage
            {
                UserId = member.Id,
                PhoneNumber = NormalizePhoneNumber(member.PrimaryMsisdn),
                RemainingMb = usage.DomesticMb,
                RoamingMb = usage.RoamingMb
            };
        });

        return (await Task.WhenAll(tasks)).ToList();
    }

    private async Task<List<GroupMember>> GetCachedActiveMembersAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (_cachedActiveMembers is not null)
        {
            return _cachedActiveMembers;
        }

        var members = await _apiClient.GetMembersAsync(accessToken, _config.ApiKey, cancellationToken);
        _cachedActiveMembers = members
            .Where(m => string.Equals(m.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.Info($"Groups fetched once. Cached active members: {_cachedActiveMembers.Count}.");
        return _cachedActiveMembers;
    }

    private void UpdateTrayUi(List<MemberUsage> usage)
    {
        var totalMb = CalculateSelectedTotalMb(usage);
        var totalGb = totalMb / 1024m;
        var iconText = FormatIconTextFromGb(totalGb);
        var tooltip = $"Total {totalMb:0.##} MB | {totalGb:0.##} GB";

        _uiContext.Post(_ =>
        {
            _currentCustomIcon?.Dispose();
            _currentCustomIcon = TrayIconFactory.CreateNumberIcon(iconText);
            _notifyIcon.Icon = _currentCustomIcon;
            _notifyIcon.Text = TruncateTooltip(tooltip);
            RebuildNumbersMenuItems(usage);
            UpdatePerNumberTrayIcons(usage);
        }, null);
    }

    private void SetLoggedOutState(string message)
    {
        _uiContext.Post(_ =>
        {
            _currentCustomIcon?.Dispose();
            _currentCustomIcon = null;
            _notifyIcon.Icon = SystemIcons.Warning;
            _notifyIcon.Text = TruncateTooltip($"Nju Tray: {message}");
            RebuildNumbersMenuItems([]);
            ClearAllPerNumberTrayIcons();
        }, null);
    }

    private void SetMenuEnabled(bool enabled)
    {
        _uiContext.Post(_ =>
        {
            _loginMenuItem.Enabled = enabled;
            _hideSecretsInLogsMenuItem.Enabled = true;
            _autostartMenuItem.Enabled = true;
            _changeApiKeyMenuItem.Enabled = enabled;
            _exitMenuItem.Enabled = true;
        }, null);
    }

    private void ShowInfo(string title, string message)
    {
        _uiContext.Post(_ =>
        {
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.ShowBalloonTip(2500);
        }, null);
    }

    private void ShowError(string title, string message)
    {
        _uiContext.Post(_ =>
        {
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Error;
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.ShowBalloonTip(3000);
        }, null);
    }

    private static string TruncateTooltip(string text)
    {
        const int maxLength = 63;
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 1)];
    }

    private static Icon LoadDefaultTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "wifi.ico");
            if (File.Exists(iconPath))
            {
                using var fromFile = new Icon(iconPath);
                return (Icon)fromFile.Clone();
            }
        }
        catch
        {
            // fallback below
        }

        return (Icon)SystemIcons.Information.Clone();
    }

    private static string NormalizePhoneNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.StartsWith("48", StringComparison.Ordinal) && value.Length > 2
            ? value[2..]
            : value;
    }

    private void RebuildNumbersMenuItems(IReadOnlyCollection<MemberUsage> usage)
    {
        while (_contextMenu.Items.Count > 0 && _contextMenu.Items[0] != _numbersSeparator)
        {
            var item = _contextMenu.Items[0];
            _contextMenu.Items.RemoveAt(0);
            item.Dispose();
        }

        if (usage.Count == 0)
        {
            var emptyItem = new ToolStripMenuItem("Brak danych")
            {
                Enabled = false
            };
            _contextMenu.Items.Insert(0, emptyItem);
            _numbersSeparator.Visible = false;
            return;
        }

        var ordered = usage.OrderBy(x => x.PhoneNumber).ToList();
        var totalMb = CalculateSelectedTotalMb(ordered);
        var totalGb = totalMb / 1024m;
        var totalItem = new ToolStripMenuItem($"Total: {totalMb:0.##} MB | {totalGb:0.##} GB")
        {
            Enabled = false
        };
        _contextMenu.Items.Insert(0, totalItem);

        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var usageItem = ordered[i];
            var domesticGb = usageItem.RemainingMb / 1024m;
            var domesticKey = BuildUsageKey(usageItem.PhoneNumber, UsageKind.Domestic);
            var domesticItem = new ToolStripMenuItem($"{usageItem.PhoneNumber} (kraj): {usageItem.RemainingMb:0.##} MB | {domesticGb:0.##} GB")
            {
                Checked = IsPerNumberIconEnabled(domesticKey),
                CheckOnClick = true
            };
            domesticItem.Click += (_, _) => TogglePerNumberIcon(domesticKey, domesticItem.Checked);
            _contextMenu.Items.Insert(1, domesticItem);

            var roamingGb = usageItem.RoamingMb / 1024m;
            var roamingKey = BuildUsageKey(usageItem.PhoneNumber, UsageKind.Roaming);
            var roamingItem = new ToolStripMenuItem($"{usageItem.PhoneNumber} (roaming): {usageItem.RoamingMb:0.##} MB | {roamingGb:0.##} GB")
            {
                Checked = IsPerNumberIconEnabled(roamingKey),
                CheckOnClick = true
            };
            roamingItem.Click += (_, _) => TogglePerNumberIcon(roamingKey, roamingItem.Checked);
            _contextMenu.Items.Insert(2, roamingItem);
        }

        _numbersSeparator.Visible = true;
    }

    private void UpdatePerNumberTrayIcons(IReadOnlyCollection<MemberUsage> usage)
    {
        var requiredKeys = usage
            .SelectMany(u =>
            {
                var keys = new List<string>(2);
                var domesticKey = BuildUsageKey(u.PhoneNumber, UsageKind.Domestic);
                var roamingKey = BuildUsageKey(u.PhoneNumber, UsageKind.Roaming);
                if (IsPerNumberIconEnabled(domesticKey))
                {
                    keys.Add(domesticKey);
                }

                if (IsPerNumberIconEnabled(roamingKey))
                {
                    keys.Add(roamingKey);
                }

                return keys;
            })
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in _numberTrayIcons.Keys.ToList())
        {
            if (!requiredKeys.Contains(key))
            {
                RemovePerNumberIcon(key);
            }
        }

        foreach (var item in usage.OrderBy(u => u.PhoneNumber))
        {
            UpdateSingleUsageTrayIcon(
                BuildUsageKey(item.PhoneNumber, UsageKind.Domestic),
                $"{item.PhoneNumber} kraj: {item.RemainingMb:0.##} MB | {(item.RemainingMb / 1024m):0.##} GB",
                item.RemainingMb / 1024m);

            UpdateSingleUsageTrayIcon(
                BuildUsageKey(item.PhoneNumber, UsageKind.Roaming),
                $"{item.PhoneNumber} roaming: {item.RoamingMb:0.##} MB | {(item.RoamingMb / 1024m):0.##} GB",
                item.RoamingMb / 1024m);
        }
    }

    private void ClearAllPerNumberTrayIcons()
    {
        foreach (var key in _numberTrayIcons.Keys.ToList())
        {
            RemovePerNumberIcon(key);
        }
    }

    private void RemovePerNumberIcon(string key)
    {
        if (!_numberTrayIcons.TryGetValue(key, out var iconState))
        {
            return;
        }

        iconState.TrayIcon.Dispose();
        iconState.Icon?.Dispose();
        _numberTrayIcons.Remove(key);
    }

    private bool IsPerNumberIconEnabled(string usageKey)
    {
        if (_perNumberTrayIconEnabled.TryGetValue(usageKey, out var enabled))
        {
            return enabled;
        }

        _perNumberTrayIconEnabled[usageKey] = true;
        return true;
    }

    private void TogglePerNumberIcon(string usageKey, bool enabled)
    {
        _perNumberTrayIconEnabled[usageKey] = enabled;
        SavePerNumberIconPreferences();

        if (_currentUsage.Count > 0)
        {
            UpdateTrayUi(_currentUsage);
        }
        else if (!enabled)
        {
            RemovePerNumberIcon(usageKey);
        }
    }

    private void SavePerNumberIconPreferences()
    {
        _config.PerNumberTrayIconEnabled = new Dictionary<string, bool>(_perNumberTrayIconEnabled, StringComparer.Ordinal);
        _configService.Save(_config);
    }

    private void UpdateSingleUsageTrayIcon(string usageKey, string tooltip, decimal usageGb)
    {
        if (!IsPerNumberIconEnabled(usageKey))
        {
            RemovePerNumberIcon(usageKey);
            return;
        }

        if (!_numberTrayIcons.TryGetValue(usageKey, out var iconState))
        {
            var guid = CreateDeterministicGuid(usageKey);
            iconState = new NumberTrayIconState(guid, new NativeTrayIcon(guid));
            _numberTrayIcons[usageKey] = iconState;
        }

        iconState.Icon?.Dispose();
        var gbText = FormatIconTextFromGb(usageGb);
        iconState.Icon = TrayIconFactory.CreateNumberIcon(gbText);
        try
        {
            iconState.TrayIcon.Show(iconState.Icon, TruncateTooltip(tooltip));
        }
        catch (Exception ex)
        {
            _logger.Error($"Per-number tray icon update failed for {usageKey}: {ex.Message}");
            RemovePerNumberIcon(usageKey);
        }
    }

    private static string BuildUsageKey(string phoneNumber, UsageKind usageKind)
    {
        return $"{phoneNumber}|{usageKind}";
    }

    private static string FormatIconTextFromGb(decimal gb)
    {
        var roundedGb = Math.Max(0, decimal.Round(gb, 0, MidpointRounding.AwayFromZero));
        if (roundedGb < 1000m)
        {
            return roundedGb.ToString("0");
        }

        var tb = Math.Max(1, (int)decimal.Round(roundedGb / 1024m, 0, MidpointRounding.AwayFromZero));
        return $"{tb}TB";
    }

    private static string CreateUsageSignature(IEnumerable<MemberUsage> usage)
    {
        return string.Join(
            "|",
            usage
                .OrderBy(x => x.PhoneNumber, StringComparer.Ordinal)
                .Select(x => $"{x.PhoneNumber}:{x.RemainingMb:0.######}:{x.RoamingMb:0.######}"));
    }

    private static Guid CreateDeterministicGuid(string usageKey)
    {
        var bytes = Encoding.UTF8.GetBytes($"NjuTrayApp:{usageKey}");
        var hash = MD5.HashData(bytes);
        return new Guid(hash);
    }

    private decimal CalculateSelectedTotalMb(IReadOnlyCollection<MemberUsage> usage)
    {
        decimal total = 0m;
        foreach (var item in usage)
        {
            if (IsPerNumberIconEnabled(BuildUsageKey(item.PhoneNumber, UsageKind.Domestic)))
            {
                total += item.RemainingMb;
            }

            if (IsPerNumberIconEnabled(BuildUsageKey(item.PhoneNumber, UsageKind.Roaming)))
            {
                total += item.RoamingMb;
            }
        }

        return total;
    }

    private enum UsageKind
    {
        Domestic,
        Roaming
    }

    private sealed class NumberTrayIconState(Guid guid, NativeTrayIcon trayIcon)
    {
        public Guid Guid { get; } = guid;
        public NativeTrayIcon TrayIcon { get; } = trayIcon;
        public Icon? Icon { get; set; }
    }
}

