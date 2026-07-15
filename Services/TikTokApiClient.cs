using System.Runtime.CompilerServices;
using System.Text.Json;
using TikTokEcoBelarus.Models;

namespace TikTokEcoBelarus.Services;

public class TikTokApiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private const string BaseUrl = "https://tiktok-api23.p.rapidapi.com";

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

        var body = await SafeGetStringAsync(url, ct);
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

        var body = await SafeGetStringAsync(url, ct);
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
    //
    // tiktok-api23 may respond with HTTP 202 + {"message":"...processing"}
    // when the backend hasn't cached the result yet.
    // We retry up to MaxRetries times with increasing delay before giving up.
    //
    // Pagination: first page has no cursor; subsequent pages use
    //   &maxCursor=<value> from the previous response.
    // Stop when hasMore is falsy or no cursor is returned.
    // ---------------------------------------------------------------
    public async Task<List<TikTokFollowingUser>> GetUserFollowingsAsync(
        string secUid,
        int maxCount = 200,
        CancellationToken ct = default)
    {
        var result   = new List<TikTokFollowingUser>();
        long? cursor = null;
        int pageSize = Math.Min(50, maxCount);
        int page     = 0;
        const int MaxRetries   = 4;
        const int RetryDelayMs = 5000; // 5 sec between retries on 202

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

            // --- Retry loop for HTTP 202 ---
            string? body = null;
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                body = await SafeGetStringAsync(url, ct, allow202: true);
                if (body is null) break; // hard error

                // Check if it's the "still processing" response
                if (body.Contains("still processing", StringComparison.OrdinalIgnoreCase)
                 || body.Contains("Request accepted",  StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[FOLLOWINGS] 202 still processing (attempt {attempt}/{MaxRetries}), waiting {RetryDelayMs}ms...");
                    if (attempt < MaxRetries)
                        await Task.Delay(RetryDelayMs * attempt, ct); // exponential: 5s, 10s, 15s
                    else
                    {
                        Console.WriteLine("[FOLLOWINGS] Max retries reached on 202, giving up.");
                        body = null;
                    }
                    continue;
                }

                break; // got a real response
            }

            if (body is null) break;

            Console.WriteLine($"[FOLLOWINGS RAW] {body[..Math.Min(300, body.Length)]}");

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

                    result.Add(new TikTokFollowingUser
                    {
                        UniqueId = uniqueId,
                        Nickname = nickname,
                        SecUid   = secUidUser
                    });
                    added++;
                }

                Console.WriteLine($"[FOLLOWINGS] Page {page + 1}: added={added}, total={result.Count}");
                page++;

                if (added == 0) break;

                // hasMore
                bool hasMore = false;
                if (root.TryGetProperty("hasMore", out var hmEl))
                {
                    if (hmEl.ValueKind == JsonValueKind.True)   hasMore = true;
                    if (hmEl.ValueKind == JsonValueKind.False)  hasMore = false;
                    if (hmEl.ValueKind == JsonValueKind.Number) hasMore = hmEl.GetInt32() != 0;
                }

                // next cursor
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

                Console.WriteLine($"[FOLLOWINGS] hasMore={hasMore}, nextCursor={nextCursor}");

                if (!hasMore || nextCursor is null) break;

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
    // ---------------------------------------------------------------
    public async IAsyncEnumerable<TikTokItem> SearchVideosAsync(
        string keyword,
        int maxPages = 100,
        int delayMs  = 1500,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int offset  = 0;
        int page    = 0;
        var seenIds = new HashSet<string>();

        while (page < maxPages)
        {
            ct.ThrowIfCancellationRequested();

            var url = $"{BaseUrl}/api/search/video" +
                      $"?keyword={Uri.EscapeDataString(keyword)}" +
                      $"&offset={offset}";

            Console.WriteLine($"[SEARCH] Page {page + 1}/{maxPages} offset={offset}");

            var body = await SafeGetStringAsync(url, ct);
            if (body is null) break;

            TikTokSearchResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<TikTokSearchResponse>(body);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SEARCH PARSE ERROR] {ex.Message}");
                Console.Error.WriteLine($"Body: {body[..Math.Min(500, body.Length)]}");
                break;
            }

            if (response?.ItemList is null || response.ItemList.Count == 0)
            {
                Console.WriteLine($"[SEARCH END] keyword=\"{keyword}\" offset={offset}: empty page.");
                break;
            }

            var newItems = response.ItemList
                .Where(item => seenIds.Add(item.Id))
                .ToList();

            Console.WriteLine(
                $"[SEARCH OK] Page {page + 1}: {response.ItemList.Count} items " +
                $"({newItems.Count} new), cursor={response.Cursor}, has_more={response.HasMore} [ignored]");

            if (newItems.Count == 0)
            {
                Console.WriteLine($"[SEARCH END] All items duplicate — stopping.");
                break;
            }

            foreach (var item in newItems)
                yield return item;

            offset += response.ItemList.Count;
            page++;

            if (page < maxPages)
                await Task.Delay(delayMs, ct);
        }
    }

    // ---------------------------------------------------------------
    // GET /api/comment/list?videoId=...&count=N
    // ---------------------------------------------------------------
    public async IAsyncEnumerable<TikTokComment> GetVideoCommentsAsync(
        string videoId,
        int count = 20,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/api/comment/list?videoId={Uri.EscapeDataString(videoId)}&count={count}";
        Console.WriteLine($"[COMMENTS] GET {url}");

        var body = await SafeGetStringAsync(url, ct);
        if (body is null) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[COMMENTS PARSE ERROR] videoId={videoId}: {ex.Message}");
            Console.Error.WriteLine($"Body: {body[..Math.Min(500, body.Length)]}");
            yield break;
        }

        var root = doc.RootElement;
        if (!root.TryGetProperty("comments", out var commentsArr)
         || commentsArr.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine($"[COMMENTS EMPTY] videoId={videoId}");
            yield break;
        }

        foreach (var c in commentsArr.EnumerateArray())
        {
            string? commentId  = c.TryGetProperty("cid",         out var cidEl)  ? cidEl.GetString()  : null;
            string? text       = c.TryGetProperty("text",        out var textEl) ? textEl.GetString() : null;
            long    likeCount  = c.TryGetProperty("digg_count",  out var lkEl)   ? lkEl.GetInt64()   : 0;
            long    createTime = c.TryGetProperty("create_time", out var ctEl)   ? ctEl.GetInt64()   : 0;

            string? authorId = null;
            if (c.TryGetProperty("user", out var userEl))
                authorId = userEl.TryGetProperty("unique_id", out var uidEl) ? uidEl.GetString() : null;

            if (commentId is null || text is null) continue;

            yield return new TikTokComment
            {
                CommentId      = commentId,
                Text           = text,
                AuthorUniqueId = authorId ?? string.Empty,
                LikeCount      = likeCount,
                CreatedAt      = DateTimeOffset.FromUnixTimeSeconds(createTime)
            };
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <param name="allow202">When true, 202 responses are returned as body text
    /// instead of being treated as errors, so callers can handle retry logic.</param>
    private async Task<string?> SafeGetStringAsync(
        string url,
        CancellationToken ct,
        bool allow202 = false)
    {
        HttpResponseMessage resp;
        try { resp = await _http.GetAsync(url, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HTTP ERROR] {url}: {ex.Message}");
            return null;
        }

        var body = await resp.Content.ReadAsStringAsync(ct);

        // 202 Accepted = "still processing" — let caller decide
        if ((int)resp.StatusCode == 202 && allow202)
        {
            Console.WriteLine($"[HTTP 202] {url}");
            return body;
        }

        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine(
                $"[API ERROR] {(int)resp.StatusCode} {url} | {body[..Math.Min(300, body.Length)]}");
            return null;
        }
        return body;
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
