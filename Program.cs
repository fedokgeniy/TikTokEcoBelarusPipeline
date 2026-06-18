using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Infrastructure;
using TikTokEcoBelarus.Infrastructure.Repositories;
using TikTokEcoBelarus.Pipeline;
using TikTokEcoBelarus.Services;

const string apiKey = "02e437b294msh2835a963405c6f2p1bc888jsn6ec318a971d0";

var builder = WebApplication.CreateBuilder(args);

// ── Blazor Server ─────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── БД ──────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Default");

// AddDbContextFactory с явным Scoped — без конфликта lifetime
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString), ServiceLifetime.Scoped);

// ── Кэш и HTTP ────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

// ── Репозитории и сервисы ──────────────────────────────────
builder.Services.AddSingleton<PipelineOrchestrator>();
builder.Services.AddScoped<IScoringRuleRepository, ScoringRuleRepository>();
builder.Services.AddScoped<ISearchQueryRepository, SearchQueryRepository>();
builder.Services.AddScoped<ITrackedChannelRepository, TrackedChannelRepository>();
builder.Services.AddScoped<BelarusEcoScorer>();
builder.Services.AddScoped<CollectionPipeline>();
builder.Services.AddScoped<ChannelMonitorPipeline>();
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
