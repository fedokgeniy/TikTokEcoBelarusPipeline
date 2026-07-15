using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

/// <summary>
/// Репозиторий для TrackedChannel и связанных сущностей.
///
/// Использует IDbContextFactory вместо прямого инжекта AppDbContext,
/// чтобы избежать "Cannot access a disposed context instance" —
/// ChannelMonitorPipeline живёт дольше Scoped-контекста Blazor.
/// Каждый метод создаёт и уничтожает собственный контекст через using.
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

    /// <summary>Обновляет метаданные: UserId, ProfileUrl, DisplayName, AvatarUrl.</summary>
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

    /// <summary>
    /// Возвращает HashSet уже сохранённых VideoId для канала.
    /// Используется в ChannelMonitorPipeline для delta-фильтрации.
    /// </summary>
    public async Task<HashSet<string>> GetExistingVideoIdsAsync(Guid channelId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TrackedChannelVideos
            .Where(v => v.TrackedChannelId == channelId)
            .Select(v => v.VideoId)
            .ToHashSetAsync();
    }

    /// <summary>
    /// Сохраняет комментарии, пропуская дубликаты по CommentId.
    /// </summary>
    public async Task SaveCommentsAsync(IEnumerable<VideoComment> comments)
    {
        var list = comments.ToList();
        if (list.Count == 0) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var incomingIds  = list.Select(c => c.CommentId).ToList();
        var existingKeys = await db.VideoComments
            .Where(c => incomingIds.Contains(c.CommentId))
            .Select(c => c.CommentId)
            .ToHashSetAsync();

        var toInsert = list.Where(c => !existingKeys.Contains(c.CommentId)).ToList();
        if (toInsert.Count == 0) return;

        db.VideoComments.AddRange(toInsert);
        await db.SaveChangesAsync();
    }
}
