using System;
using System.Collections.Generic;
using System.IO;
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
            var response = await session.GetAsync(url, headers);
            if (response.StatusCode is >= 200 and < 300) return response.Text;

            if (response.StatusCode == 429 && attempt < MaxRequestAttempts)
            {
                await Task.Delay(GetRetryDelay(response.Text, attempt));
                continue;
            }

            throw CreateHttpRequestException(response.StatusCode, response.Reason, response.Url);
        }

        throw new HttpRequestException("HttpCloak request failed after all retry attempts.");
    }

    public async Task<(Stream, long?)> GetStreamAsync(string url)
    {
        for (var attempt = 1; attempt <= MaxRequestAttempts; attempt++)
        {
            var response = await Task.Run(() => session.GetStream(url, headers));
            if (response.StatusCode is >= 200 and < 300)
            {
                var length = response.ContentLength >= 0 ? response.ContentLength : (long?)null;
                return (new HttpCloakResponseStream(response.GetContentStream(81920), response), length);
            }

            if (response.StatusCode == 429 && attempt < MaxRequestAttempts)
            {
                var retryDelay = GetRetryDelay(response.Text, attempt);
                response.Dispose();
                await Task.Delay(retryDelay);
                continue;
            }

            var exception = CreateHttpRequestException(response.StatusCode, response.Reason, response.Url);
            response.Dispose();
            throw exception;
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

    private static HttpRequestException CreateHttpRequestException(int statusCode, string reason, string url)
    {
        var reasonText = string.IsNullOrWhiteSpace(reason) ? "no reason phrase" : reason;
        var requestHost = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "unknown host";
        return new HttpRequestException($"HttpCloak returned HTTP {statusCode} ({reasonText}) for host {requestHost}.");
    }

    private sealed class HttpCloakResponseStream : Stream
    {
        private readonly Stream inner;
        private readonly StreamResponse response;

        public HttpCloakResponseStream(Stream inner, StreamResponse response)
        {
            this.inner = inner;
            this.response = response;
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
            inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            response.Dispose();
            await base.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}