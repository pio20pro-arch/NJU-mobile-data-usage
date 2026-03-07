using System.Net;
using System.Net.Http.Headers;
using NjuTrayApp.Models;

namespace NjuTrayApp.Services;

public sealed class NjuApiClient
{
    private static readonly Uri GroupsUri = new("https://prd-public-gateway.dopapp.pl/groups?hideEmpty=true");
    private static readonly string ProductsBaseUri = "https://prd-public-gateway.dopapp.pl/products";

    private readonly HttpClient _httpClient;
    private readonly FileLogger _logger;

    public NjuApiClient(FileLogger logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        _httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("pl"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) NjuTrayApp/1.0");
    }

    public async Task<List<GroupMember>> GetMembersAsync(string accessToken, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GroupsUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("origin", "https://nju.pl");
        request.Headers.Referrer = new Uri("https://nju.pl/");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("apikey", apiKey);
        }

        _logger.LogHttpRequest(HttpMethod.Get, GroupsUri.ToString(), BuildRequestHeadersForLog(request));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogHttpResponse(GroupsUri.ToString(), (int)response.StatusCode, BuildResponseHeadersForLog(response), body);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ApiUnauthorizedException(ApiUnauthorizedTarget.Groups, body);
        }

        response.EnsureSuccessStatusCode();
        var groups = System.Text.Json.JsonSerializer.Deserialize<List<GroupResponse>>(body)
            ?? [];
        return groups.SelectMany(g => g.Members).ToList();
    }

    public async Task<string> GetProductsJsonAsync(
        string accessToken,
        string apiKey,
        string userId,
        CancellationToken cancellationToken)
    {
        var uri = $"{ProductsBaseUri}?userId={Uri.EscapeDataString(userId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.TryAddWithoutValidation("origin", "https://nju.pl");
        request.Headers.Referrer = new Uri("https://nju.pl/");

        _logger.LogHttpRequest(HttpMethod.Get, uri, BuildRequestHeadersForLog(request));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogHttpResponse(uri, (int)response.StatusCode, BuildResponseHeadersForLog(response), body);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ApiUnauthorizedException(ApiUnauthorizedTarget.Products, body);
        }

        response.EnsureSuccessStatusCode();
        return body;
    }

    private static List<KeyValuePair<string, string>> BuildRequestHeadersForLog(HttpRequestMessage request)
    {
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var header in request.Headers)
        {
            var value = string.Join("; ", header.Value);
            headers.Add(new KeyValuePair<string, string>(header.Key, value));
        }
        return headers;
    }

    private static List<KeyValuePair<string, string>> BuildResponseHeadersForLog(HttpResponseMessage response)
    {
        return response.Headers
            .SelectMany(h => h.Value.Select(v => new KeyValuePair<string, string>(h.Key, v)))
            .Concat(response.Content.Headers.SelectMany(h => h.Value.Select(v => new KeyValuePair<string, string>(h.Key, v))))
            .ToList();
    }
}

public enum ApiUnauthorizedTarget
{
    Groups,
    Products
}

public sealed class ApiUnauthorizedException : Exception
{
    public ApiUnauthorizedException(ApiUnauthorizedTarget target, string responseBody)
        : base($"Unauthorized API response for {target}.")
    {
        Target = target;
        ResponseBody = responseBody;
    }

    public ApiUnauthorizedTarget Target { get; }
    public string ResponseBody { get; }
}
