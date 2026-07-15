using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Configurations;

public class VideoCommentConfiguration : IEntityTypeConfiguration<VideoComment>
{
    public void Configure(EntityTypeBuilder<VideoComment> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.VideoId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.CommentId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(c => new { c.VideoId, c.CommentId })
            .IsUnique();

        builder.Property(c => c.Text)
            .HasMaxLength(2000);

        builder.Property(c => c.AuthorUniqueId)
            .HasMaxLength(100);

        builder.Property(c => c.IsRelevant)
            .IsRequired(false);

        builder.Property(c => c.Tags)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.HasOne(c => c.Video)
            .WithMany(v => v.Comments)
            .HasForeignKey(c => c.VideoId)
            .HasPrincipalKey(v => v.VideoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
