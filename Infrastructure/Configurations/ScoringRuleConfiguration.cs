using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TikTokEcoBelarus.Domain.Entities;

namespace TikTokEcoBelarus.Infrastructure.Configurations;

public class ScoringRuleConfiguration : IEntityTypeConfiguration<ScoringRule>
{
    public void Configure(EntityTypeBuilder<ScoringRule> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Weight).HasPrecision(5, 4);
        builder.HasIndex(r => r.Category);
        builder.HasIndex(r => r.IsActive);
    }
}