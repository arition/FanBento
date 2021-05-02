using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Anotar.Serilog;
using FanBento.Database;
using FanBento.Database.Models;

namespace FanBento.Fetch
{
    public class Worker
    {
        public Worker()
        {
            InitDatabase().Wait();
            FanboxApi = new FanboxApi.FanboxApi(Configuration.Config["Fanbox:FanboxSessionId"]);
        }

        private FanboxApi.FanboxApi FanboxApi { get; }
        private FanBentoDatabase Database { get; set; }

        private async Task InitDatabase()
        {
            Database = new FanBentoDatabase(Configuration.Config["Database:ConnectionString"]);
#if DEBUG
            await Database.Database.EnsureCreatedAsync();
#endif
        }

        private async Task DownloadPostsImages(IEnumerable<Post> posts)
        {
            var imageSavePath = Configuration.Config["Fanbox:ImageSavePath"];
            var imageUrlList = posts.SelectMany(
                    t => t.Body.Images ?? t.Body.ImageMap?.Values.ToList() ?? new List<Image>(),
                    (t, d) => d.OriginalUrl)
                .ToList();
            await Task.WhenAll(imageUrlList.Select(async url =>
            {
                try
                {
                    await FanboxApi.DownloadFile(url, imageSavePath);
                }
                catch (Exception e)
                {
                    LogTo.Warning(e, $"Failed to download image {url}");
                }
            }));
        }

        private async Task DownloadPostsFiles(IEnumerable<Post> posts)
        {
            var fileSavePath = Configuration.Config["Fanbox:FileSavePath"];
            var fileUrlList = posts.SelectMany(
                    t => t.Body.Files ?? t.Body.FileMap?.Values.ToList() ?? new List<File>(),
                    (t, d) => d.Url)
                .ToList();
            await Task.WhenAll(fileUrlList.Select(async url =>
            {
                try
                {
                    await FanboxApi.DownloadFile(url, fileSavePath);
                }
                catch (Exception e)
                {
                    LogTo.Warning(e, $"Failed to download file {url}");
                }
            }));
        }

        private async Task<List<Post>> FetchNewPosts(FanBentoDatabase database)
        {
            var posts = new List<Post>();
            var hasNextPage = false;
            var idList = database.Post.Select(t => t.Id).ToHashSet();

            do
            {
                List<Post> list;
                (list, hasNextPage) = await FanboxApi.GetPostsList(hasNextPage);
                var newPostsList = list.AsParallel().Where(t => !idList.Contains(t.Id)).ToList();
                if (newPostsList.Count != list.Count)
                {
                    // some posts already exists, next page should all be old posts
                    hasNextPage = false;
                }

                await DownloadPostsImages(newPostsList);
                await DownloadPostsFiles(newPostsList);
                posts.AddRange(newPostsList);
            } while (hasNextPage);

            posts.UnifyUserReference();
            return posts;
        }

        public async Task WorkOnce()
        {
            try
            {
                var posts = await FetchNewPosts(Database);
                Database.Post.AddRange(posts);
                await Database.SaveChangesAsync();
            }
            catch (Exception e)
            {
                if (LogTo.IsErrorEnabled) LogTo.Error(e, "Error in worker");
            }
        }
    }
}