using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

public interface ITrackedChannelRepository
{
    Task<List<TrackedChannel>> GetAllAsync();
    Task<TrackedChannel?>      GetByUniqueIdAsync(string uniqueId);
    Task                       AddAsync(TrackedChannel channel);
    Task                       DeleteAsync(Guid id);

    /// <summary>
    /// Обновляет метаданные канала: UserId, ProfileUrl, DisplayName, AvatarUrl.
    /// Вызывается один раз при первом резолве.
    /// </summary>
    Task SaveMetaAsync(TrackedChannel channel);

    /// <summary>
    /// Обновляет LastVideoCount, LastCommentCount, LastCheckedAt после проверки.
    /// </summary>
    Task UpdateAfterCheckAsync(Guid id, int videoCount, int commentCount, DateTimeOffset checkedAt);

    /// <summary>
    /// Добавляет / обновляет видео канала. Дубликаты по VideoId игнорируются.
    /// </summary>
    Task SaveVideosAsync(Guid channelId, List<TrackedChannelVideo> videos);
}
