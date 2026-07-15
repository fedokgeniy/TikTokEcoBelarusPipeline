using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure;

namespace TikTokEcoBelarus.Services;

public class CommentClassifierService(
    IDbContextFactory<AppDbContext> dbFactory,
    IHttpClientFactory httpClientFactory)
{
    private const string ApiUrl        = "https://api.anthropic.com/v1/messages";
    private const int    MaxBatchChars = 6_000;
    private const int    MaxTokens     = 8192;

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
    public async Task<Dictionary<string, ClassifyResult>> ClassifyAsync(
        IReadOnlyList<VideoComment> comments,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, ClassifyResult>();
        if (comments.Count == 0) return result;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var keys           = new[] { KeyApiKey, KeyModel, KeyPrompt };
        var settings       = await db.AppSettings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var apiKey       = settings.TryGetValue(KeyApiKey, out var k) ? k.Trim() : "";
        var model        = settings.TryGetValue(KeyModel,  out var m) ? m.Trim() : DefaultModel;
        var systemPrompt = settings.TryGetValue(KeyPrompt, out var p) ? p.Trim() : DefaultSystemPrompt;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("[CLASSIFIER] Anthropic API key is empty. Установите ключ в разделе Settings.");
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
    private static List<List<VideoComment>> BuildBatches(IReadOnlyList<VideoComment> comments)
    {
        var batches = new List<List<VideoComment>>();
        var current = new List<VideoComment>();
        int chars   = 0;

        foreach (var c in comments)
        {
            int len = c.CommentId.Length + c.Text.Length + 20;
            if (current.Count > 0 && chars + len > MaxBatchChars)
            {
                batches.Add(current);
                current = [];
                chars   = 0;
            }
            current.Add(c);
            chars += len;
        }
        if (current.Count > 0) batches.Add(current);
        return batches;
    }

    private async Task<Dictionary<string, ClassifyResult>> ClassifyBatchAsync(
        List<VideoComment> batch, string apiKey, string model, string systemPrompt, CancellationToken ct)
    {
        var inputArray  = batch.Select(c => new { cid = c.CommentId, text = c.Text });
        var userMessage = JsonSerializer.Serialize(inputArray);
        var requestBody = new
        {
            model,
            max_tokens = MaxTokens,
            system     = systemPrompt,
            messages   = new[] { new { role = "user", content = userMessage } }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Add("x-api-key",         apiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        Console.WriteLine($"[CLASSIFIER] Sending batch of {batch.Count} comments to {model}...");

        HttpResponseMessage response;
        try   { response = await http.PostAsync(ApiUrl, content, ct); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CLASSIFIER HTTP ERROR] {ex.Message}");
            return new();
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine(
                $"[CLASSIFIER] HTTP {(int)response.StatusCode}: {body[..Math.Min(500, body.Length)]}");
            return new();
        }

        return ParseResponse(body);
    }

    private static Dictionary<string, ClassifyResult> ParseResponse(string body)
    {
        var result = new Dictionary<string, ClassifyResult>();
        try
        {
            var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("content", out var arr)
             || arr.ValueKind != JsonValueKind.Array) return result;

            string? rawText = null;
            foreach (var block in arr.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                 && block.TryGetProperty("text", out var tx))
                { rawText = tx.GetString(); break; }
            }

            if (string.IsNullOrWhiteSpace(rawText)) return result;

            int start = rawText.IndexOf('[');
            int end   = rawText.LastIndexOf(']');
            if (start < 0 || end <= start) return result;

            foreach (var item in JsonDocument.Parse(rawText[start..(end + 1)]).RootElement.EnumerateArray())
            {
                string? cid      = item.TryGetProperty("cid",      out var cidEl)  ? cidEl.GetString()       : null;
                string? category = item.TryGetProperty("category", out var catEl)  ? catEl.GetString()       : null;
                bool shouldReply = item.TryGetProperty("shouldReply", out var srEl) && srEl.GetBoolean();

                int score = 0;
                if (item.TryGetProperty("score", out var scEl) && scEl.ValueKind == JsonValueKind.Number)
                    score = scEl.GetInt32();

                bool relevant;
                if (item.TryGetProperty("score", out var s2) && s2.ValueKind == JsonValueKind.Number)
                    relevant = s2.GetInt32() >= 70;
                else
                    relevant = item.TryGetProperty("relevant", out var relEl) && relEl.GetBoolean();

                string? tags = null;
                if (item.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
                {
                    var list = tagsEl.EnumerateArray()
                        .Select(t => t.GetString())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .ToList();
                    if (list.Count > 0) tags = string.Join(",", list);
                }

                if (cid is not null)
                    result[cid] = new ClassifyResult(relevant, score, category, shouldReply, tags);
            }

            Console.WriteLine($"[CLASSIFIER PARSE] Parsed {result.Count} result(s).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CLASSIFIER PARSE ERROR] {ex.Message}");
            Console.Error.WriteLine($"[CLASSIFIER PARSE ERROR] rawText snippet: {body[..Math.Min(300, body.Length)]}");
        }
        return result;
    }
}

/// <summary>Full result from Claude for one comment.</summary>
public record ClassifyResult(
    bool    IsRelevant,
    int     Score,
    string? Category,
    bool    ShouldReply,
    string? Tags);
