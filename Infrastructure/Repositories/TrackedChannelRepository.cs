using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

public class TrackedChannelRepository(AppDbContext db) : ITrackedChannelRepository
{
    public async Task<List<TrackedChannel>> GetAllAsync()
        => await db.TrackedChannels
            .OrderBy(c => c.CreatedAt)
            .Include(c => c.Videos)
            .ToListAsync();

    public async Task<TrackedChannel?> GetByUniqueIdAsync(string uniqueId)
        => await db.TrackedChannels
            .FirstOrDefaultAsync(c => c.UniqueId == uniqueId);

    public async Task AddAsync(TrackedChannel channel)
    {
        db.TrackedChannels.Add(channel);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var channel = await db.TrackedChannels.FindAsync(id);
        if (channel is null) return;
        db.TrackedChannels.Remove(channel);
        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var channel = await db.TrackedChannels.FindAsync(id);
        if (channel is null) return;
        channel.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    public async Task UpdateAfterCheckAsync(Guid id, int videoCount, int commentCount, DateTimeOffset checkedAt)
    {
        var channel = await db.TrackedChannels.FindAsync(id);
        if (channel is null) return;
        channel.LastVideoCount   = videoCount;
        channel.LastCommentCount = commentCount;
        channel.LastCheckedAt   = checkedAt;
        await db.SaveChangesAsync();
    }

    public async Task SaveVideosAsync(Guid channelId, List<TrackedChannelVideo> videos)
    {
        // Получаем уже сохранённые VideoId для этого канала
        var existingIds = await db.TrackedChannelVideos
            .Where(v => v.TrackedChannelId == channelId)
            .Select(v => v.VideoId)
            .ToHashSetAsync();

        var newVideos = videos
            .Where(v => !existingIds.Contains(v.VideoId))
            .ToList();

        if (newVideos.Count == 0) return;

        db.TrackedChannelVideos.AddRange(newVideos);
        await db.SaveChangesAsync();
    }
}
