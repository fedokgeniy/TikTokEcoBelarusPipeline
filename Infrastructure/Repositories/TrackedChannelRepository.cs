using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

public class TrackedChannelRepository(AppDbContext db) : ITrackedChannelRepository
{
    public Task<List<TrackedChannel>> GetAllAsync()
        => db.TrackedChannels
             .OrderBy(c => c.CreatedAt)
             .Include(c => c.Videos)
             .ToListAsync();

    public Task<TrackedChannel?> GetByUniqueIdAsync(string uniqueId)
        => db.TrackedChannels
             .FirstOrDefaultAsync(c => c.UniqueId == uniqueId);

    public async Task AddAsync(TrackedChannel channel)
    {
        db.TrackedChannels.Add(channel);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var ch = await db.TrackedChannels.FindAsync(id);
        if (ch is null) return;
        db.TrackedChannels.Remove(ch);
        await db.SaveChangesAsync();
    }

    /// <summary>Обновляет метаданные: UserId, ProfileUrl, DisplayName, AvatarUrl.</summary>
    public async Task SaveMetaAsync(TrackedChannel channel)
    {
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
        await db.TrackedChannels
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.LastVideoCount,   videoCount)
                .SetProperty(c => c.LastCommentCount, commentCount)
                .SetProperty(c => c.LastCheckedAt,    checkedAt));
    }

    public async Task SaveVideosAsync(Guid channelId, List<TrackedChannelVideo> videos)
    {
        // Игнорируем дубликаты по VideoId
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
}
