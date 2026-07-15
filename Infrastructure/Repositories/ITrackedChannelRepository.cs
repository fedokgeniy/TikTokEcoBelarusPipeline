using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Repositories;

public interface ITrackedChannelRepository
{
    Task<List<TrackedChannel>> GetAllAsync();
    Task<TrackedChannel?>      GetByUniqueIdAsync(string uniqueId);
    Task                       AddAsync(TrackedChannel channel);
    Task                       DeleteAsync(Guid id);

    /// <summary>Обновляет метаданные канала: UserId, ProfileUrl, DisplayName, AvatarUrl.</summary>
    Task SaveMetaAsync(TrackedChannel channel);

    /// <summary>Обновляет LastVideoCount, LastCommentCount, LastCheckedAt после проверки.</summary>
    Task UpdateAfterCheckAsync(Guid id, int videoCount, int commentCount, DateTimeOffset checkedAt);

    /// <summary>Добавляет видео канала. Дубликаты по VideoId игнорируются.</summary>
    Task SaveVideosAsync(Guid channelId, List<TrackedChannelVideo> videos);

    /// <summary>Возвращает HashSet уже сохранённых VideoId для данного канала.</summary>
    Task<HashSet<string>> GetExistingVideoIdsAsync(Guid channelId);

    /// <summary>Сохраняет комментарии, игнорируя дубликаты по CommentId.</summary>
    Task SaveCommentsAsync(IEnumerable<VideoComment> comments);

    /// <summary>
    /// Возвращает список видео канала, возраст которых не превышает maxAgeDays дней.
    /// Используется для отбора видео, под которыми нужно собирать комментарии.
    /// </summary>
    Task<List<TrackedChannelVideo>> GetVideosForCommentFetchAsync(Guid channelId, int maxAgeDays);

    /// <summary>
    /// Сохраняет снапшот числа комментариев под видео (для истории динамики роста).
    /// </summary>
    Task SaveSnapshotAsync(VideoCommentSnapshot snapshot);

    /// <summary>
    /// Обновляет поля IsRelevant и Tags у конкретного комментария после классификации.
    /// </summary>
    Task UpdateCommentClassificationAsync(string commentId, bool isRelevant, string? tags);
}
