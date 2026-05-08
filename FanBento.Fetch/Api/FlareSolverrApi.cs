using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Anotar.Serilog;

namespace FanBento.Fetch.Api;

public class FlareSolverrApi : IDisposable
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Uri endpointUri;
    private readonly HttpClient httpClient;
    private readonly int maxTimeoutMilliseconds;
    private readonly SemaphoreSlim requestLock = new(1, 1);
    private readonly string sessionId = $"fanbento-{Guid.NewGuid():N}";
    private readonly List<FlareSolverrCookie> cookies;
    private bool sessionUsed;

    public FlareSolverrApi(string url, IEnumerable<Cookie> cookies, int maxTimeoutMilliseconds = 60000)
    {
        endpointUri = BuildEndpointUri(url);
        this.cookies = cookies.Select(FlareSolverrCookie.FromCookie).ToList();
        this.maxTimeoutMilliseconds = maxTimeoutMilliseconds;
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(maxTimeoutMilliseconds + 30000)
        };
    }

    public async Task<string> GetStringAsync(string url)
    {
        await requestLock.WaitAsync();
        try
        {
            var request = new FlareSolverrRequest
            {
                Cmd = "request.get",
                Url = url,
                Session = sessionId,
                SessionTtlMinutes = 30,
                MaxTimeout = maxTimeoutMilliseconds,
                Cookies = cookies,
                DisableMedia = true
            };

            sessionUsed = true;

            using var requestContent = new StringContent(
                JsonSerializer.Serialize(request, JsonSerializerOptions),
                Encoding.UTF8,
                "application/json");
            using var httpResponse = await httpClient.PostAsync(endpointUri, requestContent);
            var responseJson = await httpResponse.Content.ReadAsStringAsync();

            var response = JsonSerializer.Deserialize<FlareSolverrResponse>(responseJson, JsonSerializerOptions);
            if (!httpResponse.IsSuccessStatusCode || !string.Equals(response?.Status, "ok", StringComparison.OrdinalIgnoreCase))
                throw new HttpRequestException(
                    $"FlareSolverr request failed: {response?.Message ?? httpResponse.ReasonPhrase ?? responseJson}");

            if (response.Solution?.Response == null)
                throw new JsonException("FlareSolverr response did not contain a solution response.");

            return ExtractBrowserResponseText(response.Solution.Response);
        }
        finally
        {
            requestLock.Release();
        }
    }

    public void Dispose()
    {
        try
        {
            if (sessionUsed) DestroySessionAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            LogTo.Warning(exception, $"Failed to destroy FlareSolverr session {sessionId}");
        }

        requestLock.Dispose();
        httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task DestroySessionAsync()
    {
        var request = new FlareSolverrRequest
        {
            Cmd = "sessions.destroy",
            Session = sessionId
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(request, JsonSerializerOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.PostAsync(endpointUri, requestContent);

        if (!response.IsSuccessStatusCode)
            LogTo.Warning($"FlareSolverr returned {response.StatusCode} while destroying session {sessionId}");
    }

    private static Uri BuildEndpointUri(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) url = "http://localhost:8191";

        var uriBuilder = new UriBuilder(url);
        if (uriBuilder.Host is "0.0.0.0" or "::") uriBuilder.Host = "localhost";

        uriBuilder.Path = uriBuilder.Path.TrimEnd('/');
        if (!uriBuilder.Path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            uriBuilder.Path = $"{uriBuilder.Path}/v1";

        return uriBuilder.Uri;
    }

    private static string ExtractBrowserResponseText(string response)
    {
        var trimmedResponse = response.Trim();
        if (trimmedResponse.StartsWith('{') || trimmedResponse.StartsWith('[')) return trimmedResponse;

        var preMatch = Regex.Match(trimmedResponse, @"<pre\b[^>]*>(?<content>.*)</pre>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (preMatch.Success) return WebUtility.HtmlDecode(preMatch.Groups["content"].Value).Trim();

        var bodyMatch = Regex.Match(trimmedResponse, @"<body\b[^>]*>(?<content>.*)</body>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (bodyMatch.Success) return WebUtility.HtmlDecode(Regex.Replace(bodyMatch.Groups["content"].Value, "<.*?>", string.Empty)).Trim();

        return response;
    }

    private sealed class FlareSolverrRequest
    {
        [JsonPropertyName("cmd")]
        public string Cmd { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("session")]
        public string Session { get; set; }

        [JsonPropertyName("session_ttl_minutes")]
        public int? SessionTtlMinutes { get; set; }

        [JsonPropertyName("maxTimeout")]
        public int? MaxTimeout { get; set; }

        [JsonPropertyName("cookies")]
        public List<FlareSolverrCookie> Cookies { get; set; }

        [JsonPropertyName("disableMedia")]
        public bool? DisableMedia { get; set; }
    }

    private sealed class FlareSolverrResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("solution")]
        public FlareSolverrSolution Solution { get; set; }
    }

    private sealed class FlareSolverrSolution
    {
        [JsonPropertyName("response")]
        public string Response { get; set; }
    }

    private sealed class FlareSolverrCookie
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }

        [JsonPropertyName("domain")]
        public string Domain { get; set; }

        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("httpOnly")]
        public bool HttpOnly { get; set; }

        [JsonPropertyName("secure")]
        public bool Secure { get; set; }

        public static FlareSolverrCookie FromCookie(Cookie cookie)
        {
            return new FlareSolverrCookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path,
                HttpOnly = cookie.HttpOnly,
                Secure = cookie.Secure
            };
        }
    }
}