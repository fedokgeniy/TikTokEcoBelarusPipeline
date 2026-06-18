using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Services;

namespace TikTokEcoBelarus.Pipeline;

public class ChannelMonitorPipeline(
    TikTokApiClient api,
    ITrackedChannelRepository channelRepo)
{
    /// <summary>
    /// Для каждого активного канала:
    /// 1. Запрашивает /api/user/info — точный videoCount.
    /// 2. Если videoCount изменился — делает keyword-поиск и сохраняет последние видео.
    /// 3. Обновляет LastVideoCount, LastCommentCount, LastCheckedAt.
    /// </summary>
    public async Task RunAsync(int latestVideosLimit = 10, CancellationToken ct = default)
    {
        var channels = await channelRepo.GetAllAsync();
        var activeChannels = channels.Where(c => c.IsActive).ToList();

        Console.WriteLine($"[CHANNEL MONITOR] Checking {activeChannels.Count} active channel(s)...");

        foreach (var channel in activeChannels)
        {
            try
            {
                await CheckChannelAsync(channel, latestVideosLimit, ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CHANNEL MONITOR ERROR] @{channel.UniqueId}: {ex.Message}");
            }
        }

        Console.WriteLine("[CHANNEL MONITOR] Done.");
    }

    private async Task CheckChannelAsync(
        TrackedChannel channel,
        int limit,
        CancellationToken ct)
    {
        Console.WriteLine($"[CHANNEL] Checking @{channel.UniqueId}...");

        // 1. Получаем точный videoCount через /api/user/info
        var userInfo = await api.GetUserInfoAsync(channel.UniqueId, ct);
        if (userInfo is null)
        {
            Console.Error.WriteLine($"[CHANNEL] @{channel.UniqueId}: failed to get user info, skipping.");
            return;
        }

        int currentVideoCount = userInfo.VideoCount;
        bool videoCountChanged = channel.LastVideoCount != currentVideoCount;

        // Опционально обновляем DisplayName и AvatarUrl если они ещё не заполнены
        if (channel.DisplayName is null && userInfo.Nickname is not null)
            channel.DisplayName = userInfo.Nickname;
        if (channel.AvatarUrl is null && userInfo.AvatarThumb is not null)
            channel.AvatarUrl = userInfo.AvatarThumb;

        if (!videoCountChanged)
        {
            Console.WriteLine($"[CHANNEL] @{channel.UniqueId}: no new videos (count={currentVideoCount}).");
            await channelRepo.UpdateAfterCheckAsync(
                channel.Id, currentVideoCount, channel.LastCommentCount ?? 0, DateTimeOffset.UtcNow);
            return;
        }

        Console.WriteLine(
            $"[CHANNEL] @{channel.UniqueId}: video count {channel.LastVideoCount ?? 0} → {currentVideoCount}. Fetching latest videos...");

        // 2. Если счётчик изменился — берём последние видео через keyword-поиск
        var fetchedVideos = new List<Models.TikTokItem>();
        await foreach (var item in api.SearchVideosAsync(channel.UniqueId, maxPages: 1, ct: ct))
        {
            if (!item.Author.UniqueId.Equals(channel.UniqueId, StringComparison.OrdinalIgnoreCase))
                continue;
            fetchedVideos.Add(item);
            if (fetchedVideos.Count >= limit) break;
        }

        int currentCommentCount = fetchedVideos.Sum(v => (int)v.Stats.CommentCount);
        bool commentCountChanged = channel.LastCommentCount != currentCommentCount;

        if (commentCountChanged)
            Console.WriteLine(
                $"[CHANNEL] @{channel.UniqueId}: comment sum {channel.LastCommentCount ?? 0} → {currentCommentCount}");

        // 3. Сохраняем видео
        if (fetchedVideos.Count > 0)
        {
            var videosToSave = fetchedVideos.Select(item => new TrackedChannelVideo
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

            await channelRepo.SaveVideosAsync(channel.Id, videosToSave);
            Console.WriteLine($"[CHANNEL] @{channel.UniqueId}: saved {videosToSave.Count} video(s).");
        }

        // 4. Обновляем счётчики в БД
        await channelRepo.UpdateAfterCheckAsync(
            channel.Id, currentVideoCount, currentCommentCount, DateTimeOffset.UtcNow);
    }
}
