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
using File = System.IO.File;

namespace FanBento.Fetch.FanboxApi
{
    public class FanboxApi : IDisposable
    {
        public FanboxApi(string fanboxSessionId)
        {
            var httpClientHandler = new HttpClientHandler {CookieContainer = new CookieContainer()};
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

        public async Task DownloadFile(string url, string destinationPath)
        {
            if (!Directory.Exists(destinationPath))
                Directory.CreateDirectory(destinationPath);
            var fileName = Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Cannot extract filename from url");

            var filePath = Path.Join(destinationPath, fileName);
            if (!File.Exists(filePath))
            {
                LogTo.Information($"Downloading file {url}");
                await using var httpStream = await HttpClient.GetStreamAsync(url);
                await using var fileStream = File.Create(filePath);
                await httpStream.CopyToAsync(fileStream);
            }
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
            var posts = listHomeResponseRoot.Body.Posts.Where(t => t.Body != null).ToList();

            return (posts, NextPostsListUrl != null);
        }
    }
}