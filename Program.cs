using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure;
using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Pipeline;
using TikTokEcoBelarus.Services;

const string apiKey = "02e437b294msh2835a963405c6f2p1bc888jsn6ec318a971d0";

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.SetBasePath(Directory.GetCurrentDirectory());
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(context.Configuration.GetConnectionString("Default")));

        services.AddMemoryCache();
        services.AddHttpClient();

        services.AddScoped<IScoringRuleRepository, ScoringRuleRepository>();
        services.AddScoped<ISearchQueryRepository, SearchQueryRepository>();
        services.AddScoped<BelarusEcoScorer>();
        services.AddScoped<CollectionPipeline>();
        services.AddScoped<CsvExportService>();

        services.AddSingleton<TikTokApiClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient();
            return new TikTokApiClient(httpClient, apiKey);
        });
    })
    .Build();

// ── Seed ──────────────────────────────────────────────────────
using (var seedScope = host.Services.CreateScope())
{
    var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await SeedData.SeedAsync(db);
}
// ──────────────────────────────────────────────────────────────

using var scope = host.Services.CreateScope();
var provider = scope.ServiceProvider;

try
{
    var pipeline = provider.GetRequiredService<CollectionPipeline>();
    var csvExport = provider.GetRequiredService<CsvExportService>();
    var db = provider.GetRequiredService<AppDbContext>();

    Console.WriteLine("Запуск пайплайна...");

    var results = await pipeline.RunAsync(minBelarus: 0.3, minEco: 0.3);

    Console.WriteLine();
    Console.WriteLine($"Найдено видео: {results.Count}");

    if (results.Count == 0)
    {
        Console.WriteLine("Подходящих видео не найдено.");
    }
    else
    {
        // ── Сохранение в БД ───────────────────────────────────
        int saved = 0;
        int skipped = 0;

        foreach (var scored in results)
        {
            var videoId = scored.Item.Id;
            var existing = await db.Videos.FindAsync(videoId);

            if (existing != null)
            {
                existing.LikeCount = scored.Item.Stats.DiggCount;
                existing.CommentCount = scored.Item.Stats.CommentCount;
                existing.ShareCount = scored.Item.Stats.ShareCount;
                existing.ViewCount = scored.Item.Stats.PlayCount;
                existing.UpdatedAt = DateTime.UtcNow;
                skipped++;
            }
            else
            {
                db.Videos.Add(new Video
                {
                    VideoId = videoId,
                    VideoUrl = $"https://www.tiktok.com/@{scored.Item.Author.UniqueId}/video/{videoId}",
                    AuthorUniqueId = scored.Item.Author.UniqueId,
                    AuthorNickname = scored.Item.Author.Nickname,
                    AuthorBio = scored.Item.Author.Signature,
                    Description = scored.Item.Desc,
                    Hashtags = scored.Item.AllHashtags.ToArray(),
                    LikeCount = scored.Item.Stats.DiggCount,
                    CommentCount = scored.Item.Stats.CommentCount,
                    ShareCount = scored.Item.Stats.ShareCount,
                    ViewCount = scored.Item.Stats.PlayCount,
                    BelarusScore = (decimal)scored.BelarusScore,
                    EcoScore = (decimal)scored.EcoScore,
                    ScoreBreakdown = JsonSerializer.Serialize(new
                    {
                        belarus = scored.BelarusSignals,
                        eco = scored.EcoSignals
                    }),
                    FetchedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                saved++;
            }
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"Сохранено в БД: {saved} новых, обновлено дублей: {skipped}");
        // ──────────────────────────────────────────────────────

        var csvPath = await csvExport.ExportAsync(results);
        Console.WriteLine($"CSV сохранён: {csvPath}");
        Console.WriteLine();
        Console.WriteLine("Топ результатов:");

        foreach (var item in results.Take(10))
        {
            Console.WriteLine(
                $"- @{item.Item.Author.UniqueId} | " +
                $"score={item.TotalScore:F3} | " +
                $"belarus={item.BelarusScore:F3} | " +
                $"eco={item.EcoScore:F3} | " +
                $"{item.Item.Desc}"
            );
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine("Ошибка при выполнении пайплайна:");
    Console.WriteLine(ex.ToString());
}

Console.WriteLine();
Console.WriteLine("Нажми Enter для выхода...");
Console.ReadLine();