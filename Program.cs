using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TikTokEcoBelarus.Infrastructure;
using TikTokEcoBelarus.Pipeline;
using TikTokEcoBelarus.Services;

const string apiKey = "02e437b294msh2835a963405c6f2p1bc888jsn6ec318a971d0";

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(context.Configuration.GetConnectionString("Default")));

        services.AddHttpClient();
        services.AddSingleton<BelarusEcoScorer>();
        services.AddSingleton<TikTokApiClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient();
            return new TikTokApiClient(httpClient, apiKey);
        });
        services.AddSingleton<CollectionPipeline>();
        services.AddSingleton<CsvExportService>();
    })
    .Build();

using var scope = host.Services.CreateScope();
var provider = scope.ServiceProvider;

try
{
    var pipeline = provider.GetRequiredService<CollectionPipeline>();
    var csvExport = provider.GetRequiredService<CsvExportService>();

    Console.WriteLine("Запуск пайплайна...");

    var results = await pipeline.RunAsync(minBelarus: 0.3, minEco: 0.3);

    Console.WriteLine();
    Console.WriteLine($"Найдено видео: {results.Count}");

    if (results.Count == 0)
    {
        Console.WriteLine("Подходящих видео не найдено.");
    }
    else
    {
        var csvPath = await csvExport.ExportAsync(results);

        Console.WriteLine($"CSV сохранён: {csvPath}");
        Console.WriteLine();
        Console.WriteLine("Топ результатов:");

        foreach (var item in results.Take(10))
        {
            Console.WriteLine(
                $"- @{item.Item.Author.UniqueId} | " +
                $"score={item.TotalScore:F3} | " +
                $"belarus={item.BelarusScore:F3} | " +
                $"eco={item.EcoScore:F3} | " +
                $"{item.Item.Desc}"
            );
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine("Ошибка при выполнении пайплайна:");
    Console.WriteLine(ex.ToString());
}

Console.WriteLine();
Console.WriteLine("Нажми Enter для выхода...");
Console.ReadLine();