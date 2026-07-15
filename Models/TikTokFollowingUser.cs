namespace TikTokEcoBelarus.Models;

/// <summary>
/// Пользователь из списка подписок /api/user/followings.
/// </summary>
public class TikTokFollowingUser
{
    public string  UniqueId { get; init; } = default!;
    public string? Nickname { get; init; }
    public string? SecUid   { get; init; }
}
