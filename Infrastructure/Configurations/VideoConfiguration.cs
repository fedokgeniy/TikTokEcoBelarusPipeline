using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Configurations;

public class VideoConfiguration : IEntityTypeConfiguration<Video>
{
    public void Configure(EntityTypeBuilder<Video> builder)
    {
        builder.HasKey(v => v.VideoId);
        builder.Property(v => v.Hashtags).HasColumnType("text[]");
        builder.Property(v => v.ScoreBreakdown).HasColumnType("jsonb");
        builder.Property(v => v.BelarusScore).HasPrecision(5, 4);
        builder.Property(v => v.EcoScore).HasPrecision(5, 4);
    }
}