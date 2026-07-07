namespace TikTokEcoBelarus.Domain.Entities;

public class TrackedChannel
{
    public Guid    Id               { get; set; } = Guid.NewGuid();

    /// <summary>Числовой TikTok UID — стабильный, не меняется при смене username.</summary>
    public string? UserId           { get; set; }

    /// <summary>@username — нужен для поиска видео и отображения.</summary>
    public string  UniqueId         { get; set; } = string.Empty;

    public string? DisplayName      { get; set; }
    public string? AvatarUrl        { get; set; }

    /// <summary>Прямая ссылка на профиль из share_info.share_url.</summary>
    public string? ProfileUrl       { get; set; }

    public bool    IsActive         { get; set; } = true;
    public int?    LastVideoCount   { get; set; }
    public int?    LastCommentCount { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TrackedChannelVideo> Videos { get; set; } = [];
}
