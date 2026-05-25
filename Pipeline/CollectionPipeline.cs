using TikTokEcoBelarus.Models;
using TikTokEcoBelarus.Services;

namespace TikTokEcoBelarus.Pipeline;

public class CollectionPipeline
{
    private readonly TikTokApiClient _api;
    private readonly BelarusEcoScorer _scorer;

    // Наборы поисковых запросов под белорусский экоконтент
    private static readonly string[] SearchQueries =
    [
        // Русский язык
        "экология беларусь",
        "природа беларусь",
        "минск мусор переработка",
        "беловежская пуща",
        "нарочь природа",
        "загрязнение беларусь",
        "зелёный беларусь",
        "экоактивизм беларусь",
        "раздельный сбор беларусь",

        // Белорусский язык
        "экалогія беларусь",
        "прырода беларусь",

        // Хэштеги как запросы
        "#беларусь #экология",
        "#беловежскаяпуща",
        "#минскприрода",

        // English
        "belarus ecology",
        "belarus nature",
        "belovezhskaya forest",
    ];

    public CollectionPipeline(TikTokApiClient api, BelarusEcoScorer scorer)
    {
        _api = api;
        _scorer = scorer;
    }

    public async Task<List<ScoredVideo>> RunAsync(
        double minBelarus = 0.3,
        double minEco = 0.3,
        int maxPerQuery = 5,
        CancellationToken ct = default)
    {
        var seen = new HashSet<string>(); // дедупликация по video ID
        var results = new List<ScoredVideo>();

        foreach (var query in SearchQueries)
        {
            Console.WriteLine($"[SEARCH] Querying: \"{query}\"");

            await foreach (var item in _api.SearchVideosAsync(query, maxPerQuery, ct: ct))
            {
                // Пропускаем дубли и рекламу
                if (!seen.Add(item.Id) || item.IsAd)
                    continue;

                // Пропускаем приватные аккаунты
                if (item.Author.PrivateAccount)
                    continue;

                var scored = _scorer.Score(item);

                if (scored.PassesThreshold(minBelarus, minEco))
                {
                    results.Add(scored);
                    Console.WriteLine(
                        $" [{scored.TotalScore:F2}] BY={scored.BelarusScore:F2} " +
                        $"ECO={scored.EcoScore:F2} | @{item.Author.UniqueId}: {item.Desc[..Math.Min(60, item.Desc.Length)]}..."
                    );
                }
            }
        }

        // Сортировка по итоговому score
        return results
            .OrderByDescending(v => v.TotalScore)
            .ToList();
    }
}