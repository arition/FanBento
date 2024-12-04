using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Anotar.Serilog;
using FanBento.Database.Models;

namespace FanBento.Fetch.Api;

public class FanboxApi : IDisposable
{
    public FanboxApi(string fanboxSessionId)
    {
        var httpClientHandler = new HttpClientHandler { CookieContainer = new CookieContainer() };
        httpClientHandler.CookieContainer.Add(new Cookie
        {
            Name = "FANBOXSESSID",
            Value = fanboxSessionId,
            Domain = ".fanbox.cc",
            Path = "/",
            Expires = DateTime.UtcNow.AddMonths(1),
            HttpOnly = true,
            Secure = true
        });
        HttpClient = new HttpClient(httpClientHandler);
        HttpClient.DefaultRequestHeaders.Add("Origin", "https://www.fanbox.cc");
        HttpClient.DefaultRequestHeaders.Add("Referer", "https://www.fanbox.cc/");
        HttpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36 Edg/127.0.0.0");
        HttpClient.Timeout = TimeSpan.FromHours(1);
    }

    private HttpClient HttpClient { get; }
    private string NextPostsListUrl { get; set; }

    private List<string> PostPaginationUrls { get; set; } = [];

    public void Dispose()
    {
        HttpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task DownloadFile(string url, Stream stream)
    {
        await using var httpStream = await HttpClient.GetStreamAsync(url);
        await httpStream.CopyToAsync(stream);
    }

    public async Task<(Stream, long?)> GetDownloadFileStream(string url)
    {
        var response = await HttpClient.GetAsync(url);
        return (await response.Content.ReadAsStreamAsync(), response.Content.Headers.ContentLength);
    }

#nullable enable
    /// <summary>
    ///     Get Posts list. It will fetch first 10 posts by default. If nextPage is true, it will fetch
    ///     10 posts after last post in the last request.
    /// </summary>
    /// <param name="fetchPostsAfterLastRequest">Fetch next page's data</param>
    /// <returns>Tuple: (posts, hasNextPage)</returns>
    public async Task<(List<Post>, bool)> GetPostsList(bool fetchPostsAfterLastRequest = false)
    {
        if (fetchPostsAfterLastRequest && string.IsNullOrWhiteSpace(NextPostsListUrl))
            throw new InvalidOperationException(
                "Cannot find last request data. Fetch with fetchPostsAfterLastRequest=false at first.");

        var url = fetchPostsAfterLastRequest ? NextPostsListUrl : "https://api.fanbox.cc/post.listHome?limit=10";

        LogTo.Information($"Fetching posts {url}");
        var resultJson = await HttpClient.GetStringAsync(url);
        LogTo.Debug(resultJson);
        var listHomeResponseRoot = JsonSerializer.Deserialize<ListHomeResponseRoot>(resultJson);
        if (listHomeResponseRoot == null)
            throw new JsonException("Cannot decode the json returned from fanbox website.");

        NextPostsListUrl = listHomeResponseRoot.Body.NextUrl;
        var posts = listHomeResponseRoot.Body.Posts;

        return (posts, NextPostsListUrl != null);
    }
#nullable disable

    /// <summary>
    ///     Get Posts list. It will fetch first 10 posts by default. If nextPage is true, it will fetch
    ///     10 posts after last post in the last request.
    /// </summary>
    /// <param name="author">Author id</param>
    /// <param name="fetchPostsAfterLastRequest">Fetch next page's data</param>
    /// <returns>Tuple: (posts, hasNextPage)</returns>
    public async Task<(List<Post>, bool)> GetPostsListFromAuthor(string author, bool fetchPostsAfterLastRequest = false)
    {
        if (fetchPostsAfterLastRequest && PostPaginationUrls.Count == 0)
            throw new InvalidOperationException(
                "Cannot find last request data. Fetch with fetchPostsAfterLastRequest=false at first.");

        if (!fetchPostsAfterLastRequest)
        {
            var paginationUrl = $"https://api.fanbox.cc/post.paginateCreator?creatorId={author}";
            LogTo.Information("Fetching posts pagination list");
            var paginationResultJson = await HttpClient.GetStringAsync(paginationUrl);
            LogTo.Debug(paginationResultJson);
            PostPaginationUrls = JsonNode.Parse(paginationResultJson)!["body"]!.AsArray()
                .Select(t => t.GetValue<string>()).ToList();
        }

        var url = PostPaginationUrls[0];
        LogTo.Information($"Fetching posts {url}");
        var resultJson = await HttpClient.GetStringAsync(url);
        LogTo.Debug(resultJson);
        var listCreatorResponseRoot = JsonSerializer.Deserialize<ListCreatorResponseRoot>(resultJson);
        if (listCreatorResponseRoot == null)
            throw new JsonException("Cannot decode the json returned from fanbox website.");

        PostPaginationUrls.RemoveAt(0);

        var posts = listCreatorResponseRoot.Body;

        return (posts, PostPaginationUrls.Count != 0);
    }

    public async Task<ContentBody> GetPostBody(string id)
    {
        var url = $"https://api.fanbox.cc/post.info?postId={id}";

        LogTo.Information($"Fetching post body {url}");
        var resultJson = await HttpClient.GetStringAsync(url);
        LogTo.Debug(resultJson);
        var postResponseRoot = JsonSerializer.Deserialize<PostResponseRoot>(resultJson);
        if (postResponseRoot == null)
            throw new JsonException("Cannot decode the json returned from fanbox website.");

        return postResponseRoot.Body.Body;
    }
}