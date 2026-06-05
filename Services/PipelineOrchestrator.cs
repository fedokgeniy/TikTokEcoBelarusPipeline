using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure;
using TikTokEcoBelarus.Pipeline;

namespace TikTokEcoBelarus.Services;

public class PipelineRunResult
{
    public int Found { get; init; }
    public int Saved { get; init; }
    public int Skipped { get; init; }
    public string? Error { get; init; }
    public bool Success => Error == null;
    public DateTime StartedAt { get; init; }
    public DateTime FinishedAt { get; init; }
    public TimeSpan Duration => FinishedAt - StartedAt;
}

public class PipelineOrchestrator(IServiceScopeFactory scopeFactory)
{
    public async Task<PipelineRunResult> RunAsync(
        double minBelarus = 0.3,
        double minEco = 0.3,
        CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;

        try
        {
            // Каждый запуск в своём scope — db не диспоузится раньше времени
            await using var scope = scopeFactory.CreateAsyncScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<CollectionPipeline>();
            var csvExport = scope.ServiceProvider.GetRequiredService<CsvExportService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var results = await pipeline.RunAsync(minBelarus, minEco, ct: ct);

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
                        PublishedAt = scored.Item.CreateTime > 0
                       ? DateTimeOffset.FromUnixTimeSeconds(scored.Item.CreateTime).UtcDateTime
                       : null,
                        MatchedKeywords = scored.BelarusSignals.Concat(scored.EcoSignals)
    .Distinct()
    .ToArray(),
                        FetchedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    saved++;
                }
            }

            await db.SaveChangesAsync(ct);

            if (results.Count > 0)
                await csvExport.ExportAsync(results);

            return new PipelineRunResult
            {
                Found = results.Count,
                Saved = saved,
                Skipped = skipped,
                StartedAt = startedAt,
                FinishedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new PipelineRunResult
            {
                Error = ex.ToString(),
                StartedAt = startedAt,
                FinishedAt = DateTime.UtcNow
            };
        }
    }
}