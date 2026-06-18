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
    // Возвращает videoCount + uid + share_url.
    // Используется: при добавлении канала и при каждой проверке videoCount.
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

            int    videoCount  = statsNode.GetProperty("videoCount").GetInt32();
            string? userId     = userNode.TryGetProperty("id",        out var idEl)  ? idEl.GetString()  : null;
            string? nickname   = userNode.TryGetProperty("nickname",   out var nnEl)  ? nnEl.GetString()  : null;
            string? avatarThumb = TryGetFirstUrl(userNode, "avatarThumb");

            // share_url живёт в userInfo.user — нет, строим сами по uniqueId
            string  profileUrl = $"https://www.tiktok.com/@{uniqueId}";

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
    // Возвращает nickname, avatar, share_url, unique_id.
    // НЕ содержит videoCount — используй GetUserInfoAsync для него.
    // Используется: для обогащения данных канала (разовое при добавлении).
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

            string? uniqueId   = userNode.TryGetProperty("unique_id",  out var uidEl)  ? uidEl.GetString()  : null;
            string? nickname   = userNode.TryGetProperty("nickname",   out var nnEl)   ? nnEl.GetString()   : null;
            string? avatarThumb = TryGetFirstJpegUrl(userNode, "avatar_thumb");

            // share_info.share_url — прямая ссылка на профиль
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
                VideoCount  = 0  // не доступен в этом endpoint
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
    // GET /api/search/video?keyword=... (paginated)
    // ---------------------------------------------------------------
    public async IAsyncEnumerable<TikTokItem> SearchVideosAsync(
        string keyword,
        int maxPages = 5,
        int delayMs = 1500,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        long cursor = 0;
        string searchId = "0";
        int page = 0;

        while (page < maxPages)
        {
            var url = $"{BaseUrl}/api/search/video" +
                      $"?keyword={Uri.EscapeDataString(keyword)}" +
                      $"&cursor={cursor}" +
                      $"&search_id={searchId}";

            Console.WriteLine($"[SEARCH] GET {url}");

            var body = await SafeGetStringAsync(url, ct);
            if (body is null) break;

            Models.TikTokSearchResponse? response;
            try
            {
                response = System.Text.Json.JsonSerializer.Deserialize<Models.TikTokSearchResponse>(body);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SEARCH PARSE ERROR] {ex.Message}");
                break;
            }

            if (response?.ItemList is null || response.ItemList.Count == 0)
            {
                Console.WriteLine($"[SEARCH EMPTY] keyword=\"{keyword}\" cursor={cursor}");
                break;
            }

            Console.WriteLine($"[SEARCH OK] {response.ItemList.Count} items, has_more={response.HasMore}");

            foreach (var item in response.ItemList)
                yield return item;

            if (!response.HasMoreItems) break;

            cursor   = response.Cursor;
            searchId = response.Extra?.SearchRequestId is { Length: > 0 } sid ? sid : searchId;
            page++;
            await Task.Delay(delayMs, ct);
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------
    private async Task<string?> SafeGetStringAsync(string url, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try { resp = await _http.GetAsync(url, ct); }
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

    /// <summary>Для /api/user/info — avatarThumb это строка (jpeg URL).</summary>
    private static string? TryGetFirstUrl(JsonElement parent, string propName)
    {
        if (parent.TryGetProperty(propName, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString();
        return null;
    }

    /// <summary>Для /api/user/info-by-id — avatar_thumb это объект с url_list[].</summary>
    private static string? TryGetFirstJpegUrl(JsonElement parent, string propName)
    {
        if (!parent.TryGetProperty(propName, out var obj)) return null;
        if (!obj.TryGetProperty("url_list", out var list)) return null;
        // Берём последний URL в списке — обычно jpeg (а не webp)
        var urls = list.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList();
        return urls.LastOrDefault();
    }
}
