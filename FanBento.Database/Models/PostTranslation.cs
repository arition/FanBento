using System;

namespace FanBento.Database.Models;

/// <summary>Stores translated text and delivery state without copying the post's media.</summary>
public class PostTranslation
{
    // The original post and language identify one translation.
    public string PostId { get; set; }
    public string Language { get; set; } = "zh-cn";
    public Post Post { get; set; }

    // Null content means this post has never been translated successfully.
    public string ContentJson { get; set; }

    // Pending and Failed records are retried on the next enabled fetch run.
    public string Status { get; set; } = "Pending";
    public DateTime? LastAttemptUtc { get; set; }

    // Refreshing the text leaves this flag unchanged.
    public bool SentToTelegramChannel { get; set; }
}
