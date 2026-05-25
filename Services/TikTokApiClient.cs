using System.Net.Http.Json;
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

    public async IAsyncEnumerable<TikTokItem> SearchVideosAsync(
        string keyword,
        int maxPages = 5,
        int delayMs = 1500,
        CancellationToken ct = default)
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

            Console.WriteLine($"[DEBUG] GET {url}");

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _http.GetAsync(url, ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HTTP EXCEPTION] {ex.Message}");
                break;
            }

            var body = await httpResponse.Content.ReadAsStringAsync(ct);

            if (!httpResponse.IsSuccessStatusCode)
            {
                Console.Error.WriteLine(
                    $"[API ERROR] {keyword} cursor={cursor}: " +
                    $"{(int)httpResponse.StatusCode} | {body[..Math.Min(300, body.Length)]}");
                break;
            }

            TikTokSearchResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<TikTokSearchResponse>(body);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PARSE ERROR] {ex.Message}");
                Console.Error.WriteLine($"Body: {body[..Math.Min(500, body.Length)]}");
                break;
            }

            if (response?.ItemList is null || response.ItemList.Count == 0)
            {
                Console.WriteLine($"[EMPTY] No items for \"{keyword}\" cursor={cursor}");
                break;
            }

            Console.WriteLine($"[OK] Got {response.ItemList.Count} items, has_more={response.HasMore}");

            foreach (var item in response.ItemList)
                yield return item;

            if (!response.HasMoreItems)
                break;

            cursor = response.Cursor;
            // search_id берём из extra если есть, иначе оставляем текущий
            searchId = response.Extra?.SearchRequestId is { Length: > 0 } sid
                       ? sid
                       : searchId;

            page++;
            await Task.Delay(delayMs, ct);
        }
    }
}