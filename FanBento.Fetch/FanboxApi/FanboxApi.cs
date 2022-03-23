using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Anotar.Serilog;
using FanBento.Database.Models;

namespace FanBento.Fetch.FanboxApi
{
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
        }

        private HttpClient HttpClient { get; }
        private string NextPostsListUrl { get; set; }

        public void Dispose()
        {
            HttpClient?.Dispose();
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

        /// <summary>
        ///     Get Posts list. It will fetch first 10 posts by default. If nextPage is true, it will fetch
        ///     10 posts after last post in the last request.
        /// </summary>
        /// <param name="fetchPostsAfterLastRequest">Fetch next page's data</param>
        /// <returns>Tuple: (posts, hasNextPage)</returns>
        public async Task<(List<Post>, bool)> GetPostsList(bool fetchPostsAfterLastRequest = false)
        {
            if (fetchPostsAfterLastRequest && string.IsNullOrWhiteSpace(NextPostsListUrl))
            {
                throw new InvalidOperationException(
                    "Cannot find last request data. Fetch with fetchPostsAfterLastRequest=false at first.");
            }

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
}