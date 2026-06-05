using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

public interface IScoringRuleRepository
{
    Task<IList<ScoringRule>> GetActiveRulesAsync();
    Task<IList<ScoringRuleThreshold>> GetThresholdsAsync();
}