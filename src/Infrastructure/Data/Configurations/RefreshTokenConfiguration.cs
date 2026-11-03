using Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.UserId)
            .IsRequired();

        builder.Property(r => r.TokenHash)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(r => r.TokenHash);

        builder.Property(r => r.ExpiresAt)
            .IsRequired();

        builder.Property(r => r.IsRevoked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.ReplacedByToken)
            .HasMaxLength(256);

        builder.Property(r => r.CreatedAt)
            .IsRequired();
    }
}
