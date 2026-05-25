namespace TikTokEcoBelarus.Models;

public class ScoredVideo
{
    public TikTokItem Item { get; set; } = null!;

    // Scores: 0.0 – 1.0
    public double BelarusScore { get; set; }
    public double EcoScore { get; set; }

    // Итоговый score = весовая сумма
    public double TotalScore => (BelarusScore * 0.45) + (EcoScore * 0.55);

    // Что именно сработало (для отладки)
    public List<string> BelarusSignals { get; set; } = [];
    public List<string> EcoSignals { get; set; } = [];

    // Прошёл ли минимальный порог
    public bool PassesThreshold(double minBelarus = 0.3, double minEco = 0.3)
        => BelarusScore >= minBelarus && EcoScore >= minEco;

    public string VideoUrl =>
        $"https://www.tiktok.com/@{Item.Author.UniqueId}/video/{Item.Id}";
}