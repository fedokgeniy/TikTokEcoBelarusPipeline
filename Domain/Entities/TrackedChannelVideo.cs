namespace TikTokEcoBelarus.Domain.Entities;

public class TrackedChannelVideo
{
    public Guid    Id               { get; set; } = Guid.NewGuid();
    public Guid    TrackedChannelId { get; set; }
    public string  VideoId          { get; set; } = string.Empty;  // TikTok video id
    public string? Description      { get; set; }
    public long    CommentCount     { get; set; }
    public long    LikeCount        { get; set; }
    public long    PlayCount        { get; set; }
    public long    ShareCount       { get; set; }
    public DateTimeOffset VideoCreatedAt { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    public TrackedChannel Channel  { get; set; } = default!;
    public ICollection<VideoComment> Comments { get; set; } = new List<VideoComment>();
}
