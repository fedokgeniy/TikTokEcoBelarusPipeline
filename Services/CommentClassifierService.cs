using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Services;

/// <summary>
/// Классифицирует комментарии к видео с помощью Claude Haiku (Anthropic API).
///
/// Алгоритм:
///   1. Формирует батчи комментариев, каждый батч ≤ MaxBatchChars символов.
///   2. Отправляет батч в Haiku с системным промптом на экологическую тематику Беларуси.
///   3. Парсит ответ — массив JSON: [{"cid":"...","relevant":true,"tags":[...]},...]
///   4. Возвращает словарь cid → (isRelevant, tags).
/// </summary>
public class CommentClassifierService
{
    private readonly HttpClient _http;
    private readonly string     _apiKey;

    private const string ApiUrl        = "https://api.anthropic.com/v1/messages";
    private const string Model         = "claude-haiku-4-5";
    private const int    MaxBatchChars = 12_000;  // ~3k tokens — безопасный предел для Haiku
    private const int    MaxTokens     = 1024;

    private static readonly string SystemPrompt =
        "Ты — аналитик экологических данных. " +
        "Тебе дают список комментариев из TikTok к видео об экологии Беларуси. " +
        "Для каждого комментария определи: релевантен ли он экологической повестке Беларуси (загрязнение, " +
        "природа, экология, реки, леса, воздух, отходы, климат, протесты, экоактивизм и т.п.). " +
        "Верни ТОЛЬКО JSON-массив, без пояснений, без markdown. " +
        "Формат каждого элемента: {\"cid\":\"<id>\",\"relevant\":<true|false>,\"tags\":[\"tag1\",\"tag2\"]}. " +
        "Если комментарий нерелевантен — tags пустой массив. Теги на английском, строчными.";

    public CommentClassifierService(string anthropicApiKey)
    {
        _apiKey = anthropicApiKey;
        _http   = new HttpClient();
        _http.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    /// <summary>
    /// Классифицирует список комментариев батчами.
    /// Возвращает словарь: CommentId → (IsRelevant, Tags-через-запятую).
    /// </summary>
    public async Task<Dictionary<string, (bool IsRelevant, string? Tags)>> ClassifyAsync(
        IReadOnlyList<VideoComment> comments,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, (bool, string?)>();
        if (comments.Count == 0) return result;

        var batches = BuildBatches(comments);
        Console.WriteLine($"[CLASSIFIER] {comments.Count} comments → {batches.Count} batch(es)");

        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            var batchResult = await ClassifyBatchAsync(batch, ct);
            foreach (var kv in batchResult)
                result[kv.Key] = kv.Value;

            if (batches.Count > 1)
                await Task.Delay(500, ct); // rate-limit guard
        }

        return result;
    }

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    private static List<List<VideoComment>> BuildBatches(IReadOnlyList<VideoComment> comments)
    {
        var batches = new List<List<VideoComment>>();
        var current = new List<VideoComment>();
        int currentChars = 0;

        foreach (var c in comments)
        {
            // JSON entry approximation: {"cid":"<id>","text":"<text>"}
            int entryLen = c.CommentId.Length + c.Text.Length + 20;

            if (current.Count > 0 && currentChars + entryLen > MaxBatchChars)
            {
                batches.Add(current);
                current      = new List<VideoComment>();
                currentChars = 0;
            }

            current.Add(c);
            currentChars += entryLen;
        }

        if (current.Count > 0)
            batches.Add(current);

        return batches;
    }

    private async Task<Dictionary<string, (bool IsRelevant, string? Tags)>> ClassifyBatchAsync(
        List<VideoComment> batch,
        CancellationToken ct)
    {
        // Строим user-message: компактный JSON-массив комментариев
        var inputArray = batch.Select(c => new { cid = c.CommentId, text = c.Text }).ToList();
        var userMessage = JsonSerializer.Serialize(inputArray);

        var requestBody = new
        {
            model      = Model,
            max_tokens = MaxTokens,
            system     = SystemPrompt,
            messages   = new[]
            {
                new { role = "user", content = userMessage }
            }
        };

        var json    = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        Console.WriteLine($"[CLASSIFIER] Sending batch of {batch.Count} comments to Haiku...");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(ApiUrl, content, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CLASSIFIER HTTP ERROR] {ex.Message}");
            return new();
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"[CLASSIFIER] HTTP {(int)response.StatusCode}: {responseBody[..Math.Min(500, responseBody.Length)]}");
            return new();
        }

        return ParseHaikuResponse(responseBody);
    }

    private static Dictionary<string, (bool IsRelevant, string? Tags)> ParseHaikuResponse(string responseBody)
    {
        var result = new Dictionary<string, (bool, string?)>();

        try
        {
            var doc  = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Anthropic response: { "content": [{ "type": "text", "text": "[...]" }] }
            if (!root.TryGetProperty("content", out var contentArr)
             || contentArr.ValueKind != JsonValueKind.Array)
                return result;

            string? rawText = null;
            foreach (var block in contentArr.EnumerateArray())
            {
                if (block.TryGetProperty("type",  out var typeEl) && typeEl.GetString() == "text"
                 && block.TryGetProperty("text",  out var textEl))
                {
                    rawText = textEl.GetString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(rawText))
            {
                Console.Error.WriteLine("[CLASSIFIER PARSE] Empty text block in Haiku response.");
                return result;
            }

            // Найти первый '[' — модель иногда добавляет преамбулу
            int start = rawText.IndexOf('[');
            int end   = rawText.LastIndexOf(']');
            if (start < 0 || end < 0 || end <= start)
            {
                Console.Error.WriteLine($"[CLASSIFIER PARSE] No JSON array found in: {rawText[..Math.Min(300, rawText.Length)]}");
                return result;
            }

            var jsonArray = JsonDocument.Parse(rawText[start..(end + 1)]);

            foreach (var item in jsonArray.RootElement.EnumerateArray())
            {
                string? cid       = item.TryGetProperty("cid",      out var cidEl)  ? cidEl.GetString()  : null;
                bool    relevant  = item.TryGetProperty("relevant", out var relEl)  && relEl.GetBoolean();

                string? tags = null;
                if (item.TryGetProperty("tags", out var tagsEl)
                 && tagsEl.ValueKind == JsonValueKind.Array)
                {
                    var tagList = tagsEl.EnumerateArray()
                        .Select(t => t.GetString())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .ToList();
                    if (tagList.Count > 0)
                        tags = string.Join(",", tagList);
                }

                if (cid is not null)
                    result[cid] = (relevant, tags);
            }

            Console.WriteLine($"[CLASSIFIER PARSE] Parsed {result.Count} result(s) from Haiku.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CLASSIFIER PARSE ERROR] {ex.Message}");
        }

        return result;
    }
}
