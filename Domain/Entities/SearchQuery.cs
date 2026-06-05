namespace TikTokEcoBelarus.Domain.Entities;

public class SearchQuery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string QueryType { get; set; } = string.Empty;   // hashtag | keyword | sound | user
    public string Value { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 0;
    public DateTime? LastRunAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VideoSearchQueryLink> VideoLinks { get; set; } = [];
}