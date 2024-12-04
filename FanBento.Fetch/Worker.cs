using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Anotar.Serilog;
using FanBento.Database;
using FanBento.Database.Models;
using FanBento.Fetch.Api;
using Microsoft.EntityFrameworkCore;
using MimeTypes;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using File = System.IO.File;

namespace FanBento.Fetch;

public class Worker
{
    public Worker()
    {
        InitDatabase().Wait();
        FanboxApi = new FanboxApi(Configuration.Config["Fanbox:FanboxSessionId"]);
        AoiroboxApi = new AoiroboxApi();
    }

    private FanboxApi FanboxApi { get; }
    private AoiroboxApi AoiroboxApi { get; }
    private FanBentoDatabase Database { get; set; }
    private IMinioClient S3Client { get; set; }

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

    private async Task DownloadFileFromFanboxToS3(string url, string destinationPath)
    {
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        var mimeType = MimeTypeMap.GetMimeType(Path.GetExtension(fileName)[1..]);
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Cannot extract filename from url");
        if (!await CheckIfFileExistsOnS3($"{destinationPath}/{fileName}"))
        {
            LogTo.Information($"Downloading file {url}");
            var (stream, length) = await FanboxApi.GetDownloadFileStream(url);
            await using var httpStream = stream;
            var httpStreamLength = length.Value;

            await DownloadFileToS3(httpStream, httpStreamLength, mimeType, $"{destinationPath}/{fileName}");
        }
    }

    [Obsolete]
    private async Task DownloadFileFromAoiroboxToS3(string fileName, Stream stream, string destinationPath)
    {
        var mimeType = MimeTypeMap.GetMimeType(Path.GetExtension(fileName)[1..]);
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Cannot extract filename from url");

        await using var httpStream = stream;
        var httpStreamLength = stream.Length;

        await DownloadFileToS3(httpStream, httpStreamLength, mimeType, $"{destinationPath}/{fileName}");
    }

    private async Task<bool> CheckIfFileExistsOnS3(string destinationPath)
    {
        S3Client ??= new MinioClient()
            .WithEndpoint(Configuration.Config["Assets:S3:EndPoint"])
            .WithCredentials(Configuration.Config["Assets:S3:KeyId"],
                Configuration.Config["Assets:S3:KeySecret"])
            .WithSSL()
            .Build();

        var statObjectArgs = new StatObjectArgs()
            .WithBucket(Configuration.Config["Assets:S3:Bucket"])
            .WithObject($"{destinationPath}");

        try
        {
            await S3Client.StatObjectAsync(statObjectArgs);
            return true;
        }
        catch (MinioException)
        {
        }

        return false;
    }

    private async Task DownloadFileToS3(Stream stream, long length, string mimeType, string destinationPath)
    {
        S3Client ??= new MinioClient()
            .WithEndpoint(Configuration.Config["Assets:S3:EndPoint"])
            .WithCredentials(Configuration.Config["Assets:S3:KeyId"],
                Configuration.Config["Assets:S3:KeySecret"])
            .WithSSL()
            .Build();

        await using var httpStream = stream;
        var putObjectArgs = new PutObjectArgs()
            .WithBucket(Configuration.Config["Assets:S3:Bucket"])
            .WithObject($"{destinationPath}")
            .WithStreamData(httpStream)
            .WithObjectSize(length)
            .WithContentType(mimeType);
        await S3Client.PutObjectAsync(putObjectArgs);
    }

    private async Task DownloadPostsImages(IEnumerable<Post> posts)
    {
        var imageUrlList = posts.SelectMany(
                t => t.Body.Images ?? t.Body.ImageMap?.Values.ToList() ?? [],
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
                        await DownloadFileFromFanboxToS3(url, Configuration.Config["Assets:S3:ImageSavePath"]);
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
                t => t.Body.Files ?? t.Body.FileMap?.Values.ToList() ?? [],
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
                        await DownloadFileFromFanboxToS3(url, Configuration.Config["Assets:S3:FileSavePath"]);
                        break;
                }
            }
            catch (Exception e)
            {
                LogTo.Warning(e, $"Failed to download file {url}");
            }
        }));
    }

    [Obsolete]
    private async Task<List<Post>> ReplaceAoiroboxUrl(List<Post> postsList)
    {
        foreach (var post in postsList)
        {
            if (post.Body?.Blocks == null) continue;
            var newBlocks = new List<Block>();
            foreach (var bodyBlock in post.Body.Blocks)
            {
                if (string.IsNullOrEmpty(bodyBlock.Text) ||
                    !Regex.IsMatch(bodyBlock.Text, @"https?:\/\/aoirobox\.sakura\.ne\.jp\S+"))
                {
                    newBlocks.Add(bodyBlock);
                    continue;
                }

                var url = Regex.Match(bodyBlock.Text, @"https?:\/\/aoirobox\.sakura\.ne\.jp\S+").Value;
                try
                {
                    var resultList = await AoiroboxApi.GetDownloadFileStream(url);
                    var newAoiroBoxBlocks = await Task.WhenAll(resultList.Select(async result =>
                    {
                        var (realUrl, stream, type) = result;

                        var fileName = Path.GetFileName(new Uri(realUrl).LocalPath);
                        var extension = Path.GetExtension(fileName)[1..];
                        var fileNameOnly = Guid.NewGuid().ToString("N");

                        var newBodyBlock = new Block
                        {
                            Type = type
                        };
                        switch (type)
                        {
                            case "image":
                                newBodyBlock.ImageId = fileNameOnly;
                                post.Body.ImageMap ??= [];
                                post.Body.ImageMap.Add(newBodyBlock.ImageId, new Image
                                {
                                    Id = fileNameOnly,
                                    Extension = extension
                                });
                                break;
                            case "file":
                                newBodyBlock.FileId = fileNameOnly;
                                post.Body.FileMap ??= [];
                                post.Body.FileMap.Add(newBodyBlock.FileId, new Database.Models.File
                                {
                                    Id = fileNameOnly,
                                    Name = fileNameOnly,
                                    Extension = extension
                                });
                                break;
                        }

                        await DownloadFileFromAoiroboxToS3($"{fileNameOnly}.{extension}", stream,
                            Configuration.Config["Assets:S3:ImageSavePath"]);

                        return newBodyBlock;
                    }));
                    newBlocks.AddRange(newAoiroBoxBlocks);
                }
                catch (Exception e)
                {
                    // do not trigger browser fetch repeatedly
                    LogTo.Error(e, $"Failed to download from aoirobox {url}");
                }
            }

            post.Body.Blocks = newBlocks;
        }

        return postsList;
    }

    private async Task<List<Post>> FetchNewPosts(FanBentoDatabase database)
    {
        var posts = new List<Post>();
        var hasNextPage = false;
        var idList = database.Post.Select(t => t.Id).ToHashSet();

        do
        {
            (var list, hasNextPage) = Configuration.Config["Fanbox:Author"] == null
                ? await FanboxApi.GetPostsList(
                    hasNextPage
                )
                : await FanboxApi.GetPostsListFromAuthor(
                    Configuration.Config["Fanbox:Author"],
                    hasNextPage
                );
            list = (await Task.WhenAll(list.Select(async post =>
            {
                post.Body = await FanboxApi.GetPostBody(post.Id);
                return post;
            }))).Where(post => post.Body != null).ToList();

            var newPostsList = list.AsParallel().Where(t => !idList.Contains(t.Id)).ToList();
            if (newPostsList.Count != list.Count && Configuration.Config["Fanbox:FetchToEnd"] != "true")
                // some posts already exists, next page should all be old posts
                hasNextPage = false;
            if (Configuration.Config["Fanbox:FetchToEnd"] == "true" ||
                Configuration.Config["Fanbox:FetchToEnd"] == "reAdd")
                // always fetch all posts
                newPostsList = list;

            // always check and download the files from the full posts list
            await DownloadPostsImages(list);
            await DownloadPostsFiles(list);
            // newPostsList = await ReplaceAoiroboxUrl(newPostsList);
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

            var dbPost = await Database.Post.AsNoTracking().Where(p => p.Id == post.Id).FirstOrDefaultAsync();
            if (dbPost != null)
            {
                post.SentToTelegramChannel = dbPost.SentToTelegramChannel;
                Database.Post.Update(post);

                if (post.Body.Files != null)
                    foreach (var bodyFile in post.Body.Files.Where(bodyFile =>
                                 !Database.File.Any(file => file.Id == bodyFile.Id)))
                        Database.Entry(bodyFile).State = EntityState.Added;

                if (post.Body.Images != null)
                    foreach (var bodyImage in post.Body.Images.Where(bodyImage =>
                                 !Database.Image.Any(image => image.Id == bodyImage.Id)))
                        Database.Entry(bodyImage).State = EntityState.Added;
            }
            else
            {
                Database.Post.Add(post);
            }
        }

        // get existing users in database
        var usersInDatabase = await Database.User.AsNoTracking().ToListAsync();

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
            if (x is null) return false;
            if (y is null) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.UserId == y.UserId;
        }

        public int GetHashCode(User obj)
        {
            return obj.UserId != null ? obj.UserId.GetHashCode() : 0;
        }
    }
}