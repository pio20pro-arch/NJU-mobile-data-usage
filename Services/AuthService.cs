using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using NjuTrayApp.Models;

namespace NjuTrayApp.Services;

public sealed class AuthService
{
    private static readonly Uri TokenUri = new("https://prd-iam.dopapp.pl/auth/realms/nju/protocol/openid-connect/token");
    private const string ClientId = "nju-web-app-v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NetworkRetryDelay = TimeSpan.FromSeconds(5);
    private const int NetworkRetryCount = 60;

    private readonly FileLogger _logger;
    private readonly SecureAuthStore _secureAuthStore;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private TokenState? _tokens;
    private LoginCredentials? _savedCredentials;

    public AuthService(FileLogger logger)
    {
        _logger = logger;
        _secureAuthStore = new SecureAuthStore(logger);
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        RestorePersistedAuthData();
    }

    public bool IsAuthenticated => _tokens is not null;
    public bool HasStoredToken => _tokens is not null;
    public bool HasStoredCredentials => _savedCredentials is not null;

    public async Task LoginAsync(LoginCredentials credentials, CancellationToken cancellationToken)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            var values = new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = ClientId,
                ["username"] = credentials.Username,
                ["password"] = credentials.Password
            };

            _tokens = await ExecuteTokenRequestAsync(values, cancellationToken);
            _savedCredentials = new LoginCredentials
            {
                Username = credentials.Username,
                Password = credentials.Password
            };
            PersistAuthData();
            _logger.Info("Login succeeded.");
        }
        finally
        {
            _authLock.Release();
        }
    }

    public async Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            if (_tokens is null)
            {
                throw new ReauthRequiredException("No tokens in memory.");
            }

            if (_tokens.AccessExpiresAt > DateTimeOffset.UtcNow.Add(RefreshSkew))
            {
                return _tokens.AccessToken;
            }

            if (_tokens.RefreshExpiresAt <= DateTimeOffset.UtcNow.Add(RefreshSkew))
            {
                _tokens = null;
                throw new ReauthRequiredException("Refresh token expired.");
            }

            var values = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = _tokens.RefreshToken
            };

            _tokens = await ExecuteTokenRequestAsync(values, cancellationToken);
            PersistAuthData();
            _logger.Info("Access token refreshed.");
            return _tokens.AccessToken;
        }
        catch (HttpRequestException ex)
        {
            _logger.Error($"Token refresh network error (will retry in next cycle): {ex.Message}");
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Error($"Token refresh timeout (will retry in next cycle): {ex.Message}");
            throw new HttpRequestException("Token refresh timed out.", ex);
        }
        catch (ReauthRequiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _tokens = null;
            PersistAuthData();
            _logger.Error($"Token refresh failed: {ex}");
            throw new ReauthRequiredException("Refresh token failed.", ex);
        }
        finally
        {
            _authLock.Release();
        }
    }

    public async Task<string> ForceRefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            if (_tokens is null)
            {
                throw new ReauthRequiredException("No tokens in memory.");
            }

            if (_tokens.RefreshExpiresAt <= DateTimeOffset.UtcNow.Add(RefreshSkew))
            {
                _tokens = null;
                throw new ReauthRequiredException("Refresh token expired.");
            }

            var values = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = _tokens.RefreshToken
            };

            _tokens = await ExecuteTokenRequestAsync(values, cancellationToken);
            PersistAuthData();
            _logger.Info("Access token force-refreshed.");
            return _tokens.AccessToken;
        }
        catch (HttpRequestException ex)
        {
            _logger.Error($"Force refresh network error (will retry in next cycle): {ex.Message}");
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Error($"Force refresh timeout (will retry in next cycle): {ex.Message}");
            throw new HttpRequestException("Force refresh timed out.", ex);
        }
        catch (ReauthRequiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _tokens = null;
            PersistAuthData();
            _logger.Error($"Force refresh failed: {ex}");
            throw new ReauthRequiredException("Force refresh failed.", ex);
        }
        finally
        {
            _authLock.Release();
        }
    }

    public void ClearTokens()
    {
        _tokens = null;
        PersistAuthData();
    }

    public async Task<bool> LoginWithStoredCredentialsAsync(CancellationToken cancellationToken)
    {
        if (_savedCredentials is null)
        {
            return false;
        }

        try
        {
            await LoginAsync(_savedCredentials, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Auto login with stored credentials failed: {ex.Message}");
            return false;
        }
    }

    private async Task<TokenState> ExecuteTokenRequestAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(values);
        var requestBodyForLog = await content.ReadAsStringAsync(cancellationToken);
        _logger.LogHttpRequest(
            HttpMethod.Post,
            TokenUri.ToString(),
            [new("Content-Type", "application/x-www-form-urlencoded")],
            requestBodyForLog);
        using var response = await SendTokenRequestWithRetryAsync(values, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var headers = response.Headers.SelectMany(h => h.Value.Select(v => new KeyValuePair<string, string>(h.Key, v)));
        _logger.LogHttpResponse(TokenUri.ToString(), (int)response.StatusCode, headers, body);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Token request failed with {(int)response.StatusCode}: {body}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Token response is empty.");

        var now = DateTimeOffset.UtcNow;
        var accessExp = tokenResponse.ExpiresIn > 0
            ? now.AddSeconds(tokenResponse.ExpiresIn)
            : JwtHelper.TryGetExpiry(tokenResponse.AccessToken) ?? now.AddMinutes(5);
        var refreshExp = tokenResponse.RefreshExpiresIn > 0
            ? now.AddSeconds(tokenResponse.RefreshExpiresIn)
            : JwtHelper.TryGetExpiry(tokenResponse.RefreshToken) ?? now.AddHours(1);

        return new TokenState
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            AccessExpiresAt = accessExp,
            RefreshExpiresAt = refreshExp
        };
    }

    private async Task<HttpResponseMessage> SendTokenRequestWithRetryAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var content = new FormUrlEncodedContent(values);
                using var request = new HttpRequestMessage(HttpMethod.Post, TokenUri) { Content = content };
                return await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (IsNetworkStartupError(ex) && attempt < NetworkRetryCount)
            {
                _logger.Error($"Token request network unavailable ({ex.Message}). Retry {attempt}/{NetworkRetryCount} in {NetworkRetryDelay.TotalSeconds:0}s.");
                await Task.Delay(NetworkRetryDelay, cancellationToken);
            }
        }
    }

    private static bool IsNetworkStartupError(Exception ex)
    {
        if (ex is HttpRequestException httpEx && httpEx.InnerException is SocketException socketEx)
        {
            return socketEx.SocketErrorCode is SocketError.HostNotFound
                or SocketError.TryAgain
                or SocketError.NoData;
        }

        return ex.Message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase);
    }

    private void RestorePersistedAuthData()
    {
        var data = _secureAuthStore.Load();
        if (data is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(data.Username) && !string.IsNullOrWhiteSpace(data.Password))
        {
            _savedCredentials = new LoginCredentials
            {
                Username = data.Username,
                Password = data.Password
            };
        }

        if (data.Tokens is not null &&
            !string.IsNullOrWhiteSpace(data.Tokens.AccessToken) &&
            !string.IsNullOrWhiteSpace(data.Tokens.RefreshToken))
        {
            _tokens = new TokenState
            {
                AccessToken = data.Tokens.AccessToken,
                RefreshToken = data.Tokens.RefreshToken,
                AccessExpiresAt = data.Tokens.AccessExpiresAt,
                RefreshExpiresAt = data.Tokens.RefreshExpiresAt
            };
        }

        _logger.Info($"Secure auth restored. Tokens={_tokens is not null}, Credentials={_savedCredentials is not null}.");
    }

    private void PersistAuthData()
    {
        _secureAuthStore.Save(new PersistedAuthData
        {
            Username = _savedCredentials?.Username,
            Password = _savedCredentials?.Password,
            Tokens = _tokens is null
                ? null
                : new PersistedTokenData
                {
                    AccessToken = _tokens.AccessToken,
                    RefreshToken = _tokens.RefreshToken,
                    AccessExpiresAt = _tokens.AccessExpiresAt,
                    RefreshExpiresAt = _tokens.RefreshExpiresAt
                }
        });
    }
}

public sealed class ReauthRequiredException : Exception
{
    public ReauthRequiredException(string message)
        : base(message)
    {
    }

    public ReauthRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
