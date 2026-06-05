using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Models;
using TikTokEcoBelarus.Services;

namespace TikTokEcoBelarus.Pipeline;

public class CollectionPipeline(
    TikTokApiClient api,
    BelarusEcoScorer scorer,
    ISearchQueryRepository searchQueryRepo)
{
    public async Task<List<(ScoredVideo Scored, Guid QueryId)>> RunAsync(
        double minBelarus = 0.3,
        double minEco = 0.3,
        int maxPerQuery = 5,
        CancellationToken ct = default)
    {
        var queries = await searchQueryRepo.GetActiveQueriesAsync();
        var seen = new HashSet<string>();
        var results = new List<(ScoredVideo Scored, Guid QueryId)>();

        foreach (var query in queries.OrderBy(q => q.Priority))
        {
            Console.WriteLine($"[SEARCH] Querying: \"{query.Value}\" (type: {query.QueryType})");

            if (query.DateFrom.HasValue)
                Console.WriteLine($"[FILTER] Видео не старше: {query.DateFrom.Value:yyyy-MM-dd}");

            await foreach (var item in api.SearchVideosAsync(query.Value, maxPerQuery, ct: ct))
            {
                if (item.IsAd || item.Author.PrivateAccount)
                    continue;

                if (query.DateFrom.HasValue &&
                    item.CreatedAt.UtcDateTime < query.DateFrom.Value.ToUniversalTime())
                {
                    Console.WriteLine($"[SKIP] Видео {item.Id} старше DateFrom ({item.CreatedAt:yyyy-MM-dd})");
                    continue;
                }

                var scored = await scorer.ScoreAsync(item);

                if (!scored.PassesThreshold(minBelarus, minEco))
                    continue;

                results.Add((scored, query.Id));

                if (seen.Add(item.Id))
                    Console.WriteLine(
                        $"  [{scored.TotalScore:F2}] BY={scored.BelarusScore:F2} " +
                        $"ECO={scored.EcoScore:F2} | @{item.Author.UniqueId}: " +
                        $"{item.Desc[..Math.Min(60, item.Desc.Length)]}...");
            }

            await searchQueryRepo.UpdateLastRunAtAsync(query.Id);
        }

        return results.OrderByDescending(r => r.Scored.TotalScore).ToList();
    }
}