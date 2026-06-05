using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Models;
using TikTokEcoBelarus.Services;

namespace TikTokEcoBelarus.Pipeline;

public class CollectionPipeline
{
    private readonly TikTokApiClient _api;
    private readonly BelarusEcoScorer _scorer;
    private readonly ISearchQueryRepository _searchQueryRepo;

    public CollectionPipeline(
        TikTokApiClient api,
        BelarusEcoScorer scorer,
        ISearchQueryRepository searchQueryRepo)
    {
        _api = api;
        _scorer = scorer;
        _searchQueryRepo = searchQueryRepo;
    }

    public async Task<List<ScoredVideo>> RunAsync(
        double minBelarus = 0.3,
        double minEco = 0.3,
        int maxPerQuery = 5,
        CancellationToken ct = default)
    {
        var queries = await _searchQueryRepo.GetActiveQueriesAsync();

        var seen = new HashSet<string>();
        var results = new List<ScoredVideo>();

        foreach (var query in queries.OrderBy(q => q.Priority))
        {
            Console.WriteLine($"[SEARCH] Querying: \"{query.Value}\" (type: {query.QueryType})");

            if (query.DateFrom.HasValue)
                Console.WriteLine($"[FILTER] Видео не старше: {query.DateFrom.Value:yyyy-MM-dd}");

            await foreach (var item in _api.SearchVideosAsync(query.Value, maxPerQuery, ct: ct))
            {
                if (!seen.Add(item.Id) || item.IsAd)
                    continue;

                if (item.Author.PrivateAccount)
                    continue;

                // Фильтрация по дате публикации
                if (query.DateFrom.HasValue &&
                    item.CreatedAt.UtcDateTime < query.DateFrom.Value.ToUniversalTime())
                {
                    Console.WriteLine($"[SKIP] Видео {item.Id} старше DateFrom ({item.CreatedAt:yyyy-MM-dd})");
                    continue;
                }

                var scored = await _scorer.ScoreAsync(item);

                if (scored.PassesThreshold(minBelarus, minEco))
                {
                    results.Add(scored);
                    Console.WriteLine(
                        $"  [{scored.TotalScore:F2}] BY={scored.BelarusScore:F2} " +
                        $"ECO={scored.EcoScore:F2} | @{item.Author.UniqueId}: " +
                        $"{item.Desc[..Math.Min(60, item.Desc.Length)]}..."
                    );
                }
            }

            await _searchQueryRepo.UpdateLastRunAtAsync(query.Id);
        }

        return results
            .OrderByDescending(v => v.TotalScore)
            .ToList();
    }
}