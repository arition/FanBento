using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Anotar.Serilog;
using FanBento.Database;
using FanBento.Database.Models;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FanBento.TelegramBot
{
    public class Worker
    {
        public Worker()
        {
            InitDatabase().Wait();
            TelegramBotClient = new TelegramBotClient(Configuration.Config["Telegram:BotToken"]);
        }

        private TelegramBotClient TelegramBotClient { get; }
        private FanBentoDatabase Database { get; set; }

        private async Task InitDatabase()
        {
            Database = new FanBentoDatabase(Configuration.Config["Database:ConnectionString"]);
            await Database.Database.EnsureCreatedAsync();
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
            var channelId = new ChatId(Configuration.Config["Telegram:ChannelId"]);
            var domain = Configuration.Config["FanBento.Website:Domain"];
            var markdownEscapeRegex = new Regex(@"[_\*\[\]\(\)~`>#+\-=|{}\.!]");
            foreach (var post in posts)
            {
                try
                {
                    var url = $"https://t.me/iv?url=https%3A%2F%2F{domain}" +
                              $"%2Fposts%2Fdetails%2F{post.Id}&rhash=4ccdcfde3b4311";
                    var title = markdownEscapeRegex.Replace(post.Title, "\\$&");
                    var author = markdownEscapeRegex.Replace(post.User.Name, "\\$&");
                    await TelegramBotClient.SendTextMessageAsync(
                        channelId, $"[{author} \\- {title}]({url})", ParseMode.MarkdownV2);

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
        }

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
        }
    }
}