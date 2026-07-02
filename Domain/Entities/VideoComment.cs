namespace TikTokEcoBelarus.Domain.Entities;

public class VideoComment
{
    public Guid   Id               { get; set; } = Guid.NewGuid();
    public string VideoId          { get; set; } = string.Empty;
    public string CommentId        { get; set; } = string.Empty;
    public string Text             { get; set; } = string.Empty;
    public string AuthorUniqueId   { get; set; } = string.Empty;
    public long   LikeCount        { get; set; }
    public DateTimeOffset CommentCreatedAt { get; set; }
    public DateTime FetchedAt      { get; set; } = DateTime.UtcNow;

    public TrackedChannelVideo Video { get; set; } = null!;
}
