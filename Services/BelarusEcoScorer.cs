using Microsoft.Extensions.Caching.Memory;
using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Models;

namespace TikTokEcoBelarus.Services;

public class BelarusEcoScorer(IScoringRuleRepository repo, IMemoryCache cache)
{
    private const string RulesCacheKey = "scoring_rules";
    private const string ThresholdsCacheKey = "scoring_thresholds";

    public async Task<ScoredVideo> ScoreAsync(TikTokItem item)
    {
        var rules = await GetRulesAsync();
        var thresholds = await GetThresholdsAsync();

        var scored = new ScoredVideo { Item = item };

        scored.BelarusScore = ComputeScore(item, "belarus", rules, thresholds, scored.BelarusSignals);
        scored.EcoScore = ComputeScore(item, "eco", rules, thresholds, scored.EcoSignals);

        return scored;
    }

    private static double ComputeScore(
        TikTokItem item,
        string scoreType,
        IList<ScoringRule> rules,
        IList<ScoringRuleThreshold> thresholds,
        List<string> signals)
    {
        double score = 0.0;

        // Группируем правила по категории
        var grouped = rules
            .Where(r => r.ScoreType == scoreType)
            .GroupBy(r => r.Category);

        foreach (var group in grouped)
        {
            var category = group.Key;
            var maxMatches = group.First().MaxMatches;
            var weight = group.First().Weight;
            int matches = 0;

            foreach (var rule in group)
            {
                if (matches >= maxMatches) break;

                var searchText = GetSearchContext(item, rule.SearchContext);
                if (searchText.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase))
                {
                    matches++;
                    signals.Add($"{category}:{rule.Keyword}");
                }
            }

            if (matches == 0) continue;

            // Проверяем есть ли threshold для этой категории
            var categoryThresholds = thresholds
                .Where(t => t.Category == category && t.ScoreType == scoreType)
                .OrderByDescending(t => t.MinMatchCount)
                .ToList();

            if (categoryThresholds.Count > 0)
            {
                // Stepped логика: берём наибольший подходящий бонус
                var applicable = categoryThresholds
                    .FirstOrDefault(t => matches >= t.MinMatchCount);
                if (applicable != null)
                    score += (double)applicable.ScoreBonus;
            }
            else
            {
                // Линейная логика: matches * weight
                score += matches * (double)weight;
            }
        }

        // Специальное правило: флаг 🇧🇾 в bio (только для belarus)
        if (scoreType == "belarus" && item.Author.Signature.Contains("🇧🇾"))
        {
            score += 0.35;
            signals.Add("flag:🇧🇾 in bio");
        }

        // Verified аккаунт усиливает belarus score
        if (scoreType == "belarus" && item.Author.Verified && score > 0.2)
        {
            score += 0.10;
            signals.Add("verified");
        }

        return Math.Min(score, 1.0);
    }

    private static string GetSearchContext(TikTokItem item, string context) => context switch
    {
        "hashtags" => string.Join(" ", item.AllHashtags),
        "bio" => item.Author.Signature,
        "description" => item.Desc,
        _ => string.Join(" ",  // "any"
                            item.Desc,
                            item.Author.Signature,
                            item.Author.Nickname,
                            item.Author.UniqueId,
                            string.Join(" ", item.AllHashtags))
    };

    private async Task<IList<ScoringRule>> GetRulesAsync()
        => await cache.GetOrCreateAsync(RulesCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return await repo.GetActiveRulesAsync();
        }) ?? [];

    private async Task<IList<ScoringRuleThreshold>> GetThresholdsAsync()
        => await cache.GetOrCreateAsync(ThresholdsCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return await repo.GetThresholdsAsync();
        }) ?? [];
}