namespace TikTokEcoBelarus.Models;

/// <summary>
/// Минимальная модель ответа /api/user/info.
/// </summary>
public class TikTokUserInfo
{
    public string  UniqueId    { get; init; } = default!;
    public string? Nickname    { get; init; }
    public string? AvatarThumb { get; init; }
    public int     VideoCount  { get; init; }
}
