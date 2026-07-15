namespace TikTokEcoBelarus.Models;

public class TikTokComment
{
    public string        CommentId      { get; set; } = string.Empty;
    public string        Text           { get; set; } = string.Empty;
    public string        AuthorUniqueId { get; set; } = string.Empty;
    public long          LikeCount      { get; set; }
    /// <summary>Число ответов на комментарий (reply_comment_total из API).</summary>
    public long          ReplyCount     { get; set; }
    public DateTimeOffset CreatedAt     { get; set; }
}
