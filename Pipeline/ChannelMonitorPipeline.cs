using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Services;

namespace TikTokEcoBelarus.Pipeline;

public class ChannelMonitorPipeline(
    TikTokApiClient api,
    ITrackedChannelRepository channelRepo)
{
    /// <summary>
    /// Для каждого активного канала проверяет, изменилось ли количество видео.
    /// Если изменилось — тянет последние видео и обновляет счётчики.
    /// </summary>
    public async Task RunAsync(int latestVideosLimit = 10, CancellationToken ct = default)
    {
        var channels = await channelRepo.GetAllAsync();
        var activeChannels = channels.Where(c => c.IsActive).ToList();

        Console.WriteLine($"[CHANNEL MONITOR] Checking {activeChannels.Count} active channels...");

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

        // Ищем свежие видео этого автора через keyword-поиск по @username
        // TikTok API (RapidAPI tiktok-api23) не имеет отдельного endpoint user/videos,
        // поэтому используем SearchVideosAsync с username как keyword.
        // Если в будущем появится user-videos endpoint — заменить этот блок.
        var fetchedVideos = new List<Models.TikTokItem>();
        await foreach (var item in api.SearchVideosAsync(channel.UniqueId, maxPages: 1, ct: ct))
        {
            // Берём только видео именно этого автора
            if (!item.Author.UniqueId.Equals(channel.UniqueId, StringComparison.OrdinalIgnoreCase))
                continue;
            fetchedVideos.Add(item);
            if (fetchedVideos.Count >= limit) break;
        }

        int currentVideoCount = fetchedVideos.Count > 0
            ? fetchedVideos[0].AuthorStats.VideoCount  // берём из первого попавшегося видео автора
            : channel.LastVideoCount ?? 0;

        int currentCommentCount = fetchedVideos.Sum(v => (int)v.Stats.CommentCount);

        bool videoCountChanged   = channel.LastVideoCount   != currentVideoCount;
        bool commentCountChanged = channel.LastCommentCount != currentCommentCount;

        if (videoCountChanged)
            Console.WriteLine(
                $"[CHANNEL] @{channel.UniqueId}: video count {channel.LastVideoCount ?? 0} → {currentVideoCount}");

        if (commentCountChanged)
            Console.WriteLine(
                $"[CHANNEL] @{channel.UniqueId}: comment count {channel.LastCommentCount ?? 0} → {currentCommentCount}");

        if (!videoCountChanged && !commentCountChanged)
        {
            Console.WriteLine($"[CHANNEL] @{channel.UniqueId}: no changes.");
            await channelRepo.UpdateAfterCheckAsync(channel.Id, currentVideoCount, currentCommentCount, DateTimeOffset.UtcNow);
            return;
        }

        // Сохраняем новые видео
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
        await channelRepo.UpdateAfterCheckAsync(channel.Id, currentVideoCount, currentCommentCount, DateTimeOffset.UtcNow);

        Console.WriteLine($"[CHANNEL] @{channel.UniqueId}: saved {videosToSave.Count} video(s).");
    }
}
