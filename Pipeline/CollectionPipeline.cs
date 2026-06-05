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

            int fetched = 0;
            int skippedAd = 0;
            int skippedDate = 0;
            int scored = 0;
            int passed = 0;

            var scoreLog = new List<string>();

            await foreach (var item in api.SearchVideosAsync(query.Value, maxPerQuery, ct: ct))
            {
                fetched++;

                if (item.IsAd || item.Author.PrivateAccount)
                {
                    skippedAd++;
                    continue;
                }

                if (query.DateFrom.HasValue &&
                    item.CreatedAt.UtcDateTime < query.DateFrom.Value.ToUniversalTime())
                {
                    skippedDate++;
                    Console.WriteLine($"[SKIP] Видео {item.Id} старше DateFrom ({item.CreatedAt:yyyy-MM-dd})");
                    continue;
                }

                var scoredVideo = await scorer.ScoreAsync(item);
                scored++;

                scoreLog.Add(
                    $"    BY={scoredVideo.BelarusScore:F2} ECO={scoredVideo.EcoScore:F2} " +
                    $"[{(scoredVideo.PassesThreshold(minBelarus, minEco) ? "✓" : "✗")}] " +
                    $"@{item.Author.UniqueId}: {item.Desc[..Math.Min(50, item.Desc.Length)]}");

                if (!scoredVideo.PassesThreshold(minBelarus, minEco))
                    continue;

                passed++;
                results.Add((scoredVideo, query.Id));

                if (seen.Add(item.Id))
                    Console.WriteLine(
                        $"  [{scoredVideo.TotalScore:F2}] BY={scoredVideo.BelarusScore:F2} " +
                        $"ECO={scoredVideo.EcoScore:F2} | @{item.Author.UniqueId}: " +
                        $"{item.Desc[..Math.Min(60, item.Desc.Length)]}...");
            }

            Console.WriteLine(
                $"[STATS] \"{query.Value}\": " +
                $"fetched={fetched} skipped(ad/private)={skippedAd} skipped(date)={skippedDate} " +
                $"scored={scored} passed={passed} (minBY={minBelarus:F2} minECO={minEco:F2})");

            // Если ничего не прошло — печатаем все скоры чтобы видеть почему
            if (passed == 0 && scoreLog.Count > 0)
            {
                Console.WriteLine($"[SCORES] Прошли порог 0/{scoreLog.Count}. Лучшие:");
                foreach (var line in scoreLog.OrderByDescending(l => l).Take(5))
                    Console.WriteLine(line);
            }

            await searchQueryRepo.UpdateLastRunAtAsync(query.Id);
        }

        return results.OrderByDescending(r => r.Scored.TotalScore).ToList();
    }
}
