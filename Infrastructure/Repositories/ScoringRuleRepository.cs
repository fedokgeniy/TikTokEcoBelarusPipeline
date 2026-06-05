using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

public class ScoringRuleRepository(AppDbContext db) : IScoringRuleRepository
{
    public async Task<IList<ScoringRule>> GetActiveRulesAsync()
        => await db.ScoringRules
            .Where(r => r.IsActive)
            .ToListAsync();

    public async Task<IList<ScoringRuleThreshold>> GetThresholdsAsync()
        => await db.ScoringRuleThresholds
            .ToListAsync();
}