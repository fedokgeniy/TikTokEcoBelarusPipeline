using System.Runtime.CompilerServices;
using System.Text.Json;
using TikTokEcoBelarus.Models;

namespace TikTokEcoBelarus.Services;

public class TikTokApiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private const string BaseUrl = "https://tiktok-api23.p.rapidapi.com";

    // Retry config for transient API responses (202, 204)
    private const int TransientMaxRetries  = 4;
    private const int TransientRetryBaseMs = 5000;

    public TikTokApiClient(HttpClient http, string apiKey)
    {
        _http = http;
        _apiKey = apiKey;
        _http.DefaultRequestHeaders.Add("x-rapidapi-host", "tiktok-api23.p.rapidapi.com");
        _http.DefaultRequestHeaders.Add("x-rapidapi-key", _apiKey);
    }

    // ---------------------------------------------------------------
    // GET /api/user/info?uniqueId=username
    // ---------------------------------------------------------------
    public async Task<TikTokUserInfo?> GetUserInfoAsync(
        string uniqueId,
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/api/user/info?uniqueId={Uri.EscapeDataString(uniqueId)}";
        Console.WriteLine($"[USER INFO] GET {url}");

        var body = await SafeGetWithRetryAsync(url, ct, tag: $"USER INFO @{uniqueId}");
        if (body is null) return null;

        try
        {
            var doc  = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var userInfoNode = root.GetProperty("userInfo");
            var statsNode    = userInfoNode.GetProperty("stats");
            var userNode     = userInfoNode.GetProperty("user");

            int     videoCount  = statsNode.GetProperty("videoCount").GetInt32();
            string? userId      = userNode.TryGetProperty("id",       out var idEl)     ? idEl.GetString()     : null;
            string? secUid      = userNode.TryGetProperty("secUid",   out var secUidEl) ? secUidEl.GetString() : null;
            string? nickname    = userNode.TryGetProperty("nickname",  out var nnEl)     ? nnEl.GetString()     : null;
            string? avatarThumb = TryGetFirstUrl(userNode, "avatarThumb");
            string  profileUrl  = $"https://www.tiktok.com/@{uniqueId}";

            Console.WriteLine($"[USER INFO] @{uniqueId} uid={userId} secUid={secUid?[..Math.Min(20, secUid?.Length ?? 0)]}... videos={videoCount}");

            return new TikTokUserInfo
            {
                UniqueId    = uniqueId,
                UserId      = userId,
                SecUid      = secUid,
                Nickname    = nickname,
                AvatarThumb = avatarThumb,
                ProfileUrl  = profileUrl,
                VideoCount  = videoCount
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[USER INFO PARSE ERROR] @{uniqueId}: {ex.Message}");
            Console.Error.WriteLine($"Body: {body[..Math.Min(500, body.Length)]}");
            return null;
        }
    }

    // ---------------------------------------------------------------
    // GET /api/user/info-by-id?userId=UID
    // ---------------------------------------------------------------
    public async Task<TikTokUserInfo?> GetUserInfoByIdAsync(
        string userId,
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/api/user/info-by-id?userId={Uri.EscapeDataString(userId)}";
        Console.WriteLine($"[USER INFO BY ID] GET {url}");

        var body = await SafeGetWithRetryAsync(url, ct, tag: $"USER INFO BY ID uid={userId}");
        if (body is null) return null;

        try
        {
            var doc      = JsonDocument.Parse(body);
            var userNode = doc.RootElement.GetProperty("user");

            string? uniqueId    = userNode.TryGetProperty("unique_id", out var uidEl)    ? uidEl.GetString()    : null;
            string? secUid      = userNode.TryGetProperty("secUid",    out var secUidEl) ? secUidEl.GetString() : null;
            string? nickname    = userNode.TryGetProperty("nickname",  out var nnEl)     ? nnEl.GetString()     : null;
            string? avatarThumb = TryGetFirstJpegUrl(userNode, "avatar_thumb");

            string? profileUrl = null;
            if (userNode.TryGetProperty("share_info", out var shareInfo)
             && shareInfo.TryGetProperty("share_url", out var shareUrlEl))
                profileUrl = shareUrlEl.GetString();

            Console.WriteLine($"[USER INFO BY ID] uid={userId} uniqueId={uniqueId} nickname={nickname}");

            return new TikTokUserInfo
            {
                UniqueId    = uniqueId ?? userId,
                UserId      = userId,
                SecUid      = secUid,
                Nickname    = nickname,
                AvatarThumb = avatarThumb,
                ProfileUrl  = profileUrl,
                VideoCount  = 0
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[USER INFO BY ID PARSE ERROR] uid={userId}: {ex.Message}");
            Console.Error.WriteLine($"Body: {body[..Math.Min(500, body.Length)]}");
            return null;
        }
    }

    // ---------------------------------------------------------------
    // GET /api/user/followings
    // ---------------------------------------------------------------
    public async Task<List<TikTokFollowingUser>> GetUserFollowingsAsync(
        string secUid,
        int maxCount = 200,
        CancellationToken ct = default)
    {
        var result   = new List<TikTokFollowingUser>();
        long? cursor = null;
        int pageSize = 30;
        int page     = 0;
        var seenIds  = new HashSet<string>();

        Console.WriteLine($"[FOLLOWINGS] secUid={secUid[..Math.Min(20, secUid.Length)]}... maxCount={maxCount}");

        while (result.Count < maxCount)
        {
            ct.ThrowIfCancellationRequested();

            var urlBuilder = new System.Text.StringBuilder();
            urlBuilder.Append($"{BaseUrl}/api/user/followings");
            urlBuilder.Append($"?secUid={Uri.EscapeDataString(secUid)}");
            urlBuilder.Append($"&count={pageSize}");
            if (cursor.HasValue)
                urlBuilder.Append($"&maxCursor={cursor.Value}");

            var url = urlBuilder.ToString();
            Console.WriteLine($"[FOLLOWINGS] Page {page + 1}, already={result.Count}, cursor={cursor?.ToString() ?? "(first)"}");

            var body = await SafeGetWithRetryAsync(url, ct, tag: $"FOLLOWINGS page={page + 1}");
            if (body is null) break;

            Console.WriteLine($"[FOLLOWINGS RAW] {body[..Math.Min(500, body.Length)]}");

            try
            {
                var doc  = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (!root.TryGetProperty("followings", out var followingsArr)
                 || followingsArr.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("[FOLLOWINGS] No 'followings' array — stopping.");
                    break;
                }

                int added = 0;
                foreach (var u in followingsArr.EnumerateArray())
                {
                    if (result.Count >= maxCount) break;

                    string? uniqueId = null;
                    if      (u.TryGetProperty("uniqueId",  out var uEl))  uniqueId = uEl.GetString();
                    else if (u.TryGetProperty("unique_id", out var u2El)) uniqueId = u2El.GetString();

                    string? nickname   = u.TryGetProperty("nickname", out var nnEl) ? nnEl.GetString() : null;
                    string? secUidUser = u.TryGetProperty("secUid",   out var sEl)  ? sEl.GetString()  : null;

                    if (string.IsNullOrWhiteSpace(uniqueId)) continue;
                    if (!seenIds.Add(uniqueId)) continue;

                    result.Add(new TikTokFollowingUser
                    {
                        UniqueId = uniqueId,
                        Nickname = nickname,
                        SecUid   = secUidUser
                    });
                    added++;
                }

                page++;
                Console.WriteLine($"[FOLLOWINGS] Page {page}: added={added}, total={result.Count}");

                if (added == 0)
                {
                    Console.WriteLine("[FOLLOWINGS] Empty page — all followings collected.");
                    break;
                }

                if (root.TryGetProperty("hasMore", out var hmEl))
                    Console.WriteLine($"[FOLLOWINGS] hasMore={hmEl} (ignored — known API bug)");

                long? nextCursor = null;
                foreach (var field in new[] { "maxCursor", "cursor", "minCursor", "minTime" })
                {
                    if (root.TryGetProperty(field, out var cEl))
                    {
                        long val = 0;
                        if (cEl.ValueKind == JsonValueKind.Number) val = cEl.GetInt64();
                        else if (cEl.ValueKind == JsonValueKind.String) long.TryParse(cEl.GetString(), out val);
                        if (val > 0) { nextCursor = val; break; }
                    }
                }

                Console.WriteLine($"[FOLLOWINGS] nextCursor={nextCursor}");

                if (nextCursor is null)
                {
                    Console.WriteLine("[FOLLOWINGS] No cursor in response — stopping.");
                    break;
                }

                if (nextCursor == cursor)
                {
                    Console.WriteLine("[FOLLOWINGS] Cursor unchanged — stopping to avoid loop.");
                    break;
                }

                cursor = nextCursor;
                await Task.Delay(900, ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FOLLOWINGS PARSE ERROR] {ex.Message}");
                Console.Error.WriteLine($"Body: {body[..Math.Min(500, body.Length)]}");
                break;
            }
        }

        Console.WriteLine($"[FOLLOWINGS DONE] Total found: {result.Count}");
        return result;
    }

    // ---------------------------------------------------------------
    // GET /api/search/video?keyword=...
    //
    // Известные особенности RapidAPI TikTok endpoint:
    //   - cursor может зависать на одном значении (напр. 12) на многих страницах
    //   - has_more=0 не отражает реальное наличие новых элементов
    //   - offset-based пагинация: offset += фактическое кол-во элементов страницы
    //
    // Стратегия остановки (streak-based, tolerant к плохому API):
    //   - emptyPageStreak >= 2   : подряд страниц с пустым itemList
    //   - duplicatePageStreak >= 3: подряд страниц без новых videoId
    //   - maxPages               : защитный лимит
    // ---------------------------------------------------------------
    public async IAsyncEnumerable<TikTokItem> SearchVideosAsync(
        string keyword,
        int maxPages = 100,
        int delayMs  = 1500,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        const int PageSize            = 10; // fallback если API вернул 0 элементов
        const int EmptyPageLimit      = 2;  // остановка после N подряд пустых страниц
        const int DuplicatePageLimit  = 3;  // остановка после N подряд страниц без новых ID
        const int LowYieldPageLimit   = 5;  // предупреждение при N подряд страницах с < 2 новых

        int offset             = 0;
        int page               = 0;
        int emptyPageStreak    = 0;
        int duplicatePageStreak = 0;
        int lowYieldStreak     = 0;
        var seenIds            = new HashSet<string>();

        while (page < maxPages)
        {
            ct.ThrowIfCancellationRequested();

            var url = $"{BaseUrl}/api/search/video" +
                      $"?keyword={Uri.EscapeDataString(keyword)}" +
                      $"&offset={offset}";

            Console.WriteLine($"[SEARCH] Page {page + 1}/{maxPages} offset={offset}");

            var body = await SafeGetStringAsync(url, ct);
            if (body is null)
            {
                Console.WriteLine($"[SEARCH END] HTTP error or null body — stopping. keyword=\"{keyword}\"");
                break;
            }

            TikTokSearchResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<TikTokSearchResponse>(body);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SEARCH PARSE ERROR] {ex.Message}");
                Console.Error.WriteLine($"Body: {body[..Math.Min(500, body.Length)]}");
                Console.WriteLine($"[SEARCH END] parse error — stopping. keyword=\"{keyword}\"");
                break;
            }

            // --- Пустая страница ---
            if (response?.ItemList is null || response.ItemList.Count == 0)
            {
                emptyPageStreak++;
                Console.WriteLine(
                    $"[SEARCH EMPTY] Page {page + 1}: itemList null/empty " +
                    $"(emptyPageStreak={emptyPageStreak}/{EmptyPageLimit})");

                if (emptyPageStreak >= EmptyPageLimit)
                {
                    Console.WriteLine(
                        $"[SEARCH END] repeated empty pages (emptyPageStreak={emptyPageStreak}) — stopping. " +
                        $"keyword=\"{keyword}\"");
                    break;
                }

                // advance offset by fallback pageSize to try shifting the window
                offset += PageSize;
                page++;
                await Task.Delay(delayMs, ct);
                continue;
            }

            // Сброс счётчика пустых страниц
            emptyPageStreak = 0;

            int rawCount = response.ItemList.Count;

            // Логируем первый и последний videoId для анализа перекрытий
            string firstId = response.ItemList[0].Id ?? "null";
            string lastId  = response.ItemList[rawCount - 1].Id ?? "null";
            Console.WriteLine($"[SEARCH PAGE] firstId={firstId} lastId={lastId}");

            // --- Дедупликация по videoId ---
            var newItems = response.ItemList
                .Where(item => !string.IsNullOrEmpty(item.Id) && seenIds.Add(item.Id))
                .ToList();

            int newCount    = newItems.Count;
            int offsetNext  = offset + rawCount;

            Console.WriteLine(
                $"[SEARCH OK] Page {page + 1}: raw={rawCount} new={newCount} " +
                $"offsetNext={offsetNext} " +
                $"cursor={response.Cursor} has_more={response.HasMore} [both ignored]");

            // --- Дублирующая страница ---
            if (newCount == 0)
            {
                duplicatePageStreak++;
                Console.WriteLine(
                    $"[SEARCH DUP] Page {page + 1}: all {rawCount} items already seen " +
                    $"(duplicatePageStreak={duplicatePageStreak}/{DuplicatePageLimit})");

                if (duplicatePageStreak >= DuplicatePageLimit)
                {
                    Console.WriteLine(
                        $"[SEARCH END] repeated duplicate pages (duplicatePageStreak={duplicatePageStreak}) — stopping. " +
                        $"keyword=\"{keyword}\"");
                    break;
                }
            }
            else
            {
                // Новые элементы найдены — сбрасываем streak дублей
                duplicatePageStreak = 0;

                // Проверка низкого выхода (опционально — только лог, не остановка)
                if (newCount < 2)
                {
                    lowYieldStreak++;
                    if (lowYieldStreak >= LowYieldPageLimit)
                        Console.WriteLine(
                            $"[SEARCH WARN] lowYieldStreak={lowYieldStreak}: " +
                            $"consistently low new-video yield per page");
                }
                else
                {
                    lowYieldStreak = 0;
                }

                foreach (var item in newItems)
                    yield return item;
            }

            offset = offsetNext;
            page++;

            if (page < maxPages)
                await Task.Delay(delayMs, ct);
        }

        if (page >= maxPages)
            Console.WriteLine($"[SEARCH END] max pages reached ({maxPages}) — stopping. keyword=\"{keyword}\"");
    }

    // ---------------------------------------------------------------
    // GET /api/post/comments?videoId=...&count=50&cursor=N
    //
    // Эндпоинт /api/comment/list удалён провайдером (HTTP 404).
    // Новый рабочий эндпоинт: /api/post/comments.
    // Пагинация: cursor из поля "cursor" в ответе; stop когда hasMore=false
    // или cursor не меняется.
    // ---------------------------------------------------------------
    public async IAsyncEnumerable<TikTokComment> GetVideoCommentsAsync(
        string videoId,
        int pageSize = 50,
        int maxPages = 20,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        long cursor  = 0;
        int  page    = 0;
        var  seenIds = new HashSet<string>();

        while (page < maxPages)
        {
            ct.ThrowIfCancellationRequested();

            var url = $"{BaseUrl}/api/post/comments" +
                      $"?videoId={Uri.EscapeDataString(videoId)}" +
                      $"&count={pageSize}" +
                      $"&cursor={cursor}";

            Console.WriteLine($"[COMMENTS] videoId={videoId} page={page + 1} cursor={cursor}");

            var body = await SafeGetStringAsync(url, ct);
            if (body is null) break;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[COMMENTS PARSE ERROR] videoId={videoId}: {ex.Message}");
                break;
            }

            var root = doc.RootElement;

            if (!root.TryGetProperty("comments", out var commentsArr)
             || commentsArr.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine($"[COMMENTS EMPTY] videoId={videoId} page={page + 1}");
                break;
            }

            int yielded = 0;
            foreach (var c in commentsArr.EnumerateArray())
            {
                string? commentId  = c.TryGetProperty("cid",               out var cidEl)   ? cidEl.GetString()   : null;
                string? text       = c.TryGetProperty("text",              out var textEl)  ? textEl.GetString()  : null;
                long    likeCount  = c.TryGetProperty("digg_count",        out var lkEl)    ? lkEl.GetInt64()    : 0;
                long    replyTotal = c.TryGetProperty("reply_comment_total",out var rtEl)   ? rtEl.GetInt64()   : 0;
                long    createTime = c.TryGetProperty("create_time",       out var ctEl)    ? ctEl.GetInt64()    : 0;

                string? authorId = null;
                string? authorUniqueId = null;
                if (c.TryGetProperty("user", out var userEl))
                {
                    authorId       = userEl.TryGetProperty("uid",       out var uidEl)  ? uidEl.GetString()  : null;
                    authorUniqueId = userEl.TryGetProperty("unique_id", out var uniqEl) ? uniqEl.GetString() : null;
                }

                if (commentId is null || text is null) continue;
                if (!seenIds.Add(commentId)) continue;

                yield return new TikTokComment
                {
                    CommentId      = commentId,
                    Text           = text,
                    AuthorUniqueId = authorUniqueId ?? authorId ?? string.Empty,
                    LikeCount      = likeCount,
                    ReplyCount     = replyTotal,
                    CreatedAt      = DateTimeOffset.FromUnixTimeSeconds(createTime)
                };
                yielded++;
            }

            Console.WriteLine($"[COMMENTS] videoId={videoId} page={page + 1}: yielded={yielded}");

            bool hasMore = root.TryGetProperty("hasMore", out var hmEl) && hmEl.GetBoolean();

            long nextCursor = 0;
            if (root.TryGetProperty("cursor", out var cEl2))
            {
                if (cEl2.ValueKind == JsonValueKind.Number)       nextCursor = cEl2.GetInt64();
                else if (cEl2.ValueKind == JsonValueKind.String)  long.TryParse(cEl2.GetString(), out nextCursor);
            }

            page++;

            if (!hasMore || nextCursor == 0 || nextCursor == cursor)
            {
                Console.WriteLine($"[COMMENTS DONE] videoId={videoId} total_pages={page}");
                break;
            }

            cursor = nextCursor;
            await Task.Delay(800, ct);
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private async Task<string?> SafeGetWithRetryAsync(
        string url,
        CancellationToken ct,
        string tag = "")
    {
        for (int attempt = 1; attempt <= TransientMaxRetries; attempt++)
        {
            var (body, statusCode) = await SafeGetStringInternalAsync(url, ct);

            if (statusCode >= 400)
            {
                Console.Error.WriteLine($"[{tag}] HTTP {statusCode} — hard error.");
                return null;
            }

            if (statusCode == 200 && !string.IsNullOrWhiteSpace(body))
                return body;

            string reason = statusCode == 202 ? "202 still processing"
                          : statusCode == 204 ? "204 no content"
                          : "200 empty body";

            if (attempt < TransientMaxRetries)
            {
                int waitMs = TransientRetryBaseMs * attempt;
                Console.WriteLine($"[{tag}] {reason} (attempt {attempt}/{TransientMaxRetries}), retrying in {waitMs / 1000}s...");
                await Task.Delay(waitMs, ct);
            }
            else
            {
                Console.WriteLine($"[{tag}] {reason} after {TransientMaxRetries} attempts — giving up.");
                return null;
            }
        }
        return null;
    }

    private async Task<(string? body, int statusCode)> SafeGetStringInternalAsync(
        string url,
        CancellationToken ct)
    {
        HttpResponseMessage resp;
        try { resp = await _http.GetAsync(url, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HTTP ERROR] {url}: {ex.Message}");
            return (null, 0);
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        return (body, (int)resp.StatusCode);
    }

    private async Task<string?> SafeGetStringAsync(string url, CancellationToken ct)
    {
        var (body, statusCode) = await SafeGetStringInternalAsync(url, ct);
        if (statusCode is >= 200 and < 300 && !string.IsNullOrWhiteSpace(body))
            return body;
        if (statusCode != 0)
            Console.Error.WriteLine($"[API ERROR] HTTP {statusCode} {url} | {(body ?? "")[..Math.Min(300, (body ?? "").Length)]}");
        return null;
    }

    private static string? TryGetFirstUrl(JsonElement parent, string propName)
    {
        if (parent.TryGetProperty(propName, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString();
        return null;
    }

    private static string? TryGetFirstJpegUrl(JsonElement parent, string propName)
    {
        if (!parent.TryGetProperty(propName, out var obj)) return null;
        if (!obj.TryGetProperty("url_list",  out var list)) return null;
        var urls = list.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList();
        return urls.LastOrDefault();
    }
}
