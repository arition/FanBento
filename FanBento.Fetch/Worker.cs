using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Anotar.Serilog;
using FanBento.Database;
using FanBento.Database.Models;
using FanBento.Fetch.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using MimeTypes;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using File = System.IO.File;

namespace FanBento.Fetch;

public class Worker : IDisposable
{
    /// <summary>Creates FANBOX access only for fetch runs; translation refresh needs no FANBOX cookie.</summary>
    public Worker()
    {
        InitDatabase().Wait();
        if (!Configuration.Config.GetValue("Translation:Refresh", false))
            FanboxApi = new FanboxApi(Configuration.Config["Fanbox:FanboxSessionId"]);
    }

    private FanboxApi FanboxApi { get; }
    private FanBentoDatabase Database { get; set; }
    private IMinioClient S3Client { get; set; }

    public void Dispose()
    {
        FanboxApi?.Dispose();
        Database?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Opens the database and adds translation storage when needed.</summary>
    private async Task InitDatabase()
    {
        Database = new FanBentoDatabase(Configuration.Config["Database:ConnectionString"]);
        await Database.InitializeAsync();
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
            if (length.HasValue)
            {
                await DownloadFileToS3(httpStream, length.Value, mimeType, $"{destinationPath}/{fileName}");
                return;
            }

            var tempFilePath = Path.GetTempFileName();
            try
            {
                await using (var tempFileStream = File.Create(tempFilePath))
                    await httpStream.CopyToAsync(tempFileStream);

                await using var uploadFileStream = File.OpenRead(tempFilePath);
                await DownloadFileToS3(uploadFileStream, uploadFileStream.Length, mimeType,
                    $"{destinationPath}/{fileName}");
            }
            finally
            {
                File.Delete(tempFilePath);
            }
        }
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

    private async Task<List<Post>> FetchNewPosts(FanBentoDatabase database)
    {
        var posts = new List<Post>();
        var fetchedPostCount = 0;
        var fetchLimit = int.TryParse(Configuration.Config["Fanbox:FetchLimit"], out var configuredFetchLimit)
            ? configuredFetchLimit
            : (int?)null;
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
            if (fetchLimit is > 0)
            {
                var remainingPostCount = fetchLimit.Value - fetchedPostCount;
                if (remainingPostCount <= 0) break;

                list = list.Take(remainingPostCount).ToList();
            }

            var postsWithBody = new List<Post>();
            foreach (var post in list)
            {
                post.Body = await FanboxApi.GetPostBody(post.Id);
                await Task.Delay(5000); // delay to avoid hitting API rate limit
                if (post.Body != null) postsWithBody.Add(post);
            }

            list = postsWithBody;
            fetchedPostCount += list.Count;

            var newPostsList = list.AsParallel().Where(t => !idList.Contains(t.Id)).ToList();
            LogTo.Debug($"Fetched {list.Count} posts, {newPostsList.Count} new posts");
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
            posts.AddRange(newPostsList);
            if (fetchLimit is > 0 && fetchedPostCount >= fetchLimit.Value) hasNextPage = false;
        } while (hasNextPage);

        return posts;
    }

    /// <summary>Saves originals and queues translations only for posts first seen while translation is enabled.</summary>
    public async Task AddToDatabase(List<Post> posts)
    {
        // Existing posts are not backfilled during ordinary fetching.
        var translationEnabled = Configuration.Config.GetValue("Translation:Enabled", false);
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
                if (translationEnabled)
                    Database.PostTranslation.Add(new PostTranslation { PostId = post.Id, Post = post });
            }
        }

        // get existing users in database
        var usersInDatabase = await Database.User.AsNoTracking().ToListAsync();

        // Mark users that already existed in database as modified, no matter it is actually modified or not
        foreach (var user in users.Where(user => usersInDatabase.Any(t => t.UserId == user.UserId)))
            Database.Entry(user).State = EntityState.Modified;

        await Database.SaveChangesAsync();
    }

    /// <summary>Fetches original posts, or runs a filtered refresh without contacting FANBOX.</summary>
    public async Task WorkOnce()
    {
        try
        {
            var refresh = Configuration.Config.GetValue("Translation:Refresh", false);
            if (refresh)
            {
                await RefreshTranslations();
            }
            else
            {
                var posts = await FetchNewPosts(Database);
                await AddToDatabase(posts);
            }
        }
        catch (Exception e)
        {
            if (LogTo.IsErrorEnabled) LogTo.Error(e, "Error in worker");
            if (Configuration.Config.GetValue("Translation:Refresh", false)) Environment.ExitCode = 1;
        }

        // Retry saved work even when this run could not fetch new originals.
        if (!Configuration.Config.GetValue("Translation:Refresh", false) &&
            Configuration.Config.GetValue("Translation:Enabled", false))
        {
            try
            {
                await TranslatePendingPosts();
            }
            catch (Exception e)
            {
                LogTo.Warning($"Translation retries could not start ({e.GetType().Name}).");
            }
        }
    }

    /// <summary>Loads original text with stable block order and link destinations for translation.</summary>
    private IQueryable<Post> TranslationPosts() => Database.Post.AsNoTracking()
        .Include(post => post.Body).ThenInclude(body => body.Blocks).ThenInclude(block => block.Links);

    /// <summary>Retries only records queued by an enabled fetch or an explicit refresh.</summary>
    private async Task TranslatePendingPosts()
    {
        using var translator = new PostTranslator(Configuration.Config);
        // Read IDs first so successful records can be saved one at a time.
        var ids = await Database.PostTranslation.Where(t => t.Language == "zh-cn" && t.Status != "Completed")
            .Select(t => t.PostId).ToListAsync();
        foreach (var id in ids)
        {
            var post = await TranslationPosts().SingleAsync(post => post.Id == id);
            var translation = await Database.PostTranslation.FindAsync(id, "zh-cn");
            await TranslatePost(translator, post, translation);
        }
    }

    /// <summary>Refreshes stored originals within a published-date range or creator filter.</summary>
    private async Task RefreshTranslations()
    {
        // Dates use the same wall-clock values as Post.PublishedDatetime; no local timezone conversion is applied.
        var from = DateTime.TryParse(Configuration.Config["Translation:From"], out var fromDate) ? fromDate : (DateTime?)null;
        var to = DateTime.TryParse(Configuration.Config["Translation:To"], out var toDate) ? toDate : (DateTime?)null;
        var creatorId = Configuration.Config["Translation:CreatorId"]?.Trim();
        if (from == null && to == null && string.IsNullOrWhiteSpace(creatorId))
            throw new ArgumentException("Translation refresh requires From, To, or CreatorId.");
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
            throw new ArgumentException("Translation:From must be earlier than Translation:To.");
        using var translator = new PostTranslator(Configuration.Config);
        var query = TranslationPosts();
        if (from.HasValue) query = query.Where(post => post.PublishedDatetime >= from.Value);
        if (to.HasValue) query = query.Where(post => post.PublishedDatetime < to.Value);
        if (!string.IsNullOrWhiteSpace(creatorId)) query = query.Where(post => post.CreatorId == creatorId);
        var ids = await query.OrderBy(post => post.PublishedDatetime).Select(post => post.Id).ToListAsync();
        foreach (var id in ids)
        {
            var post = await TranslationPosts().SingleAsync(post => post.Id == id);
            var translation = await Database.PostTranslation.FindAsync(id, "zh-cn");
            if (translation == null)
            {
                translation = new PostTranslation { PostId = id };
                Database.PostTranslation.Add(translation);
            }
            translation.Status = "Pending";
            await Database.SaveChangesAsync();
            await TranslatePost(translator, post, translation);
        }
    }

    /// <summary>Saves each result independently and keeps the last successful text if a refresh fails.</summary>
    private async Task TranslatePost(PostTranslator translator, Post post, PostTranslation translation)
    {
        translation.LastAttemptUtc = DateTime.UtcNow;
        try
        {
            // Replace content only after the whole response passes validation.
            var content = await translator.TranslateAsync(post);
            translation.ContentJson = content;
            translation.Status = "Completed";
            LogTo.Information($"Translated post {post.Id} to zh-cn");
        }
        catch (Exception e)
        {
            translation.Status = "Failed";
            LogTo.Warning($"Translation failed for post {post.Id} ({e.GetType().Name}); it will be retried.");
        }
        await Database.SaveChangesAsync();
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