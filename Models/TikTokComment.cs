namespace TikTokEcoBelarus.Models;

public class TikTokComment
{
    public string CommentId      { get; set; } = string.Empty;
    public string Text           { get; set; } = string.Empty;
    public string AuthorUniqueId { get; set; } = string.Empty;
    public long   LikeCount      { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
