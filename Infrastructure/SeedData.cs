using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace TikTokEcoBelarus.Infrastructure;

public static class SeedData
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await SeedScoringRulesAsync(db);
        await SeedScoringRuleThresholdsAsync(db);
        await SeedSearchQueriesAsync(db);
    }

    private static async Task SeedScoringRulesAsync(AppDbContext db)
    {
        if (await db.ScoringRules.AnyAsync()) return;

        var rules = new List<ScoringRule>
        {
            // ── Belarus_Explicit (weight: 0.40, maxMatches: 1) ──
            new() { Keyword = "беларусь",    Category = "Belarus_Explicit", ScoreType = "belarus", Weight = 0.40m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "belarus",     Category = "Belarus_Explicit", ScoreType = "belarus", Weight = 0.40m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "беларускі",   Category = "Belarus_Explicit", ScoreType = "belarus", Weight = 0.40m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "беларуская",  Category = "Belarus_Explicit", ScoreType = "belarus", Weight = 0.40m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "беларускае",  Category = "Belarus_Explicit", ScoreType = "belarus", Weight = 0.40m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "bielarus",    Category = "Belarus_Explicit", ScoreType = "belarus", Weight = 0.40m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "бел",         Category = "Belarus_Explicit", ScoreType = "belarus", Weight = 0.40m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "by",          Category = "Belarus_Explicit", ScoreType = "belarus", Weight = 0.40m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "🇧🇾",         Category = "Belarus_Explicit", ScoreType = "belarus", Weight = 0.40m, SearchContext = "any", MaxMatches = 1 },

            // ── Belarus_City (weight: 0.25, maxMatches: 1) ──
            new() { Keyword = "минск",    Category = "Belarus_City", ScoreType = "belarus", Weight = 0.25m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "minsk",    Category = "Belarus_City", ScoreType = "belarus", Weight = 0.25m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "гродно",   Category = "Belarus_City", ScoreType = "belarus", Weight = 0.25m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "брест",    Category = "Belarus_City", ScoreType = "belarus", Weight = 0.25m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "витебск",  Category = "Belarus_City", ScoreType = "belarus", Weight = 0.25m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "могилев",  Category = "Belarus_City", ScoreType = "belarus", Weight = 0.25m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "гомель",   Category = "Belarus_City", ScoreType = "belarus", Weight = 0.25m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "brest",    Category = "Belarus_City", ScoreType = "belarus", Weight = 0.25m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "grodno",   Category = "Belarus_City", ScoreType = "belarus", Weight = 0.25m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "vitebsk",  Category = "Belarus_City", ScoreType = "belarus", Weight = 0.25m, SearchContext = "any", MaxMatches = 1 },

            // ── Belarus_Place (weight: 0.30, maxMatches: 1) ──
            new() { Keyword = "беловежская",   Category = "Belarus_Place", ScoreType = "belarus", Weight = 0.30m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "беловежскі",    Category = "Belarus_Place", ScoreType = "belarus", Weight = 0.30m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "нарочь",        Category = "Belarus_Place", ScoreType = "belarus", Weight = 0.30m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "налибоки",      Category = "Belarus_Place", ScoreType = "belarus", Weight = 0.30m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "припять",       Category = "Belarus_Place", ScoreType = "belarus", Weight = 0.30m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "неман",         Category = "Belarus_Place", ScoreType = "belarus", Weight = 0.30m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "браславские",   Category = "Belarus_Place", ScoreType = "belarus", Weight = 0.30m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "augustow",      Category = "Belarus_Place", ScoreType = "belarus", Weight = 0.30m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "беловежа",      Category = "Belarus_Place", ScoreType = "belarus", Weight = 0.30m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "belovezhskaya", Category = "Belarus_Place", ScoreType = "belarus", Weight = 0.30m, SearchContext = "any", MaxMatches = 1 },

            // ── Belarus_Language (weight: 0.10, maxMatches: 99 — threshold считается отдельно) ──
            new() { Keyword = "прырода",  Category = "Belarus_Language", ScoreType = "belarus", Weight = 0.10m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "экалогія", Category = "Belarus_Language", ScoreType = "belarus", Weight = 0.10m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "лес",      Category = "Belarus_Language", ScoreType = "belarus", Weight = 0.10m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "рака",     Category = "Belarus_Language", ScoreType = "belarus", Weight = 0.10m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "возера",   Category = "Belarus_Language", ScoreType = "belarus", Weight = 0.10m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "чыстата",  Category = "Belarus_Language", ScoreType = "belarus", Weight = 0.10m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "адходы",   Category = "Belarus_Language", ScoreType = "belarus", Weight = 0.10m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "жывёлы",   Category = "Belarus_Language", ScoreType = "belarus", Weight = 0.10m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "расліны",  Category = "Belarus_Language", ScoreType = "belarus", Weight = 0.10m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "ахова",    Category = "Belarus_Language", ScoreType = "belarus", Weight = 0.10m, SearchContext = "any", MaxMatches = 99 },

            // ── Eco_Hashtag (weight: 0.15, maxMatches: 3) ──
            new() { Keyword = "ecology",       Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "environment",   Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "nature",        Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "eco",           Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "green",         Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "sustainability",Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "climate",       Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "recycling",     Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "wildlife",      Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "conservation",  Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "cleanwater",    Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "savetheplanet", Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "naturelover",   Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "forest",        Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "biodiversity",  Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "zerowaste",     Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "экология",      Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "природа",       Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "зелёный",       Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "переработка",   Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "экалогія",      Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },
            new() { Keyword = "прырода",       Category = "Eco_Hashtag", ScoreType = "eco", Weight = 0.15m, SearchContext = "hashtags", MaxMatches = 3 },

            // ── Eco_Topic (weight: 0.12, maxMatches: 99) ──
            new() { Keyword = "экология",             Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "экологія",             Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "природа",              Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "окружающая среда",     Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "переработка",          Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "мусор",                Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "отходы",               Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "загрязнение",          Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "климат",               Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "лес",                  Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "река",                 Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "озеро",                Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "заповедник",           Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "biodiversity",         Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "устойчивое развитие",  Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "зеленая",              Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "чистота",              Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "раздельный сбор",      Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "вторсырье",            Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },
            new() { Keyword = "эко",                  Category = "Eco_Topic", ScoreType = "eco", Weight = 0.12m, SearchContext = "description", MaxMatches = 99 },

            // ── Eco_Strong (weight: 0.20, maxMatches: 99) ──
            new() { Keyword = "заповедник",   Category = "Eco_Strong", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "нацпарк",      Category = "Eco_Strong", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "national park",Category = "Eco_Strong", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "биосфера",     Category = "Eco_Strong", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "красная книга",Category = "Eco_Strong", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "вымирающие",   Category = "Eco_Strong", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "endangered",   Category = "Eco_Strong", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "pollution",    Category = "Eco_Strong", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "загрязнение",  Category = "Eco_Strong", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "очистка",      Category = "Eco_Strong", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 99 },
            new() { Keyword = "экоактивизм",  Category = "Eco_Strong", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 99 },

            // ── Eco_Place (weight: 0.20, maxMatches: 1) — белорусские эко-места ──
            new() { Keyword = "беловежская", Category = "Eco_Place", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "нарочь",      Category = "Eco_Place", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "браславские", Category = "Eco_Place", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 1 },
            new() { Keyword = "налибоки",    Category = "Eco_Place", ScoreType = "eco", Weight = 0.20m, SearchContext = "any", MaxMatches = 1 },

            // ── Eco_Bio (weight: 0.15, maxMatches: 1) — эко в биографии автора ──
            new() { Keyword = "эко",     Category = "Eco_Bio", ScoreType = "eco", Weight = 0.15m, SearchContext = "bio", MaxMatches = 1 },
            new() { Keyword = "eco",     Category = "Eco_Bio", ScoreType = "eco", Weight = 0.15m, SearchContext = "bio", MaxMatches = 1 },
            new() { Keyword = "природа", Category = "Eco_Bio", ScoreType = "eco", Weight = 0.15m, SearchContext = "bio", MaxMatches = 1 },
            new() { Keyword = "nature",  Category = "Eco_Bio", ScoreType = "eco", Weight = 0.15m, SearchContext = "bio", MaxMatches = 1 },
        };

        db.ScoringRules.AddRange(rules);
        await db.SaveChangesAsync();
    }

    private static async Task SeedScoringRuleThresholdsAsync(AppDbContext db)
    {
        if (await db.ScoringRuleThresholds.AnyAsync()) return;

        // Belarus_Language: 1 совпадение → +0.10, 2+ → +0.20
        db.ScoringRuleThresholds.AddRange(
            new ScoringRuleThreshold { Category = "Belarus_Language", ScoreType = "belarus", MinMatchCount = 1, ScoreBonus = 0.10m },
            new ScoringRuleThreshold { Category = "Belarus_Language", ScoreType = "belarus", MinMatchCount = 2, ScoreBonus = 0.20m }
        );

        await db.SaveChangesAsync();
    }

    private static async Task SeedSearchQueriesAsync(AppDbContext db)
    {
        if (await db.SearchQueries.AnyAsync()) return;

        db.SearchQueries.AddRange(
            new SearchQuery { QueryType = "hashtag", Value = "экология", Priority = 1 },
            new SearchQuery { QueryType = "hashtag", Value = "беларусь", Priority = 2 },
            new SearchQuery { QueryType = "hashtag", Value = "природаБеларуси", Priority = 3 },
            new SearchQuery { QueryType = "hashtag", Value = "экалогія", Priority = 4 },
            new SearchQuery { QueryType = "hashtag", Value = "Belarus", Priority = 5 },
            new SearchQuery { QueryType = "keyword", Value = "экология беларусь", Priority = 6 },
            new SearchQuery { QueryType = "keyword", Value = "природа минск", Priority = 7 }
        );

        await db.SaveChangesAsync();
    }
}