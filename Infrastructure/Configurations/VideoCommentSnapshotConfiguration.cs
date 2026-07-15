using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Configurations;

public class VideoCommentSnapshotConfiguration : IEntityTypeConfiguration<VideoCommentSnapshot>
{
    public void Configure(EntityTypeBuilder<VideoCommentSnapshot> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.VideoId)
            .IsRequired()
            .HasMaxLength(100);

        // Один снапшот на видео в одну секунду — достаточно для дедупликации
        builder.HasIndex(s => new { s.VideoId, s.SnapshotAt })
            .IsUnique();

        builder.HasOne(s => s.Video)
            .WithMany()
            .HasForeignKey(s => s.VideoId)
            .HasPrincipalKey(v => v.VideoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
