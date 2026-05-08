using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HttpCloak;

namespace FanBento.Fetch.Api;

public class HttpCloakApi : IDisposable
{
    private const int MaxRequestAttempts = 5;

    private readonly Dictionary<string, string> headers;
    private readonly Session session;

    public HttpCloakApi(IEnumerable<System.Net.Cookie> cookies, IDictionary<string, string> headers)
    {
        this.headers = headers.ToDictionary();
        session = new Session(preset: Presets.Firefox133, retry: 0);

        foreach (var cookie in cookies.Where(cookie => !string.IsNullOrWhiteSpace(cookie.Name)))
            session.SetCookie(cookie.Name, cookie.Value, domain: cookie.Domain, secure: cookie.Secure);
    }

    public async Task<string> GetStringAsync(string url)
    {
        for (var attempt = 1; attempt <= MaxRequestAttempts; attempt++)
        {
            var response = await Task.Run(() => session.Get(url, headers));
            if (response.StatusCode is >= 200 and < 300) return response.Text;

            if (response.StatusCode == 429 && attempt < MaxRequestAttempts)
            {
                await Task.Delay(GetRetryDelay(response.Text, attempt));
                continue;
            }

            throw new HttpRequestException($"HttpCloak returned HTTP {response.StatusCode}: {response.Text}");
        }

        throw new HttpRequestException("HttpCloak request failed after all retry attempts.");
    }

    public void Dispose()
    {
        session.Dispose();
        GC.SuppressFinalize(this);
    }

    private static TimeSpan GetRetryDelay(string responseText, int attempt)
    {
        try
        {
            var retryAfter = JsonNode.Parse(responseText)?["retry_after"]?.GetValue<int>();
            if (retryAfter != null)
                return TimeSpan.FromSeconds(Math.Clamp(retryAfter.Value * Math.Pow(2, attempt - 1), 1, 300));
        }
        catch (JsonException)
        {
        }

        return TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, attempt - 1), 300));
    }
}