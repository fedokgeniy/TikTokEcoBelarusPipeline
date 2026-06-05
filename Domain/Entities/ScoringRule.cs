namespace TikTokEcoBelarus.Domain.Entities;

public class ScoringRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Keyword { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;   // Belarus_Explicit, Eco_Hashtag, ...
    public string ScoreType { get; set; } = string.Empty;  // belarus | eco
    public decimal Weight { get; set; }
    public string SearchContext { get; set; } = "any";     // any | hashtags | bio | description
    public int MaxMatches { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}