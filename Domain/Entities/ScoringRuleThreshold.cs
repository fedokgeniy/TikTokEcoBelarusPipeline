namespace TikTokEcoBelarus.Domain.Entities;

public class ScoringRuleThreshold
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Category { get; set; } = string.Empty;
    public string ScoreType { get; set; } = string.Empty;
    public int MinMatchCount { get; set; }
    public decimal ScoreBonus { get; set; }
}