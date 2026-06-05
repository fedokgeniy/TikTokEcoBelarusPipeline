using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Configurations;

public class VideoSearchQueryLinkConfiguration : IEntityTypeConfiguration<VideoSearchQueryLink>
{
    public void Configure(EntityTypeBuilder<VideoSearchQueryLink> builder)
    {
        builder.HasKey(l => new { l.VideoId, l.SearchQueryId });

        builder.HasOne(l => l.Video)
            .WithMany(v => v.SearchQueryLinks)
            .HasForeignKey(l => l.VideoId);

        builder.HasOne(l => l.SearchQuery)
            .WithMany(q => q.VideoLinks)
            .HasForeignKey(l => l.SearchQueryId);
    }
}