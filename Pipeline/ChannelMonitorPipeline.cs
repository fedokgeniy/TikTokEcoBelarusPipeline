using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Services;

namespace TikTokEcoBelarus.Pipeline;

/// <summary>
/// Проверяет активные каналы на появление новых видео, собирает комментарии,
/// сохраняет снапшоты роста и классифицирует комментарии через Claude Haiku.
/// </summary>
public class ChannelMonitorPipeline(
    TikTokApiClient            api,
    ITrackedChannelRepository  channelRepo,
    CommentClassifierService   classifier)
{
    public async Task RunAsync(
        int latestVideosLimit = 10,
        int maxVideoAgeDays   = 5,
        CancellationToken ct  = default)
    {
        var channels = await channelRepo.GetAllAsync();
        var active   = channels.Where(c => c.IsActive).ToList();

        Console.WriteLine($"[CHANNEL MONITOR] Checking {active.Count} active channel(s)...");

        foreach (var channel in active)
        {
            try   { await CheckChannelAsync(channel, latestVideosLimit, maxVideoAgeDays, ct); }
            catch (Exception ex)
            { Console.Error.WriteLine($"[CHANNEL MONITOR ERROR] @{channel.UniqueId}: {ex.Message}"); }
        }

        Console.WriteLine("[CHANNEL MONITOR] Done.");
    }

    private async Task CheckChannelAsync(
        TrackedChannel channel, int limit, int maxVideoAgeDays, CancellationToken ct)
    {
        Console.WriteLine($"[CHANNEL] Checking @{channel.UniqueId}...");

        var userInfo = await api.GetUserInfoAsync(channel.UniqueId, ct);
        if (userInfo is null)
        {
            Console.Error.WriteLine($"[CHANNEL] @{channel.UniqueId}: failed to get user info, skipping.");
            return;
        }

        bool metaChanged = false;

        if (channel.UserId is null && userInfo.UserId is not null)
        { channel.UserId = userInfo.UserId; metaChanged = true;
          Console.WriteLine($"[CHANNEL] @{channel.UniqueId}: resolved uid={channel.UserId}"); }

        if (channel.ProfileUrl is null)
        { channel.ProfileUrl = userInfo.ProfileUrl ?? $"https://www.tiktok.com/@{channel.UniqueId}"; metaChanged = true; }

        if (channel.DisplayName is null && userInfo.Nickname is not null)
        { channel.DisplayName = userInfo.Nickname; metaChanged = true; }

        if (channel.AvatarUrl is null && userInfo.AvatarThumb is not null)
        { channel.AvatarUrl = userInfo.AvatarThumb; metaChanged = true; }

        if (metaChanged)
            await channelRepo.SaveMetaAsync(channel);

        int currentVideoCount = userInfo.VideoCount;
        int previousCount     = channel.LastVideoCount ?? 0;
        int delta             = currentVideoCount - previousCount;

        if (delta <= 0)
        {
            Console.WriteLine(
                $"[CHANNEL] @{channel.UniqueId}: no new videos " +
                $"(prev={previousCount}, current={currentVideoCount}).");

            await FetchCommentsForRecentVideosAsync(channel, maxVideoAgeDays, ct);

            await channelRepo.UpdateAfterCheckAsync(
                channel.Id, currentVideoCount, channel.LastCommentCount ?? 0, DateTimeOffset.UtcNow);
            return;
        }

        Console.WriteLine(
            $"[CHANNEL] @{channel.UniqueId}: {delta} new video(s) detected " +
            $"({previousCount} → {currentVideoCount}). Fetching delta...");

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

        int currentCommentCount = fetched.Sum(v => (int)v.Stats.CommentCount);

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

        await FetchCommentsForRecentVideosAsync(channel, maxVideoAgeDays, ct);

        await channelRepo.UpdateAfterCheckAsync(
            channel.Id, currentVideoCount, currentCommentCount, DateTimeOffset.UtcNow);
    }

    private async Task FetchCommentsForRecentVideosAsync(
        TrackedChannel channel, int maxVideoAgeDays, CancellationToken ct)
    {
        var videos = await channelRepo.GetVideosForCommentFetchAsync(channel.Id, maxVideoAgeDays);

        Console.WriteLine(
            $"[COMMENTS] @{channel.UniqueId}: {videos.Count} video(s) ≤ {maxVideoAgeDays}d old to process.");

        foreach (var video in videos)
        {
            ct.ThrowIfCancellationRequested();

            await channelRepo.SaveSnapshotAsync(new VideoCommentSnapshot
            {
                VideoId      = video.VideoId,
                SnapshotAt   = DateTimeOffset.UtcNow,
                CommentCount = video.CommentCount
            });

            var collected = new List<VideoComment>();

            await foreach (var c in api.GetVideoCommentsAsync(
                video.VideoId,
                pageSize: 50,
                maxPages: 200,
                ct: ct))
            {
                collected.Add(new VideoComment
                {
                    VideoId          = video.VideoId,
                    CommentId        = c.CommentId,
                    Text             = c.Text.Length > 2000 ? c.Text[..2000] : c.Text,
                    AuthorUniqueId   = c.AuthorUniqueId,
                    LikeCount        = c.LikeCount,
                    ReplyCount       = c.ReplyCount,
                    CommentCreatedAt = c.CreatedAt,
                    FetchedAt        = DateTime.UtcNow
                });
            }

            Console.WriteLine(
                $"[COMMENTS] videoId={video.VideoId}: collected {collected.Count} comment(s).");

            if (collected.Count == 0) continue;

            var insertedIds = await channelRepo.SaveCommentsAsync(collected);

            if (insertedIds.Count == 0)
            {
                Console.WriteLine(
                    $"[COMMENTS] videoId={video.VideoId}: all comments already in DB, skipping classification.");
                continue;
            }

            var toClassify = collected
                .Where(c => insertedIds.Contains(c.CommentId))
                .ToList();

            Console.WriteLine(
                $"[CLASSIFIER] videoId={video.VideoId}: classifying {toClassify.Count} new comment(s)...");

            Dictionary<string, ClassifyResult> classified;
            try
            {
                classified = await classifier.ClassifyAsync(toClassify, ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CLASSIFIER ERROR] videoId={video.VideoId}: {ex.Message}");
                continue;
            }

            // Батч-апдейт: один DbContext на все результаты вместо N отдельных контекстов
            try
            {
                await channelRepo.UpdateCommentClassificationBatchAsync(classified, ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CLASSIFIER SAVE ERROR] videoId={video.VideoId}: {ex.Message}");
            }

            int relevantCount = classified.Values.Count(v => v.IsRelevant);
            Console.WriteLine(
                $"[CLASSIFIER] videoId={video.VideoId}: {relevantCount}/{classified.Count} relevant.");

            await Task.Delay(1000, ct);
        }
    }
}
