using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using Anotar.Serilog;
using FanBento.Database;
using FanBento.Database.Models;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FanBento.TelegramBot;

public partial class Worker
{
    [GeneratedRegex(@"[_\*\[\]\(\)~`>#+\-=|{}\.!]")]
    private static partial Regex MarkdownEscapeRegex();

    public Worker()
    {
        InitDatabase().Wait();
        TelegramBotClient = new TelegramBotClient(Configuration.Config["Telegram:BotToken"]);
    }

    private TelegramBotClient TelegramBotClient { get; }
    private FanBentoDatabase Database { get; set; }

    /// <summary>Opens original and translated delivery storage.</summary>
    private async Task InitDatabase()
    {
        Database = new FanBentoDatabase(Configuration.Config["Database:ConnectionString"]);
        await Database.InitializeAsync();
    }

    private async Task<List<Post>> FetchNewPosts()
    {
        var posts = await Database.Post
            .Include(t => t.User)
            .Where(t => !t.SentToTelegramChannel)
            .OrderBy(t => t.PublishedDatetime)
            .ToListAsync();
        return posts;
    }

    public async Task SendToTelegramChannel(List<Post> posts)
    {
        var channelId = new ChatId(Configuration.Config["Telegram:ChannelId"] ??
                                   throw new ArgumentException("Missing Telegram:ChannelId"));
        var alternativeTelegramCreatorIdSet = ReadStringSet("AlternativeTelegram:CreatorIdList");
        var alternativeChannelId = Configuration.Config["AlternativeTelegram:ChannelId"] != null
            ? new ChatId(Configuration.Config["AlternativeTelegram:ChannelId"])
            : channelId;
        var domain = Configuration.Config["FanBento.Website:Domain"];
        var blockFreeCreatorIdSet = ReadStringSet("Telegram:BlockFreeCreatorIdList");
        var markdownEscapeRegex = MarkdownEscapeRegex();
        foreach (var post in posts)
            try
            {
                var isFreePost = post.FeeRequired <= 0;
                var isBlockedFreeAuthor = isFreePost && (
                    (!string.IsNullOrWhiteSpace(post.CreatorId) && blockFreeCreatorIdSet.Contains(post.CreatorId)) ||
                    (!string.IsNullOrWhiteSpace(post.User?.UserId) && blockFreeCreatorIdSet.Contains(post.User.UserId))
                );

                if (isBlockedFreeAuthor)
                {
                    post.SentToTelegramChannel = true;
                    await Database.SaveChangesAsync();
                    LogTo.Information($"Skipped free post {post.Id} by blocked author ({post.User?.UserId ?? post.CreatorId})");
                    continue;
                }

                var currentChannelId = channelId;
                if (alternativeTelegramCreatorIdSet.Contains(post.User.UserId))
                    currentChannelId = alternativeChannelId;

                var url = $"https://t.me/iv?url=https%3A%2F%2F{domain}" +
                          $"%2Fposts%2Fdetails%2F{post.Id}&rhash=4ccdcfde3b4311";
                var title = markdownEscapeRegex.Replace(post.Title, "\\$&");
                var author = markdownEscapeRegex.Replace(post.User.Name, "\\$&");
                await TelegramBotClient.SendMessage(
                    currentChannelId, $"[{author} \\- {title}]({url})", parseMode: ParseMode.MarkdownV2);

                post.SentToTelegramChannel = true;
                await Database.SaveChangesAsync();

                LogTo.Information($"Sent post {post.Id} - {post.Title}");
                await Task.Delay(2000);
            }
            catch (Exception e)
            {
                if (LogTo.IsWarningEnabled) LogTo.Warning(e, $"Error sending post {post.Id} - {post.Title}");
            }
    }

    private static HashSet<string> ReadStringSet(string key)
    {
        // Supports both arrays and legacy comma-separated strings.
        var section = Configuration.Config.GetSection(key);
        var fromArray = section.GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (fromArray.Count > 0)
            return fromArray;

        var fromScalar = Configuration.Config[key];
        if (string.IsNullOrWhiteSpace(fromScalar))
            return new HashSet<string>(StringComparer.Ordinal);

        return fromScalar.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Publishes originals and translations independently so one channel's failure cannot block the other.</summary>
    public async Task WorkOnce()
    {
        try
        {
            var posts = await FetchNewPosts();
            await SendToTelegramChannel(posts);
        }
        catch (Exception e)
        {
            if (LogTo.IsErrorEnabled) LogTo.Error(e, "Error in worker");
        }

        try
        {
            await SendTranslations();
        }
        catch (Exception e)
        {
            if (LogTo.IsErrorEnabled) LogTo.Error(e, "Error publishing translations");
        }
    }

    /// <summary>Sends saved Chinese titles to the optional translated channel and records each successful delivery.</summary>
    private async Task SendTranslations()
    {
        // An omitted channel leaves translated posts unsent for a later configured run.
        var configuredChannel = Configuration.Config["Telegram:TranslatedChannelId"];
        if (string.IsNullOrWhiteSpace(configuredChannel)) return;
        var channel = new ChatId(configuredChannel);
        var domain = Configuration.Config["FanBento.Website:Domain"];
        var blockedCreators = ReadStringSet("Telegram:BlockFreeCreatorIdList");
        var translations = await Database.PostTranslation.Include(t => t.Post).ThenInclude(post => post.User)
            .Where(t => t.Language == "zh-cn" && t.ContentJson != null && !t.SentToTelegramChannel)
            .OrderBy(t => t.Post.PublishedDatetime).ToListAsync();
        foreach (var translation in translations)
        {
            var post = translation.Post;
            try
            {
                // Match original-channel exclusions, including legacy user-ID entries.
                var blocked = post.FeeRequired <= 0 &&
                    ((!string.IsNullOrWhiteSpace(post.CreatorId) && blockedCreators.Contains(post.CreatorId)) ||
                     (!string.IsNullOrWhiteSpace(post.User?.UserId) && blockedCreators.Contains(post.User.UserId)));
                if (!blocked)
                {
                    var content = JsonSerializer.Deserialize<Dictionary<string, string>>(translation.ContentJson);
                    var title = MarkdownEscapeRegex().Replace(content["title"], "\\$&");
                    var author = MarkdownEscapeRegex().Replace(post.User.Name, "\\$&");
                    var pageUrl = $"https://{domain}/zh-cn/posts/details/{Uri.EscapeDataString(post.Id)}";
                    var url = $"https://t.me/iv?url={Uri.EscapeDataString(pageUrl)}&rhash=4ccdcfde3b4311";
                    await TelegramBotClient.SendMessage(channel, $"[{author} \\- {title}]({url})",
                        parseMode: ParseMode.MarkdownV2);
                    LogTo.Information($"Sent zh-cn post {post.Id}");
                }
                translation.SentToTelegramChannel = true;
                await Database.SaveChangesAsync();
                if (!blocked) await Task.Delay(2000);
            }
            catch (Exception e)
            {
                if (LogTo.IsWarningEnabled) LogTo.Warning(e, $"Error sending zh-cn post {post.Id}");
            }
        }
    }
}