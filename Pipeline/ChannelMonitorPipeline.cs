using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Services;

namespace TikTokEcoBelarus.Pipeline;

public class ChannelMonitorPipeline(
    TikTokApiClient api,
    ITrackedChannelRepository channelRepo)
{
    /// <summary>
    /// Итерируется по активным каналам.
    /// 1. Запрашивает /api/user/info по uniqueId — получает точный videoCount.
    /// 2. Если videoCount изменился — тянет видео через keyword-поиск.
    /// 3. Обновляет счётчики и сохраняет видео в БД.
    /// При первом запуске также резолвит UserId и ProfileUrl через /api/user/info-by-id.
    /// </summary>
    public async Task RunAsync(int latestVideosLimit = 10, CancellationToken ct = default)
    {
        var channels = await channelRepo.GetAllAsync();
        var active   = channels.Where(c => c.IsActive).ToList();

        Console.WriteLine($"[CHANNEL MONITOR] Checking {active.Count} active channel(s)...");

        foreach (var channel in active)
        {
            try   { await CheckChannelAsync(channel, latestVideosLimit, ct); }
            catch (Exception ex)
            { Console.Error.WriteLine($"[CHANNEL MONITOR ERROR] @{channel.UniqueId}: {ex.Message}"); }
        }

        Console.WriteLine("[CHANNEL MONITOR] Done.");
    }

    private async Task CheckChannelAsync(TrackedChannel channel, int limit, CancellationToken ct)
    {
        Console.WriteLine($"[CHANNEL] Checking @{channel.UniqueId}...");

        // 1. Получаем videoCount через /api/user/info (единственный endpoint с этим полем)
        var userInfo = await api.GetUserInfoAsync(channel.UniqueId, ct);
        if (userInfo is null)
        {
            Console.Error.WriteLine($"[CHANNEL] @{channel.UniqueId}: failed to get user info, skipping.");
            return;
        }

        // 2. Если UserId ещё не сохранён — резолвим через /api/user/info-by-id для получения share_url
        if (channel.UserId is null && userInfo.UserId is not null)
        {
            var byId = await api.GetUserInfoByIdAsync(userInfo.UserId, ct);
            channel.UserId     = userInfo.UserId;
            channel.ProfileUrl = byId?.ProfileUrl ?? $"https://www.tiktok.com/@{channel.UniqueId}";
            if (channel.DisplayName is null) channel.DisplayName = byId?.Nickname ?? userInfo.Nickname;
            if (channel.AvatarUrl   is null) channel.AvatarUrl   = byId?.AvatarThumb ?? userInfo.AvatarThumb;
            await channelRepo.SaveMetaAsync(channel);
            Console.WriteLine($"[CHANNEL] @{channel.UniqueId}: resolved uid={channel.UserId} profileUrl={channel.ProfileUrl}");
        }
        else
        {
            // Обновляем DisplayName/AvatarUrl если были пусты
            bool metaChanged = false;
            if (channel.DisplayName is null && userInfo.Nickname    is not null) { channel.DisplayName = userInfo.Nickname;    metaChanged = true; }
            if (channel.AvatarUrl   is null && userInfo.AvatarThumb is not null) { channel.AvatarUrl   = userInfo.AvatarThumb; metaChanged = true; }
            if (metaChanged) await channelRepo.SaveMetaAsync(channel);
        }

        // 3. Сравниваем videoCount
        int  currentVideoCount = userInfo.VideoCount;
        bool videoCountChanged = channel.LastVideoCount != currentVideoCount;

        if (!videoCountChanged)
        {
            Console.WriteLine($"[CHANNEL] @{channel.UniqueId}: no new videos (count={currentVideoCount}).");
            await channelRepo.UpdateAfterCheckAsync(channel.Id, currentVideoCount, channel.LastCommentCount ?? 0, DateTimeOffset.UtcNow);
            return;
        }

        Console.WriteLine(
            $"[CHANNEL] @{channel.UniqueId}: video count {channel.LastVideoCount ?? 0} → {currentVideoCount}. Fetching latest...");

        // 4. Если видео добавились — тянем через keyword-поиск
        var fetched = new List<Models.TikTokItem>();
        await foreach (var item in api.SearchVideosAsync(channel.UniqueId, maxPages: 1, ct: ct))
        {
            if (!item.Author.UniqueId.Equals(channel.UniqueId, StringComparison.OrdinalIgnoreCase))
                continue;
            fetched.Add(item);
            if (fetched.Count >= limit) break;
        }

        int  currentCommentCount = fetched.Sum(v => (int)v.Stats.CommentCount);
        bool commentCountChanged = channel.LastCommentCount != currentCommentCount;

        if (commentCountChanged)
            Console.WriteLine(
                $"[CHANNEL] @{channel.UniqueId}: comment sum {channel.LastCommentCount ?? 0} → {currentCommentCount}");

        // 5. Сохраняем видео
        if (fetched.Count > 0)
        {
            var toSave = fetched.Select(item => new TrackedChannelVideo
            {
                TrackedChannelId = channel.Id,
                VideoId          = item.Id,
                Description      = item.Desc.Length > 1000 ? item.Desc[..1000] : item.Desc,
                CommentCount     = item.Stats.CommentCount,
                LikeCount        = item.Stats.DiggCount,
                PlayCount        = item.Stats.PlayCount,
                ShareCount       = item.Stats.ShareCount,
                VideoCreatedAt   = item.CreatedAt,
                FetchedAt        = DateTime.UtcNow
            }).ToList();

            await channelRepo.SaveVideosAsync(channel.Id, toSave);
            Console.WriteLine($"[CHANNEL] @{channel.UniqueId}: saved {toSave.Count} video(s).");
        }

        // 6. Обновляем счётчики
        await channelRepo.UpdateAfterCheckAsync(
            channel.Id, currentVideoCount, currentCommentCount, DateTimeOffset.UtcNow);
    }
}
