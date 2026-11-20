using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
        var fanboxSessionCookie = new Cookie
        {
            Name = "FANBOXSESSID",
            Value = fanboxSessionId,
            Domain = ".fanbox.cc",
            Path = "/",
            Expires = DateTime.UtcNow.AddMonths(1),
            HttpOnly = true,
            Secure = true
        };
        var fanboxHeaders = new Dictionary<string, string>
        {
            ["Origin"] = "https://www.fanbox.cc",
            ["Referer"] = "https://www.fanbox.cc/",
        };
        HttpCloakApi = new HttpCloakApi([fanboxSessionCookie], fanboxHeaders);
    }

    private HttpCloakApi HttpCloakApi { get; }
    private string NextPostsListUrl { get; set; }

    private List<string> PostPaginationUrls { get; set; } = [];

    public void Dispose()
    {
        HttpCloakApi?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task DownloadFile(string url, Stream stream)
    {
        var (httpStream, length) = await HttpCloakApi.GetStreamAsync(url);
        await using (httpStream)
        {
            var start = stream.CanSeek ? stream.Position : (long?)null;
            await httpStream.CopyToAsync(stream);
            if (length.HasValue && start.HasValue && stream.Position - start.Value != length.Value)
                throw new IOException("FANBOX download did not match its expected length.");
        }
    }

    public async Task<(Stream, long?)> GetDownloadFileStream(string url)
    {
        return await HttpCloakApi.GetStreamAsync(url);
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
        var resultJson = await HttpCloakApi.GetStringAsync(url);
        LogTo.Debug(resultJson);
        var listHomeResponseRoot = JsonSerializer.Deserialize<ListHomeResponseRoot>(resultJson);
        if (listHomeResponseRoot?.Body == null)
            throw CreateFanboxJsonException(resultJson);

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
            var paginationResultJson = await HttpCloakApi.GetStringAsync(paginationUrl);
            LogTo.Debug(paginationResultJson);
            PostPaginationUrls = ParseCreatorPageUrls(paginationResultJson);
        }

        if (PostPaginationUrls.Count == 0) return ([], false);

        var url = PostPaginationUrls[0];
        LogTo.Information($"Fetching posts {url}");
        var resultJson = await HttpCloakApi.GetStringAsync(url);
        LogTo.Debug(resultJson);
        var posts = ParseCreatorPosts(resultJson);
        PostPaginationUrls.RemoveAt(0);

        return (posts, PostPaginationUrls.Count != 0);
    }

    private static List<Post> ParseCreatorPosts(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("body", out var posts))
        {
            if (posts.ValueKind == JsonValueKind.Object && posts.TryGetProperty("posts", out var nestedPosts))
                posts = nestedPosts;
            if (posts.ValueKind == JsonValueKind.Array)
                return posts.Deserialize<List<Post>>();
        }

        throw CreateFanboxJsonException(json);
    }

    private static List<string> ParseCreatorPageUrls(string json)
    {
        var body = (JsonNode.Parse(json) as JsonObject)?["body"];
        var pageUrls = body switch
        {
            JsonArray legacyPageUrls => legacyPageUrls,
            JsonObject pagination => pagination["pageUrls"] as JsonArray,
            _ => null
        };
        if (pageUrls == null) throw CreateFanboxJsonException(json);

        var urls = new List<string>(pageUrls.Count);
        foreach (var pageUrl in pageUrls)
        {
            if (pageUrl is not JsonValue value || !value.TryGetValue<string>(out var url) ||
                string.IsNullOrWhiteSpace(url))
                throw new JsonException("FANBOX creator pagination returned an invalid page URL.");
            urls.Add(url);
        }

        return urls;
    }

    public async Task<ContentBody> GetPostBody(string id)
    {
        var url = $"https://api.fanbox.cc/post.info?postId={id}";

        LogTo.Information($"Fetching post body {url}");
        var resultJson = await HttpCloakApi.GetStringAsync(url);
        LogTo.Debug(resultJson);
        var postResponseRoot = JsonSerializer.Deserialize<PostResponseRoot>(resultJson);
        if (postResponseRoot?.Body == null)
            throw CreateFanboxJsonException(resultJson);

        return postResponseRoot.Body.Post.Body;
    }

    private static JsonException CreateFanboxJsonException(string resultJson)
    {
        try
        {
            var error = JsonNode.Parse(resultJson)?["error"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(error)) return new JsonException($"Fanbox API returned error: {error}");
        }
        catch (JsonException)
        {
        }

        return new JsonException("Cannot decode the json returned from fanbox website.");
    }
}