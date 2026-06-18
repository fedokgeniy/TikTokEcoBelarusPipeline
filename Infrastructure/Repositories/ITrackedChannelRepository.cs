using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

public interface ITrackedChannelRepository
{
    Task<List<TrackedChannel>> GetAllAsync();
    Task<TrackedChannel?>      GetByUniqueIdAsync(string uniqueId);
    Task                       AddAsync(TrackedChannel channel);
    Task                       DeleteAsync(Guid id);
    Task                       SetActiveAsync(Guid id, bool isActive);
    Task                       UpdateAfterCheckAsync(Guid id, int videoCount, int commentCount, DateTimeOffset checkedAt);
    Task                       SaveVideosAsync(Guid channelId, List<TrackedChannelVideo> videos);
}
