using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure.Configurations;

namespace TikTokEcoBelarus.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SearchQuery> SearchQueries => Set<SearchQuery>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<VideoSearchQueryLink> VideoSearchQueryLinks => Set<VideoSearchQueryLink>();
    public DbSet<ScoringRule> ScoringRules => Set<ScoringRule>();
    public DbSet<ScoringRuleThreshold> ScoringRuleThresholds => Set<ScoringRuleThreshold>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new VideoConfiguration());
        modelBuilder.ApplyConfiguration(new VideoSearchQueryLinkConfiguration());
        modelBuilder.ApplyConfiguration(new ScoringRuleConfiguration());
    }
}