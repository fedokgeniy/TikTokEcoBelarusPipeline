using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Infrastructure;
using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Pipeline;
using TikTokEcoBelarus.Services;

const string apiKey = "02e437b294msh2835a963405c6f2p1bc888jsn6ec318a971d0";

var builder = WebApplication.CreateBuilder(args);

// ── Blazor Server ──────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── БД ────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ── Кэш и HTTP ────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

// ── Репозитории и сервисы ──────────────────────────────────────
builder.Services.AddScoped<IScoringRuleRepository, ScoringRuleRepository>();
builder.Services.AddScoped<ISearchQueryRepository, SearchQueryRepository>();
builder.Services.AddScoped<BelarusEcoScorer>();
builder.Services.AddScoped<CollectionPipeline>();
builder.Services.AddScoped<CsvExportService>();

builder.Services.AddSingleton<TikTokApiClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = factory.CreateClient();
    return new TikTokApiClient(httpClient, apiKey);
});

var app = builder.Build();

// ── Seed ──────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.SeedAsync(db);
}

// ── Middleware ─────────────────────────────────────────────────
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<TikTokEcoBelarus.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();