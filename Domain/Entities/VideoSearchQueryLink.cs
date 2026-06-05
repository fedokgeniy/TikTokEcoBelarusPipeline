namespace TikTokEcoBelarus.Domain.Entities;

public class VideoSearchQueryLink
{
    public string VideoId { get; set; } = string.Empty;
    public Guid SearchQueryId { get; set; }

    public Video Video { get; set; } = null!;
    public SearchQuery SearchQuery { get; set; } = null!;
}