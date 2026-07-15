using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Models;
using TikTokEcoBelarus.Services;

namespace TikTokEcoBelarus.Pipeline;

public class CollectionPipeline(
    TikTokApiClient api,
    BelarusEcoScorer scorer,
    ISearchQueryRepository searchQueryRepo)
{
    /// <summary>
    /// maxVideos — максимальное кол-во видео на один запрос (из UI/AppSettings).
    /// Количество страниц вычисляется как ceil(maxVideos / 10), затем результат обрезается.
    /// </summary>
    public async Task<List<(ScoredVideo Scored, Guid QueryId)>> RunAsync(
        double minBelarus = 0.15,
        double minEco = 0.20,
        int maxVideos = 50,
        CancellationToken ct = default)
    {
        // ~10 видео на страницу у tiktok-api23
        const int videosPerPage = 10;
        int maxPages = (int)Math.Ceiling((double)maxVideos / videosPerPage);

        var queries = await searchQueryRepo.GetActiveQueriesAsync();
        var seen    = new HashSet<string>();
        var results = new List<(ScoredVideo Scored, Guid QueryId)>();

        foreach (var query in queries.OrderBy(q => q.Priority))
        {
            Console.WriteLine($"[SEARCH] Querying: \"{query.Value}\" (type: {query.QueryType})");
            Console.WriteLine($"[SEARCH] maxVideos={maxVideos} → maxPages={maxPages} (~{videosPerPage} видео/стр.)");

            if (query.DateFrom.HasValue)
                Console.WriteLine($"[FILTER] Видео не старше: {query.DateFrom.Value:yyyy-MM-dd}");

            int fetched     = 0;
            int skippedAd   = 0;
            int skippedDate = 0;
            int scored      = 0;
            int passed      = 0;

            var scoreLog    = new List<string>();
            var queryVideos = new List<(ScoredVideo Scored, Guid QueryId)>();

            await foreach (var item in api.SearchVideosAsync(query.Value, maxPages, ct: ct))
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
                queryVideos.Add((scoredVideo, query.Id));

                if (seen.Add(item.Id))
                    Console.WriteLine(
                        $"  [{scoredVideo.TotalScore:F2}] BY={scoredVideo.BelarusScore:F2} " +
                        $"ECO={scoredVideo.EcoScore:F2} | @{item.Author.UniqueId}: " +
                        $"{item.Desc[..Math.Min(60, item.Desc.Length)]}...");
            }

            // Обрезаем до maxVideos
            if (queryVideos.Count > maxVideos)
            {
                Console.WriteLine($"[TRIM] Обрезаем {queryVideos.Count} → {maxVideos} видео для запроса \"{query.Value}\"");
                queryVideos = queryVideos.Take(maxVideos).ToList();
                passed = queryVideos.Count;
            }

            results.AddRange(queryVideos);

            Console.WriteLine(
                $"[STATS] \"{query.Value}\": " +
                $"fetched={fetched} skipped(ad/private)={skippedAd} skipped(date)={skippedDate} " +
                $"scored={scored} passed={passed} (minBY={minBelarus:F2} minECO={minEco:F2})");

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
