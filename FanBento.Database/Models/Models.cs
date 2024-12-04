using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FanBento.Database.Models;

public class Styles
{
    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("length")] public int Length { get; set; }
}

public class Block : IOrder
{
    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; }
    [JsonPropertyName("imageId")] public string ImageId { get; set; }
    [JsonPropertyName("embedId")] public string EmbedId { get; set; }
    [JsonPropertyName("urlEmbedId")] public string UrlEmbedId { get; set; }
    [JsonPropertyName("fileId")] public string FileId { get; set; }
    [JsonPropertyName("styles")] public List<Styles> Styles { get; set; }
    [JsonPropertyName("links")] public List<Link> Links { get; set; }
    [JsonIgnore] public int Order { get; set; }
}

public class File : IOrder
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("extension")] public string Extension { get; set; }
    [JsonPropertyName("size")] public int Size { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; }
    [JsonIgnore] public int Order { get; set; }
}

public class Embed : IOrder
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("serviceProvider")] public string ServiceProvider { get; set; }
    [JsonPropertyName("contentId")] public string ContentId { get; set; }
    [JsonIgnore] public int Order { get; set; }
}

public class UrlEmbed : IOrder
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("host")] public string Host { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; }
    [JsonPropertyName("html")] public string Html { get; set; }
    [JsonIgnore] public int Order { get; set; }
}

public class Image : IOrder
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("extension")] public string Extension { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("originalUrl")] public string OriginalUrl { get; set; }
    [JsonPropertyName("thumbnailUrl")] public string ThumbnailUrl { get; set; }
    [JsonIgnore] public int Order { get; set; }
}

public class Link : IOrder
{
    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("length")] public int Length { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; }
    [JsonIgnore] public int Order { get; set; }
}

/// <summary>
///     There are two types of ContentBody: one consists of {Blocks, ImageMap, FileMap, EmbedMap, UrlEmbedMap},
///     the other consists of {Text, Images}. Please check Blocks is null or not before interact with the object.
/// </summary>
public class ContentBody
{
    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("blocks")] public List<Block> Blocks { get; set; }
    [JsonPropertyName("imageMap")] public Dictionary<string, Image> ImageMap { get; set; }
    [JsonPropertyName("fileMap")] public Dictionary<string, File> FileMap { get; set; }
    [JsonPropertyName("embedMap")] public Dictionary<string, Embed> EmbedMap { get; set; }
    [JsonPropertyName("urlEmbedMap")] public Dictionary<string, UrlEmbed> UrlEmbedMap { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; }
    [JsonPropertyName("images")] public List<Image> Images { get; set; }
    [JsonPropertyName("files")] public List<File> Files { get; set; }
}

public class User
{
    [JsonPropertyName("userId")] public string UserId { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("iconUrl")] public string IconUrl { get; set; }
}

public class Post
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; }
    [JsonPropertyName("coverImageUrl")] public string CoverImageUrl { get; set; }
    [JsonPropertyName("feeRequired")] public int FeeRequired { get; set; }

    [JsonPropertyName("publishedDatetime")]
    public DateTime PublishedDatetime { get; set; }

    [JsonPropertyName("updatedDatetime")] public DateTime UpdatedDatetime { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("body")] public ContentBody Body { get; set; }
    [JsonPropertyName("tags")] public List<string> Tags { get; set; }
    [JsonPropertyName("excerpt")] public string Excerpt { get; set; }
    [JsonPropertyName("isLiked")] public bool IsLiked { get; set; }
    [JsonPropertyName("likeCount")] public int LikeCount { get; set; }
    [JsonPropertyName("commentCount")] public int CommentCount { get; set; }
    [JsonPropertyName("user")] public User User { get; set; }
    [JsonPropertyName("creatorId")] public string CreatorId { get; set; }
    [JsonPropertyName("hasAdultContent")] public bool HasAdultContent { get; set; }
    [JsonIgnore] public bool SentToTelegramChannel { get; set; }
}

public class ListHomeResponseRoot
{
    [JsonPropertyName("body")] public ListHomeResponseBody Body { get; set; }
}

public class PostResponseRoot
{
    [JsonPropertyName("body")] public Post Body { get; set; }
}

public class ListHomeResponseBody
{
    [JsonPropertyName("items")] public List<Post> Posts { get; set; }
    [JsonPropertyName("nextUrl")] public string NextUrl { get; set; }
}
public class ListCreatorResponseRoot
{
    [JsonPropertyName("body")] public List<Post> Body { get; set; }
}