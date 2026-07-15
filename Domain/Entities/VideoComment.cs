namespace TikTokEcoBelarus.Domain.Entities;

public class VideoComment
{
    public Guid   Id               { get; set; } = Guid.NewGuid();
    public string VideoId          { get; set; } = string.Empty;
    public string CommentId        { get; set; } = string.Empty;
    public string Text             { get; set; } = string.Empty;
    public string AuthorUniqueId   { get; set; } = string.Empty;
    public long   LikeCount        { get; set; }
    public long   ReplyCount       { get; set; }
    public DateTimeOffset CommentCreatedAt { get; set; }
    public DateTime FetchedAt      { get; set; } = DateTime.UtcNow;

    // --- AI classification ---
    /// <summary>null = not yet classified, true = relevant, false = not relevant.</summary>
    public bool?   IsRelevant      { get; set; }
    /// <summary>Comma-separated tags returned by Claude Haiku, e.g. "pollution,river".</summary>
    public string? Tags            { get; set; }

    public TrackedChannelVideo Video { get; set; } = null!;
}
