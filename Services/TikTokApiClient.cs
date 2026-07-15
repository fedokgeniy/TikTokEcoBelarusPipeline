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

            int    videoCount   = statsNode.GetProperty("videoCount").GetInt32();
            string? userId      = userNode.TryGetProperty("id",       out var idEl) ? idEl.GetString()  : null;
            string? nickname    = userNode.TryGetProperty("nickname",  out var nnEl) ? nnEl.GetString()  : null;
            string? avatarThumb = TryGetFirstUrl(userNode, "avatarThumb");
            string  profileUrl  = $"https://www.tiktok.com/@{uniqueId}";

            Console.WriteLine($"[USER INFO] @{uniqueId} uid={userId} videos={videoCount}");

            return new TikTokUserInfo
            {
                UniqueId    = uniqueId,
                UserId      = userId,
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

            string? uniqueId    = userNode.TryGetProperty("unique_id", out var uidEl) ? uidEl.GetString() : null;
            string? nickname    = userNode.TryGetProperty("nickname",  out var nnEl)  ? nnEl.GetString()  : null;
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
    // GET /api/search/video?keyword=...
    //
    // ВАЖНО: tiktok-api23 ВСЕГДА возвращает has_more=0 — это баг API.
    // Реальный признак конца: пустой ItemList или повторяющиеся items.
    // Пагинация по offset: 0, +count_items, +count_items, ...
    // Останавливаемся только если пришёл пустой массив или HTTP-ошибка.
    // ---------------------------------------------------------------
    public async IAsyncEnumerable<TikTokItem> SearchVideosAsync(
        string keyword,
        int maxPages = 100,
        int delayMs  = 1500,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int offset = 0;
        int page   = 0;
        var seenIds = new HashSet<string>();

        while (page < maxPages)
        {
            ct.ThrowIfCancellationRequested();

            var url = $"{BaseUrl}/api/search/video" +
                      $"?keyword={Uri.EscapeDataString(keyword)}" +
                      $"&offset={offset}";

            Console.WriteLine($"[SEARCH] Page {page + 1}/{maxPages} offset={offset} GET {url}");

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
                Console.WriteLine($"[SEARCH END] keyword=\"{keyword}\" offset={offset}: пустая страница — контент закончился.");
                break;
            }

            // Проверяем дубли: если все id уже видели — API зациклился
            var newItems = response.ItemList
                .Where(item => seenIds.Add(item.Id))
                .ToList();

            Console.WriteLine(
                $"[SEARCH OK] Page {page + 1}: {response.ItemList.Count} items " +
                $"({newItems.Count} новых), cursor={response.Cursor}, has_more={response.HasMore} [игнорируется]");

            if (newItems.Count == 0)
            {
                Console.WriteLine($"[SEARCH END] keyword=\"{keyword}\": все items повторяются — API вернул дубли, стоп.");
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
            Console.WriteLine($"[COMMENTS EMPTY] videoId={videoId} raw={body[..Math.Min(200, body.Length)]}");
            yield break;
        }

        foreach (var c in commentsArr.EnumerateArray())
        {
            string? commentId  = c.TryGetProperty("cid",        out var cidEl)  ? cidEl.GetString()   : null;
            string? text       = c.TryGetProperty("text",       out var textEl) ? textEl.GetString()  : null;
            long    likeCount  = c.TryGetProperty("digg_count", out var lkEl)   ? lkEl.GetInt64()    : 0;
            long    createTime = c.TryGetProperty("create_time",out var ctEl)   ? ctEl.GetInt64()    : 0;

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
    private async Task<string?> SafeGetStringAsync(string url, CancellationToken ct)
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
        if (!obj.TryGetProperty("url_list", out var list)) return null;
        var urls = list.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList();
        return urls.LastOrDefault();
    }
}
