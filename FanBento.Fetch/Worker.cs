using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Anotar.Serilog;
using FanBento.Database;
using FanBento.Database.Models;
using Microsoft.EntityFrameworkCore;

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
            await Database.Database.EnsureCreatedAsync();
        }

        private async Task DownloadPostsImages(IEnumerable<Post> posts)
        {
            var imageSavePath = Configuration.Config["Fanbox:ImageSavePath"];
            var imageUrlList = posts.SelectMany(
                    t => t.Body.Images ?? t.Body.ImageMap?.Values.ToList() ?? new List<Image>(),
                    (_, d) => d.OriginalUrl)
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
                    (_, d) => d.Url)
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

            return posts;
        }

        public async Task AddToDatabase(List<Post> posts)
        {
            // unify same user objects, and add order to lists
            var users = posts.Select(t => t.User).Distinct(new UserEqualityComparer()).ToList();
            foreach (var post in posts)
            {
                post.User = users.First(t => t.UserId == post.User.UserId);
                post.Body.Blocks?.AddOrder();
                post.Body.Files?.AddOrder();
                post.Body.Images?.AddOrder();
            }

            // get existing users in database
            var usersInDatabase = await Database.User.AsNoTracking().ToListAsync();

            // add new posts to database
            Database.Post.AddRange(posts);

            // Mark users that already existed in database as modified, no matter it is actually modified or not
            foreach (var user in users.Where(user => usersInDatabase.Any(t => t.UserId == user.UserId)))
                Database.Entry(user).State = EntityState.Modified;

            await Database.SaveChangesAsync();
        }

        public async Task WorkOnce()
        {
            try
            {
                var posts = await FetchNewPosts(Database);
                await AddToDatabase(posts);
            }
            catch (Exception e)
            {
                if (LogTo.IsErrorEnabled) LogTo.Error(e, "Error in worker");
            }
        }

        private class UserEqualityComparer : IEqualityComparer<User>
        {
            public bool Equals(User x, User y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.UserId == y.UserId;
            }

            public int GetHashCode(User obj)
            {
                return obj.UserId != null ? obj.UserId.GetHashCode() : 0;
            }
        }
    }
}