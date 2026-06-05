using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure;
using TikTokEcoBelarus.Models;

namespace TikTokEcoBelarus.Services;

public class VideoDeduplicationService(AppDbContext db)
{
    public async Task<bool> IsAlreadyFetchedAsync(string videoId)
        => await db.Videos.AnyAsync(v => v.VideoId == videoId);

    public async Task UpsertAsync(ScoredVideo scored, Guid searchQueryId)
    {
        var item = scored.Item;
        var videoId = item.Id.ToString();

        var existing = await db.Videos.FindAsync(videoId);

        if (existing == null)
        {
            var video = new Video
            {
                VideoId = videoId,
                VideoUrl = $"https://www.tiktok.com/@{item.Author.UniqueId}/video/{item.Id}",
                AuthorUniqueId = item.Author.UniqueId,
                AuthorNickname = item.Author.Nickname,
                AuthorBio = item.Author.Signature,
                Description = item.Desc,
                Hashtags = item.AllHashtags.ToArray(),
                LikeCount = item.Stats.DiggCount,
                CommentCount = item.Stats.CommentCount,
                ShareCount = item.Stats.ShareCount,
                ViewCount = item.Stats.PlayCount,
                BelarusScore = (decimal)scored.BelarusScore,
                EcoScore = (decimal)scored.EcoScore,
                ScoreBreakdown = JsonSerializer.Serialize(new
                {
                    belarus = scored.BelarusSignals,
                    eco = scored.EcoSignals
                }),
                FetchedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Videos.Add(video);
            await db.SaveChangesAsync();

            // Связь с поисковым запросом
            db.VideoSearchQueryLinks.Add(new VideoSearchQueryLink
            {
                VideoId = videoId,
                SearchQueryId = searchQueryId
            });
        }
        else
        {
            // Обновляем только счётчики — они меняются со временем
            existing.LikeCount = item.Stats.DiggCount;
            existing.CommentCount = item.Stats.CommentCount;
            existing.ShareCount = item.Stats.ShareCount;
            existing.ViewCount = item.Stats.PlayCount;
            existing.UpdatedAt = DateTime.UtcNow;

            // Добавляем связь если ещё не существует
            bool linkExists = await db.VideoSearchQueryLinks
                .AnyAsync(l => l.VideoId == videoId && l.SearchQueryId == searchQueryId);

            if (!linkExists)
            {
                db.VideoSearchQueryLinks.Add(new VideoSearchQueryLink
                {
                    VideoId = videoId,
                    SearchQueryId = searchQueryId
                });
            }
        }

        await db.SaveChangesAsync();
    }
}