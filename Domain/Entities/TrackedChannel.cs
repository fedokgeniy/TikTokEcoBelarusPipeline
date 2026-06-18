namespace TikTokEcoBelarus.Domain.Entities;

public class TrackedChannel
{
    public Guid    Id               { get; set; } = Guid.NewGuid();
    public string  UniqueId         { get; set; } = string.Empty;  // @username TikTok
    public string? DisplayName      { get; set; }
    public string? AvatarUrl        { get; set; }
    public bool    IsActive         { get; set; } = true;
    public int?    LastVideoCount   { get; set; }   // кол-во видео при последней проверке
    public int?    LastCommentCount { get; set; }   // суммарный CommentCount по последним видео
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TrackedChannelVideo> Videos { get; set; } = [];
}
