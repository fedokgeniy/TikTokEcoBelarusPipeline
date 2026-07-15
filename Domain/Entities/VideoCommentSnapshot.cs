namespace TikTokEcoBelarus.Domain.Entities;

/// <summary>
/// Хранит снапшот числа комментариев под видео в конкретный момент времени.
/// Позволяет строить динамику роста: (VideoId, SnapshotAt) → CommentCount.
/// </summary>
public class VideoCommentSnapshot
{
    public Guid           Id           { get; set; } = Guid.NewGuid();
    public string         VideoId      { get; set; } = string.Empty;
    public DateTimeOffset SnapshotAt   { get; set; } = DateTimeOffset.UtcNow;
    public long           CommentCount { get; set; }

    public TrackedChannelVideo Video { get; set; } = null!;
}
