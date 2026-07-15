using Microsoft.EntityFrameworkCore;
using TikTokEcoBelarus.Domain.Entities;
using TikTokEcoBelarus.Infrastructure.Configurations;

namespace TikTokEcoBelarus.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SearchQuery>           SearchQueries          => Set<SearchQuery>();
    public DbSet<Video>                 Videos                 => Set<Video>();
    public DbSet<VideoSearchQueryLink>  VideoSearchQueryLinks  => Set<VideoSearchQueryLink>();
    public DbSet<ScoringRule>           ScoringRules           => Set<ScoringRule>();
    public DbSet<ScoringRuleThreshold>  ScoringRuleThresholds  => Set<ScoringRuleThreshold>();
    public DbSet<AppSetting>            AppSettings            => Set<AppSetting>();
    public DbSet<TrackedChannel>        TrackedChannels        => Set<TrackedChannel>();
    public DbSet<TrackedChannelVideo>   TrackedChannelVideos   => Set<TrackedChannelVideo>();
    public DbSet<VideoComment>          VideoComments          => Set<VideoComment>();
    public DbSet<VideoCommentSnapshot>  VideoCommentSnapshots  => Set<VideoCommentSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new VideoConfiguration());
        modelBuilder.ApplyConfiguration(new VideoSearchQueryLinkConfiguration());
        modelBuilder.ApplyConfiguration(new ScoringRuleConfiguration());
        modelBuilder.ApplyConfiguration(new TrackedChannelConfiguration());
        modelBuilder.ApplyConfiguration(new TrackedChannelVideoConfiguration());
        modelBuilder.ApplyConfiguration(new VideoCommentConfiguration());
        modelBuilder.ApplyConfiguration(new VideoCommentSnapshotConfiguration());

        modelBuilder.Entity<AppSetting>(b =>
        {
            b.HasKey(s => s.Key);
            b.Property(s => s.Key).HasMaxLength(100);
            b.Property(s => s.Value).HasMaxLength(500);
        });
    }
}
