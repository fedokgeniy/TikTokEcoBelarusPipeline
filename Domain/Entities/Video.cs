namespace TikTokEcoBelarus.Domain.Entities;

public class Video
{
    public string VideoId { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
    public string AuthorUniqueId { get; set; } = string.Empty;
    public string AuthorNickname { get; set; } = string.Empty;
    public string AuthorBio { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Hashtags { get; set; } = [];
    public long LikeCount { get; set; }
    public long CommentCount { get; set; }
    public long ShareCount { get; set; }
    public long ViewCount { get; set; }
    public decimal BelarusScore { get; set; }
    public decimal EcoScore { get; set; }
    public string ScoreBreakdown { get; set; } = "{}";
    public string[] MatchedKeywords { get; set; } = [];  
    public DateTime? PublishedAt { get; set; }            
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VideoSearchQueryLink> SearchQueryLinks { get; set; } = [];
}