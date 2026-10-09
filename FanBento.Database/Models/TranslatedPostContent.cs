using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FanBento.Database.Models;

/// <summary>Maps plain text to stable keys and rebuilds link offsets after translation.</summary>
public static class TranslatedPostContent
{
    /// <summary>Collects only text; media, author names, and link destinations stay local.</summary>
    public static Dictionary<string, string> GetTexts(Post post)
    {
        // Keys use saved block order so database IDs never enter the LLM request.
        var texts = new Dictionary<string, string>();
        AddText(texts, "title", post.Title);
        AddText(texts, "excerpt", post.Excerpt);
        AddText(texts, "body", post.Body.Text);
        foreach (var block in post.Body.Blocks ?? [])
        {
            // Split linked paragraphs so translated labels can retain their original URLs.
            var segments = GetSegments(block);
            for (var index = 0; index < segments.Count; index++)
                AddText(texts, $"block:{block.Order}:{index}", segments[index].Text);
        }
        return texts;
    }

    /// <summary>Rejects missing, extra, or empty results before any translated text is saved.</summary>
    public static void Validate(Post post, Dictionary<string, string> translated)
    {
        var source = GetTexts(post);
        if (translated == null || source.Count != translated.Count || source.Any(pair =>
                !translated.TryGetValue(pair.Key, out var value) || string.IsNullOrWhiteSpace(value)))
            throw new InvalidOperationException("Translation must contain a nonempty value for every source key.");
    }

    /// <summary>Applies plain text to a detached post and recalculates its link positions.</summary>
    public static void Apply(Post post, Dictionary<string, string> translated)
    {
        Validate(post, translated);
        post.Title = translated.GetValueOrDefault("title", post.Title);
        post.Excerpt = translated.GetValueOrDefault("excerpt", post.Excerpt);
        post.Body.Text = translated.GetValueOrDefault("body", post.Body.Text);
        foreach (var block in post.Body.Blocks ?? [])
        {
            var segments = GetSegments(block);
            // Offsets must count UTF-16 characters, matching FANBOX and C# string indexing.
            var text = new StringBuilder();
            var links = new List<Link>();
            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                var value = translated.GetValueOrDefault($"block:{block.Order}:{index}", segment.Text);
                if (segment.Url != null)
                    links.Add(new Link { Offset = text.Length, Length = value.Length, Url = segment.Url });
                text.Append(value);
            }
            if (block.Text != null) block.Text = text.ToString();
            block.Links = links;
        }
    }

    /// <summary>Collects text fields for translation. Skips blank fields that do not need an LLM response.</summary>
    private static void AddText(Dictionary<string, string> texts, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) texts.Add(key, value);
    }

    /// <summary>Checks link ranges and divides a block into linked and unlinked text.</summary>
    private static List<(string Text, string Url)> GetSegments(Block block)
    {
        var segments = new List<(string Text, string Url)>();
        if (block.Text == null) return segments;
        // Reject overlapping or out-of-range links rather than silently dropping them.
        var position = 0;
        foreach (var link in (block.Links ?? []).OrderBy(link => link.Offset))
        {
            if (link.Offset < position || link.Length <= 0 || link.Offset > block.Text.Length - link.Length)
                throw new InvalidOperationException("Invalid source link range.");
            if (link.Offset > position) segments.Add((block.Text[position..link.Offset], null));
            segments.Add((block.Text.Substring(link.Offset, link.Length), link.Url));
            position = link.Offset + link.Length;
        }
        if (position < block.Text.Length) segments.Add((block.Text[position..], null));
        return segments;
    }
}
