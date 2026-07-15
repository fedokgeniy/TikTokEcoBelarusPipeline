using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Services;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

/// <summary>
/// Репозиторий для TrackedChannel и связанных сущностей.
/// Каждый метод создаёт и уничтожает собственный DbContext через using.
/// Исключение: UpdateCommentClassificationBatchAsync — один контекст на весь батч.
/// </summary>
public class TrackedChannelRepository(IDbContextFactory<AppDbContext> dbFactory) : ITrackedChannelRepository
{
    public async Task<List<TrackedChannel>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TrackedChannels
            .OrderBy(c => c.CreatedAt)
            .Include(c => c.Videos)
            .ToListAsync();
    }

    public async Task<TrackedChannel?> GetByUniqueIdAsync(string uniqueId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TrackedChannels
            .FirstOrDefaultAsync(c => c.UniqueId == uniqueId);
    }

    public async Task AddAsync(TrackedChannel channel)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.TrackedChannels.Add(channel);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ch = await db.TrackedChannels.FindAsync(id);
        if (ch is null) return;
        db.TrackedChannels.Remove(ch);
        await db.SaveChangesAsync();
    }

    public async Task SaveMetaAsync(TrackedChannel channel)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.TrackedChannels
            .Where(c => c.Id == channel.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.UserId,      channel.UserId)
                .SetProperty(c => c.ProfileUrl,  channel.ProfileUrl)
                .SetProperty(c => c.DisplayName, channel.DisplayName)
                .SetProperty(c => c.AvatarUrl,   channel.AvatarUrl));
    }

    public async Task UpdateAfterCheckAsync(
        Guid id, int videoCount, int commentCount, DateTimeOffset checkedAt)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.TrackedChannels
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.LastVideoCount,   videoCount)
                .SetProperty(c => c.LastCommentCount, commentCount)
                .SetProperty(c => c.LastCheckedAt,    checkedAt));
    }

    public async Task SaveVideosAsync(Guid channelId, List<TrackedChannelVideo> videos)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existingIds = await db.TrackedChannelVideos
            .Where(v => v.TrackedChannelId == channelId)
            .Select(v => v.VideoId)
            .ToListAsync();

        var newVideos = videos
            .Where(v => !existingIds.Contains(v.VideoId))
            .ToList();

        if (newVideos.Count == 0) return;

        db.TrackedChannelVideos.AddRange(newVideos);
        await db.SaveChangesAsync();
    }

    public async Task<HashSet<string>> GetExistingVideoIdsAsync(Guid channelId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TrackedChannelVideos
            .Where(v => v.TrackedChannelId == channelId)
            .Select(v => v.VideoId)
            .ToHashSetAsync();
    }

    public async Task<HashSet<string>> SaveCommentsAsync(IEnumerable<VideoComment> comments)
    {
        var list = comments.ToList();
        if (list.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync();
        var incomingIds  = list.Select(c => c.CommentId).ToList();
        var existingKeys = await db.VideoComments
            .Where(c => incomingIds.Contains(c.CommentId))
            .Select(c => c.CommentId)
            .ToHashSetAsync();

        var toInsert = list.Where(c => !existingKeys.Contains(c.CommentId)).ToList();
        if (toInsert.Count == 0) return [];

        db.VideoComments.AddRange(toInsert);
        await db.SaveChangesAsync();

        return toInsert.Select(c => c.CommentId).ToHashSet();
    }

    public async Task<List<TrackedChannelVideo>> GetVideosForCommentFetchAsync(
        Guid channelId, int maxAgeDays)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TrackedChannelVideos
            .Where(v => v.TrackedChannelId == channelId && v.VideoCreatedAt >= cutoff)
            .ToListAsync();
    }

    public async Task SaveSnapshotAsync(VideoCommentSnapshot snapshot)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var minute = new DateTimeOffset(
            snapshot.SnapshotAt.Year, snapshot.SnapshotAt.Month, snapshot.SnapshotAt.Day,
            snapshot.SnapshotAt.Hour, snapshot.SnapshotAt.Minute, 0, TimeSpan.Zero);

        bool exists = await db.VideoCommentSnapshots
            .AnyAsync(s => s.VideoId == snapshot.VideoId && s.SnapshotAt == minute);

        if (exists) return;

        snapshot.SnapshotAt = minute;
        db.VideoCommentSnapshots.Add(snapshot);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Обновляет все поля AI-классификации после ответа от Claude (одиночный вызов).
    /// </summary>
    public async Task UpdateCommentClassificationAsync(
        string commentId, bool isRelevant, int score, string? category, bool shouldReply, string? tags)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.VideoComments
            .Where(c => c.CommentId == commentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.IsRelevant,  isRelevant)
                .SetProperty(c => c.Score,       score)
                .SetProperty(c => c.Category,    category)
                .SetProperty(c => c.ShouldReply, shouldReply)
                .SetProperty(c => c.Tags,        tags));
    }

    /// <summary>
    /// Батч-обновление классификации: открывает один DbContext на весь словарь,
    /// выполняет ExecuteUpdateAsync для каждого CommentId.
    /// Вместо N контекстов — один; вместо N раундтрипов — N лёгких UPDATE'ов в одном соединении.
    /// </summary>
    public async Task UpdateCommentClassificationBatchAsync(
        Dictionary<string, ClassifyResult> results, CancellationToken ct = default)
    {
        if (results.Count == 0) return;

        await using var db = await dbFactory.CreateDbContextAsync();

        foreach (var (commentId, r) in results)
        {
            ct.ThrowIfCancellationRequested();

            await db.VideoComments
                .Where(c => c.CommentId == commentId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.IsRelevant,  r.IsRelevant)
                    .SetProperty(c => c.Score,       r.Score)
                    .SetProperty(c => c.Category,    r.Category)
                    .SetProperty(c => c.ShouldReply, r.ShouldReply)
                    .SetProperty(c => c.Tags,        r.Tags), ct);
        }
    }
}
