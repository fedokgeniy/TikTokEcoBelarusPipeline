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
    /// 1. Запрашивает /api/user/info — получает точный videoCount.
    /// 2. Если currentVideoCount > LastVideoCount — вычисляет delta.
    /// 3. Тянет видео из поиска, фильтруя только те VideoId, которых ещё нет в БД.
    /// 4. Сохраняет новые видео в TrackedChannelVideos.
    /// 5. Для каждого нового видео загружает последние комментарии и сохраняет их.
    /// 6. Обновляет счётчики канала.
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

        // 1. Получаем videoCount через /api/user/info
        var userInfo = await api.GetUserInfoAsync(channel.UniqueId, ct);
        if (userInfo is null)
        {
            Console.Error.WriteLine($"[CHANNEL] @{channel.UniqueId}: failed to get user info, skipping.");
            return;
        }

        // 2. Резолвим метаданные при первом запуске
        if (channel.UserId is null && userInfo.UserId is not null)
        {
            var byId = await api.GetUserInfoByIdAsync(userInfo.UserId, ct);
            channel.UserId     = userInfo.UserId;
            channel.ProfileUrl = byId?.ProfileUrl ?? $"https://www.tiktok.com/@{channel.UniqueId}";
            if (channel.DisplayName is null) channel.DisplayName = byId?.Nickname ?? userInfo.Nickname;
            if (channel.AvatarUrl   is null) channel.AvatarUrl   = byId?.AvatarThumb ?? userInfo.AvatarThumb;
            await channelRepo.SaveMetaAsync(channel);
            Console.WriteLine($"[CHANNEL] @{channel.UniqueId}: resolved uid={channel.UserId}");
        }
        else
        {
            bool metaChanged = false;
            if (channel.DisplayName is null && userInfo.Nickname    is not null) { channel.DisplayName = userInfo.Nickname;    metaChanged = true; }
            if (channel.AvatarUrl   is null && userInfo.AvatarThumb is not null) { channel.AvatarUrl   = userInfo.AvatarThumb; metaChanged = true; }
            if (metaChanged) await channelRepo.SaveMetaAsync(channel);
        }

        // 3. Cross-validation: реагируем ТОЛЬКО если видео стало БОЛЬШЕ
        int currentVideoCount = userInfo.VideoCount;
        int previousCount     = channel.LastVideoCount ?? 0;
        int delta             = currentVideoCount - previousCount;

        if (delta <= 0)
        {
            Console.WriteLine(
                $"[CHANNEL] @{channel.UniqueId}: no new videos " +
                $"(prev={previousCount}, current={currentVideoCount}).");
            await channelRepo.UpdateAfterCheckAsync(
                channel.Id, currentVideoCount, channel.LastCommentCount ?? 0, DateTimeOffset.UtcNow);
            return;
        }

        Console.WriteLine(
            $"[CHANNEL] @{channel.UniqueId}: {delta} new video(s) detected " +
            $"({previousCount} → {currentVideoCount}). Fetching delta...");

        // 4. Загружаем видео из поиска, берём только те VideoId, которых ещё нет в БД
        var existingIds = await channelRepo.GetExistingVideoIdsAsync(channel.Id);
        var fetched     = new List<Models.TikTokItem>();

        // maxPages=3 даёт до ~36 видео — с запасом даже при большом delta
        await foreach (var item in api.SearchVideosAsync(channel.UniqueId, maxPages: 3, ct: ct))
        {
            if (!item.Author.UniqueId.Equals(channel.UniqueId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (existingIds.Contains(item.Id))
                continue; // уже есть в БД — пропускаем

            fetched.Add(item);

            // Ограничиваем: не тянем больше чем delta (или limit, что меньше)
            if (fetched.Count >= Math.Min(delta, limit)) break;
        }

        int currentCommentCount = fetched.Sum(v => (int)v.Stats.CommentCount);

        // 5. Сохраняем новые видео в TrackedChannelVideos
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
            Console.WriteLine($"[CHANNEL] @{channel.UniqueId}: saved {toSave.Count} new video(s).");
        }

        // 6. Скачиваем последние комментарии для каждого нового видео
        var allComments = new List<VideoComment>();

        foreach (var item in fetched)
        {
            Console.WriteLine($"[CHANNEL] Fetching comments for video {item.Id}...");
            await foreach (var c in api.GetVideoCommentsAsync(item.Id, count: 20, ct: ct))
            {
                allComments.Add(new VideoComment
                {
                    VideoId          = item.Id,
                    CommentId        = c.CommentId,
                    Text             = c.Text.Length > 2000 ? c.Text[..2000] : c.Text,
                    AuthorUniqueId   = c.AuthorUniqueId,
                    LikeCount        = c.LikeCount,
                    CommentCreatedAt = c.CreatedAt,
                    FetchedAt        = DateTime.UtcNow
                });
            }
            // Небольшая задержка между запросами комментариев (rate limit)
            await Task.Delay(800, ct);
        }

        if (allComments.Count > 0)
        {
            await channelRepo.SaveCommentsAsync(allComments);
            Console.WriteLine(
                $"[CHANNEL] @{channel.UniqueId}: saved {allComments.Count} comment(s) " +
                $"across {fetched.Count} video(s).");
        }

        // 7. Обновляем счётчики канала
        await channelRepo.UpdateAfterCheckAsync(
            channel.Id, currentVideoCount, currentCommentCount, DateTimeOffset.UtcNow);
    }
}
