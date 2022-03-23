using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Anotar.Serilog;
using FanBento.Database;
using FanBento.Database.Models;
using Microsoft.EntityFrameworkCore;
using MimeTypes;
using Minio;
using File = System.IO.File;

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
        private MinioClient S3Client { get; set; }

        private async Task InitDatabase()
        {
            Database = new FanBentoDatabase(Configuration.Config["Database:ConnectionString"]);
            await Database.Database.EnsureCreatedAsync();
        }

        private async Task DownloadFileToFileSystem(string url, string destinationPath)
        {
            if (!Directory.Exists(destinationPath))
                Directory.CreateDirectory(destinationPath);
            var fileName = Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Cannot extract filename from url");

            var filePath = Path.Join(destinationPath, fileName);
            if (!File.Exists(filePath))
            {
                LogTo.Information($"Downloading file {url}");
                await using var fileStream = File.Create(filePath);
                await FanboxApi.DownloadFile(url, fileStream);
            }
        }

        private async Task DownloadFileToS3(string url, string destinationPath)
        {
            S3Client ??= new MinioClient()
                .WithEndpoint(Configuration.Config["Assets:S3:EndPoint"])
                .WithCredentials(Configuration.Config["Assets:S3:KeyId"],
                    Configuration.Config["Assets:S3:KeySecret"])
                .WithSSL()
                .Build();

            var fileName = Path.GetFileName(new Uri(url).LocalPath);
            var mimeType = MimeTypeMap.GetMimeType(Path.GetExtension(fileName).Substring(1));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Cannot extract filename from url");

            var (stream, length) = await FanboxApi.GetDownloadFileStream(url);
            await using var httpStream = stream;
            var httpStreamLength = length.Value;
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(Configuration.Config["Assets:S3:Bucket"])
                .WithObject($"{destinationPath}/{fileName}")
                .WithStreamData(httpStream)
                .WithObjectSize(httpStreamLength)
                .WithContentType(mimeType);
            await S3Client.PutObjectAsync(putObjectArgs);
        }

        private async Task DownloadPostsImages(IEnumerable<Post> posts)
        {
            var imageUrlList = posts.SelectMany(
                    t => t.Body.Images ?? t.Body.ImageMap?.Values.ToList() ?? new List<Image>(),
                    (_, d) => d.OriginalUrl)
                .ToList();
            await Task.WhenAll(imageUrlList.Select(async url =>
            {
                try
                {
                    switch (Configuration.Config["Assets:Storage"])
                    {
                        case "FileSystem":
                            await DownloadFileToFileSystem(url,
                                Configuration.Config["Assets:FileSystem:ImageSavePath"]);
                            break;
                        case "S3":
                            await DownloadFileToS3(url, Configuration.Config["Assets:S3:ImageSavePath"]);
                            break;
                    }
                }
                catch (Exception e)
                {
                    LogTo.Warning(e, $"Failed to download image {url}");
                }
            }));
        }

        private async Task DownloadPostsFiles(IEnumerable<Post> posts)
        {
            var fileUrlList = posts.SelectMany(
                    t => t.Body.Files ?? t.Body.FileMap?.Values.ToList() ?? new List<Database.Models.File>(),
                    (_, d) => d.Url)
                .ToList();
            await Task.WhenAll(fileUrlList.Select(async url =>
            {
                try
                {
                    switch (Configuration.Config["Assets:Storage"])
                    {
                        case "FileSystem":
                            await DownloadFileToFileSystem(url, Configuration.Config["Assets:FileSystem:FileSavePath"]);
                            break;
                        case "S3":
                            await DownloadFileToS3(url, Configuration.Config["Assets:S3:FileSavePath"]);
                            break;
                    }
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
                list = (await Task.WhenAll(list.Select(async post =>
                {
                    post.Body = await FanboxApi.GetPostBody(post.Id);
                    return post;
                }))).Where(post => post.Body != null).ToList();

                var newPostsList = list.AsParallel().Where(t => !idList.Contains(t.Id)).ToList();
                if (newPostsList.Count != list.Count && Configuration.Config["Fanbox:FetchToEnd"] != "true")
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