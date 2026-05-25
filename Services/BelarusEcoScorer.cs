namespace TikTokEcoBelarus.Services;

using TikTokEcoBelarus.Models;

public class BelarusEcoScorer
{
    // ── Белорусские сигналы ─────────────────────────────────

    // Явные географические маркеры (высокий вес)
    private static readonly string[] BelarusExplicit =
    [
        "беларусь", "belarus", "беларускі", "беларуская",
        "беларускае", "bielarus", "бел", "by", "🇧🇾"
    ];

    // Города и регионы
    private static readonly string[] BelarusCities =
    [
        "минск", "minsk", "гродно", "брест", "витебск",
        "могилев", "гомель", "brest", "grodno", "vitebsk"
    ];

    // Известные белорусские места / природа
    private static readonly string[] BelarusPlaces =
    [
        "беловежская", "беловежскі", "нарочь", "налибоки",
        "припять", "неман", "браславские", "augustow",
        "беловежа", "belovezhskaya"
    ];

    // Язык (белорусский — сильный сигнал)
    private static readonly string[] BelarusLanguageMarkers =
    [
        "прырода", "экалогія", "лес", "рака", "возера",
        "чыстата", "адходы", "жывёлы", "расліны", "ахова"
    ];

    // ── Экологические сигналы ───────────────────────────────

    // Ключевые экологические темы (RU)
    private static readonly string[] EcoTopicsRu =
    [
        "экология", "экологія", "природа", "окружающая среда",
        "переработка", "мусор", "отходы", "загрязнение",
        "климат", "лес", "река", "озеро", "заповедник",
        "biodiversity", "устойчивое развитие", "зеленая",
        "чистота", "раздельный сбор", "вторсырье", "эко"
    ];

    // Экологические хэштеги
    private static readonly string[] EcoHashtags =
    [
        "ecology", "environment", "nature", "eco", "green",
        "sustainability", "climate", "recycling", "wildlife",
        "conservation", "cleanwater", "savetheplanet",
        "naturelover", "forest", "biodiversity", "zerowaste",
        "экология", "природа", "зелёный", "переработка",
        "экалогія", "прырода" // белорусский язык
    ];

    // Сильные индикаторы (удваивают score)
    private static readonly string[] EcoStrongSignals =
    [
        "заповедник", "нацпарк", "national park", "биосфера",
        "красная книга", "вымирающие", "endangered",
        "pollution", "загрязнение", "очистка", "экоактивизм"
    ];

    // ── Scoring ─────────────────────────────────────────────

    public ScoredVideo Score(TikTokItem item)
    {
        var scored = new ScoredVideo { Item = item };

        scored.BelarusScore = ComputeBelarusScore(item, scored.BelarusSignals);
        scored.EcoScore = ComputeEcoScore(item, scored.EcoSignals);

        return scored;
    }

    private double ComputeBelarusScore(TikTokItem item, List<string> signals)
    {
        double score = 0.0;

        var searchText = BuildSearchText(item);

        // Явное упоминание Беларуси — сильный сигнал
        foreach (var kw in BelarusExplicit)
        {
            if (searchText.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.4;
                signals.Add($"explicit:{kw}");
                break; // считаем один раз
            }
        }

        // Город
        foreach (var city in BelarusCities)
        {
            if (searchText.Contains(city, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.25;
                signals.Add($"city:{city}");
                break;
            }
        }

        // Природные места Беларуси
        foreach (var place in BelarusPlaces)
        {
            if (searchText.Contains(place, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.30;
                signals.Add($"place:{place}");
                break;
            }
        }

        // Белорусский язык в bio или описании
        int langMatches = 0;
        foreach (var lang in BelarusLanguageMarkers)
        {
            if (searchText.Contains(lang, StringComparison.OrdinalIgnoreCase))
            {
                langMatches++;
                signals.Add($"lang:{lang}");
            }
        }
        if (langMatches >= 2) score += 0.20;
        else if (langMatches == 1) score += 0.10;

        // Флаг в bio — очень сильный сигнал
        if (item.Author.Signature.Contains("🇧🇾"))
        {
            score += 0.35;
            signals.Add("flag:🇧🇾 in bio");
        }

        // Верифицированный аккаунт из BY
        if (item.Author.Verified && score > 0.2)
        {
            score += 0.10;
            signals.Add("verified");
        }

        return Math.Min(score, 1.0);
    }

    private double ComputeEcoScore(TikTokItem item, List<string> signals)
    {
        double score = 0.0;

        var desc = item.Desc.ToLower();
        var tags = item.AllHashtags;
        var bio = item.Author.Signature.ToLower();

        // Эко-хэштеги (каждый даёт +0.15, максимум 3)
        int tagMatches = 0;
        foreach (var hashtag in EcoHashtags)
        {
            if (tags.Contains(hashtag))
            {
                tagMatches++;
                signals.Add($"hashtag:{hashtag}");
                if (tagMatches >= 3) break;
            }
        }
        score += tagMatches * 0.15;

        // Эко-слова в описании
        int descMatches = 0;
        foreach (var topic in EcoTopicsRu)
        {
            if (desc.Contains(topic))
            {
                descMatches++;
                signals.Add($"desc:{topic}");
            }
        }
        score += Math.Min(descMatches * 0.12, 0.36);

        // Сильные сигналы (удваивают weight)
        foreach (var strong in EcoStrongSignals)
        {
            if (desc.Contains(strong) || tags.Contains(strong))
            {
                score += 0.20;
                signals.Add($"strong:{strong}");
            }
        }

        // Эко в bio автора
        if (bio.Contains("эко") || bio.Contains("eco") ||
            bio.Contains("природа") || bio.Contains("nature"))
        {
            score += 0.15;
            signals.Add("bio:eco");
        }

        // Белорусские эко-места в описании автоматически дают эко-очки
        foreach (var place in new[] { "беловежская", "нарочь", "браславские", "налибоки" })
        {
            if (desc.Contains(place) || bio.Contains(place))
            {
                score += 0.20;
                signals.Add($"ecoplace:{place}");
                break;
            }
        }

        return Math.Min(score, 1.0);
    }

    // Объединяем все текстовые поля для поиска
    private static string BuildSearchText(TikTokItem item) =>
        string.Join(" ",
            item.Desc,
            item.Author.Signature,
            item.Author.Nickname,
            item.Author.UniqueId,
            string.Join(" ", item.AllHashtags)
        );
}