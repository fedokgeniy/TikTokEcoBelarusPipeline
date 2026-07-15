using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Services;

namespace TikTokEcoBelarus.Pipeline;

public class ChannelMonitorPipeline(
    TikTokApiClient api,
    ITrackedChannelRepository channelRepo)
{
    /// <summary>
    /// Проверяет активные каналы на появление новых видео.
    ///
    /// Изменения относительно предыдущей версии:
    ///
    /// 1. GetUserInfoByIdAsync удалён — /api/user/info-by-id всегда возвращает 204 No Content,
    ///    все нужные поля (никнейм, аватарка, profileUrl) берём напрямую из GetUserInfoAsync.
    ///
    /// 2. GetVideoCommentsAsync отключён — /api/comment/list возвращает HTTP 404
    ///    («Endpoint does not exist»), эндпоинт удалён провайдером API.
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

        // 1. Все нужные метаданные (никнейм, аватарка, uid) читаем из /api/user/info.
        //    GetUserInfoByIdAsync больше не вызываем — эндпоинт /api/user/info-by-id
        //    постоянно возвращает 204 No Content.
        var userInfo = await api.GetUserInfoAsync(channel.UniqueId, ct);
        if (userInfo is null)
        {
            Console.Error.WriteLine($"[CHANNEL] @{channel.UniqueId}: failed to get user info, skipping.");
            return;
        }

        // 2. Актуализируем метаданные канала напрямую из userInfo
        bool metaChanged = false;

        if (channel.UserId is null && userInfo.UserId is not null)
        {
            channel.UserId = userInfo.UserId;
            metaChanged = true;
            Console.WriteLine($"[CHANNEL] @{channel.UniqueId}: resolved uid={channel.UserId}");
        }

        if (channel.ProfileUrl is null)
        {
            channel.ProfileUrl = userInfo.ProfileUrl ?? $"https://www.tiktok.com/@{channel.UniqueId}";
            metaChanged = true;
        }

        if (channel.DisplayName is null && userInfo.Nickname is not null)
        {
            channel.DisplayName = userInfo.Nickname;
            metaChanged = true;
        }

        if (channel.AvatarUrl is null && userInfo.AvatarThumb is not null)
        {
            channel.AvatarUrl = userInfo.AvatarThumb;
            metaChanged = true;
        }

        if (metaChanged)
            await channelRepo.SaveMetaAsync(channel);

        // 3. Реагируем только если видео стало БОЛЬШЕ
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

        // 4. Берём видео через поиск, фильтруем только новые
        var existingIds = await channelRepo.GetExistingVideoIdsAsync(channel.Id);
        var fetched     = new List<Models.TikTokItem>();

        await foreach (var item in api.SearchVideosAsync(channel.UniqueId, maxPages: 3, ct: ct))
        {
            if (!item.Author.UniqueId.Equals(channel.UniqueId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (existingIds.Contains(item.Id))
                continue;

            fetched.Add(item);

            if (fetched.Count >= Math.Min(delta, limit)) break;
        }

        // CommentCount берём из самих видео (фактический счётчик, отвечает действительности)
        int currentCommentCount = fetched.Sum(v => (int)v.Stats.CommentCount);

        // 5. Сохраняем новые видео
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

        // 6. Загрузка комментариев ОТКЛЮЧЕНА:
        //    /api/comment/list возвращает HTTP 404 — эндпоинт был удалён провайдером API.
        //    CommentCount уже хранится в TrackedChannelVideo.CommentCount из метаданных видео.
        //    Когда провайдер восстановит эндпоинт — добавить обратно:
        //      await foreach (var c in api.GetVideoCommentsAsync(item.Id, count: 20, ct)) { ... }

        // 7. Обновляем счётчики канала
        await channelRepo.UpdateAfterCheckAsync(
            channel.Id, currentVideoCount, currentCommentCount, DateTimeOffset.UtcNow);
    }
}
