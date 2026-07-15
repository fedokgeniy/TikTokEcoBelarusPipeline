using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure;

namespace TikTokEcoBelarus.Services;

/// <summary>
/// Классифицирует комментарии через Anthropic Claude.
/// Ключ, модель и системный промт читаются из БД перед каждым запуском.
/// </summary>
public class CommentClassifierService(
    IDbContextFactory<AppDbContext> dbFactory,
    IHttpClientFactory httpClientFactory)
{
    private const string ApiUrl        = "https://api.anthropic.com/v1/messages";
    private const int    MaxBatchChars = 12_000;
    private const int    MaxTokens     = 1024;

    // AppSettings keys — must match Settings.razor
    private const string KeyApiKey = "anthropic:apiKey";
    private const string KeyModel  = "anthropic:model";
    private const string KeyPrompt = "anthropic:systemPrompt";

    private const string DefaultModel = "claude-haiku-4-5";

    private const string DefaultSystemPrompt =
        "Ты — классификатор комментариев для экологической горячей линии «Зелёный телефон» (Беларусь). " +
        "Для КАЖДОГО комментария верни JSON-объект: " +
        "{\"cid\":\"<id>\",\"score\":<0-100>,\"category\":\"<категория>\",\"shouldReply\":<true|false>,\"tags\":[...]}. " +
        "Верни ТОЛЬКО JSON-массив, без markdown, без преамбулы.";

    // ---------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------

    public async Task<Dictionary<string, (bool IsRelevant, string? Tags)>> ClassifyAsync(
        IReadOnlyList<VideoComment> comments,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, (bool, string?)>();
        if (comments.Count == 0) return result;

        // --- Read settings from DB at runtime ---
        await using var db   = await dbFactory.CreateDbContextAsync(ct);
        var keys             = new[] { KeyApiKey, KeyModel, KeyPrompt };
        var settings         = await db.AppSettings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var apiKey       = settings.TryGetValue(KeyApiKey, out var k) ? k.Trim() : "";
        var model        = settings.TryGetValue(KeyModel,  out var m) ? m.Trim() : DefaultModel;
        var systemPrompt = settings.TryGetValue(KeyPrompt, out var p) ? p.Trim() : DefaultSystemPrompt;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine(
                "[CLASSIFIER] Anthropic API key is empty. " +
                "Установите ключ в разделе Настройки (вкладка Settings).");
            return result;
        }

        var batches = BuildBatches(comments);
        Console.WriteLine($"[CLASSIFIER] {comments.Count} comments → {batches.Count} batch(es), model={model}");

        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            var batchResult = await ClassifyBatchAsync(batch, apiKey, model, systemPrompt, ct);
            foreach (var kv in batchResult)
                result[kv.Key] = kv.Value;

            if (batches.Count > 1)
                await Task.Delay(500, ct);
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
            int entryLen = c.CommentId.Length + c.Text.Length + 20;

            if (current.Count > 0 && currentChars + entryLen > MaxBatchChars)
            {
                batches.Add(current);
                current      = [];
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
        string apiKey,
        string model,
        string systemPrompt,
        CancellationToken ct)
    {
        var inputArray  = batch.Select(c => new { cid = c.CommentId, text = c.Text }).ToList();
        var userMessage = JsonSerializer.Serialize(inputArray);

        var requestBody = new
        {
            model,
            max_tokens = MaxTokens,
            system     = systemPrompt,
            messages   = new[] { new { role = "user", content = userMessage } }
        };

        var json    = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Build a fresh HttpClient per batch so the API key is always current
        using var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Add("x-api-key",          apiKey);
        http.DefaultRequestHeaders.Add("anthropic-version",  "2023-06-01");

        Console.WriteLine($"[CLASSIFIER] Sending batch of {batch.Count} comments to {model}...");

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(ApiUrl, content, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CLASSIFIER HTTP ERROR] {ex.Message}");
            return new();
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine(
                $"[CLASSIFIER] HTTP {(int)response.StatusCode}: " +
                responseBody[..Math.Min(500, responseBody.Length)]);
            return new();
        }

        return ParseResponse(responseBody);
    }

    private static Dictionary<string, (bool IsRelevant, string? Tags)> ParseResponse(string responseBody)
    {
        var result = new Dictionary<string, (bool, string?)>();
        try
        {
            var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("content", out var contentArr)
             || contentArr.ValueKind != JsonValueKind.Array)
                return result;

            string? rawText = null;
            foreach (var block in contentArr.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "text"
                 && block.TryGetProperty("text", out var textEl))
                { rawText = textEl.GetString(); break; }
            }

            if (string.IsNullOrWhiteSpace(rawText))
            {
                Console.Error.WriteLine("[CLASSIFIER PARSE] Empty text block.");
                return result;
            }

            int start = rawText.IndexOf('[');
            int end   = rawText.LastIndexOf(']');
            if (start < 0 || end <= start)
            {
                Console.Error.WriteLine($"[CLASSIFIER PARSE] No JSON array in: {rawText[..Math.Min(300, rawText.Length)]}");
                return result;
            }

            foreach (var item in JsonDocument.Parse(rawText[start..(end + 1)]).RootElement.EnumerateArray())
            {
                string? cid  = item.TryGetProperty("cid",      out var cidEl)  ? cidEl.GetString()   : null;
                // Support both old schema (relevant) and new schema (score >= 70)
                bool relevant;
                if (item.TryGetProperty("score", out var scoreEl) && scoreEl.ValueKind == JsonValueKind.Number)
                    relevant = scoreEl.GetInt32() >= 70;
                else
                    relevant = item.TryGetProperty("relevant", out var relEl) && relEl.GetBoolean();

                string? tags = null;
                if (item.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
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

            Console.WriteLine($"[CLASSIFIER PARSE] Parsed {result.Count} result(s).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CLASSIFIER PARSE ERROR] {ex.Message}");
        }
        return result;
    }
}
