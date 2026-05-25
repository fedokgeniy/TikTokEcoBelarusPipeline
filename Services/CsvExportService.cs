using System.Globalization;
using System.Text;
using TikTokEcoBelarus.Models;

namespace TikTokEcoBelarus.Services;

public class CsvExportService
{
    public async Task<string> ExportAsync(List<ScoredVideo> videos, CancellationToken ct = default)
    {
        var outputDir = Path.Combine(AppContext.BaseDirectory, "output");
        Directory.CreateDirectory(outputDir);

        var path = Path.Combine(outputDir, $"tiktok_results_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");

        var lines = new List<string>
        {
            "VideoId,AuthorId,UniqueId,Nickname,TotalScore,BelarusScore,EcoScore,PlayCount,DiggCount,ShareCount,CommentCount,CreatedAt,Desc,VideoUrl,BelarusSignals,EcoSignals"
        };

        foreach (var v in videos)
        {
            ct.ThrowIfCancellationRequested();

            var i = v.Item;

            lines.Add(string.Join(",",
                CsvEscape(i.Id),
                CsvEscape(i.Author.Id),
                CsvEscape(i.Author.UniqueId),
                CsvEscape(i.Author.Nickname),
                v.TotalScore.ToString("F3", CultureInfo.InvariantCulture),
                v.BelarusScore.ToString("F3", CultureInfo.InvariantCulture),
                v.EcoScore.ToString("F3", CultureInfo.InvariantCulture),
                i.Stats.PlayCount.ToString(CultureInfo.InvariantCulture),
                i.Stats.DiggCount.ToString(CultureInfo.InvariantCulture),
                i.Stats.ShareCount.ToString(CultureInfo.InvariantCulture),
                i.Stats.CommentCount.ToString(CultureInfo.InvariantCulture),
                CsvEscape(i.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                CsvEscape(i.Desc),
                CsvEscape(v.VideoUrl),
                CsvEscape(string.Join("|", v.BelarusSignals)),
                CsvEscape(string.Join("|", v.EcoSignals))
            ));
        }

        await File.WriteAllLinesAsync(path, lines, new UTF8Encoding(true), ct);
        return path;
    }

    private static string CsvEscape(string? value)
    {
        value ??= string.Empty;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}