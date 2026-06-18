namespace TikTokEcoBelarus.Models;

/// <summary>
/// Результат парсинга /api/user/info или /api/user/info-by-id.
/// </summary>
public class TikTokUserInfo
{
    public string  UniqueId    { get; init; } = default!;

    /// <summary>Числовой TikTok UID (user.uid / user.id).</summary>
    public string? UserId      { get; init; }

    public string? Nickname    { get; init; }
    public string? AvatarThumb { get; init; }

    /// <summary>share_info.share_url — постоянная ссылка на профиль.</summary>
    public string? ProfileUrl  { get; init; }

    /// <summary>Только из /api/user/info (userInfo.stats.videoCount).</summary>
    public int     VideoCount  { get; init; }
}
