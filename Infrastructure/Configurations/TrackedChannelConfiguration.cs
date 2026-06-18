using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Configurations;

public class TrackedChannelConfiguration : IEntityTypeConfiguration<TrackedChannel>
{
    public void Configure(EntityTypeBuilder<TrackedChannel> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.UniqueId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(c => c.UniqueId)
            .IsUnique();

        builder.Property(c => c.DisplayName)
            .HasMaxLength(200);

        builder.Property(c => c.AvatarUrl)
            .HasMaxLength(500);

        builder.HasMany(c => c.Videos)
            .WithOne(v => v.Channel)
            .HasForeignKey(v => v.TrackedChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TrackedChannelVideoConfiguration : IEntityTypeConfiguration<TrackedChannelVideo>
{
    public void Configure(EntityTypeBuilder<TrackedChannelVideo> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.VideoId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(v => new { v.TrackedChannelId, v.VideoId })
            .IsUnique();

        builder.Property(v => v.Description)
            .HasMaxLength(1000);
    }
}
