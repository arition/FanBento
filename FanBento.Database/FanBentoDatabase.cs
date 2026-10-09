using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FanBento.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace FanBento.Database;

public class FanBentoDatabase : DbContext
{
    private readonly string _connectionString;

    public FanBentoDatabase(string connectionString)
    {
        _connectionString = connectionString;
    }

    public FanBentoDatabase(DbContextOptions<FanBentoDatabase> options) : base(options)
    {
    }

    public DbSet<Block> Block { get; set; }
    public DbSet<File> File { get; set; }
    public DbSet<Embed> Embed { get; set; }
    public DbSet<UrlEmbed> UrlEmbed { get; set; }
    public DbSet<Image> Image { get; set; }
    public DbSet<ContentBody> ContentBody { get; set; }
    public DbSet<User> User { get; set; }
    public DbSet<Post> Post { get; set; }

    // Translation rows are separate from originals and have their own delivery state.
    public DbSet<PostTranslation> PostTranslation { get; set; }

    /// <summary>Creates new databases and adds translation storage to existing EnsureCreated databases.</summary>
    public async Task InitializeAsync()
    {
        await Database.EnsureCreatedAsync();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured) optionsBuilder.UseSqlite(_connectionString);
    }

    /// <summary>Maps original content and the separate per-language translation records.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PostTranslation>().HasKey(t => new { t.PostId, t.Language });
        modelBuilder.Entity<PostTranslation>().Property(t => t.Status).IsRequired();
        modelBuilder.Entity<PostTranslation>().HasOne(t => t.Post).WithMany()
            .HasForeignKey(t => t.PostId).OnDelete(DeleteBehavior.Cascade);

        // We do not need to query these dictionaries, so we just convert them to JSON.
        modelBuilder.Entity<ContentBody>()
            .Property(p => p.EmbedMap)
            .HasConversion(
                t => JsonSerializer.Serialize(t, new JsonSerializerOptions()),
                t => JsonSerializer.Deserialize<Dictionary<string, Embed>>(t, new JsonSerializerOptions()));
        modelBuilder.Entity<ContentBody>()
            .Property(p => p.UrlEmbedMap)
            .HasConversion(
                t => JsonSerializer.Serialize(t, new JsonSerializerOptions()),
                t => JsonSerializer.Deserialize<Dictionary<string, UrlEmbed>>(t, new JsonSerializerOptions()));
        modelBuilder.Entity<ContentBody>()
            .Property(p => p.FileMap)
            .HasConversion(
                t => JsonSerializer.Serialize(t, new JsonSerializerOptions()),
                t => JsonSerializer.Deserialize<Dictionary<string, File>>(t, new JsonSerializerOptions()));
        modelBuilder.Entity<ContentBody>()
            .Property(p => p.ImageMap)
            .HasConversion(
                t => JsonSerializer.Serialize(t, new JsonSerializerOptions()),
                t => JsonSerializer.Deserialize<Dictionary<string, Image>>(t, new JsonSerializerOptions()));
        modelBuilder.Entity<Post>()
            .Property(p => p.Tags)
            .HasConversion(
                t => JsonSerializer.Serialize(t, new JsonSerializerOptions()),
                t => JsonSerializer.Deserialize<List<string>>(t, new JsonSerializerOptions()));
    }
}