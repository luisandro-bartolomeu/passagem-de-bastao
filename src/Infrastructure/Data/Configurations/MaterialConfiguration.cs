using Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("materials");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.SubjectId)
            .IsRequired();

        builder.Property(m => m.UploadedByUserId)
            .IsRequired();

        builder.Property(m => m.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(m => m.Description)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(m => m.MaterialType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.FileStorageKey)
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(m => m.FileStorageKey)
            .IsUnique();

        builder.Property(m => m.OriginalFileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(m => m.ContentType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.FileSizeBytes)
            .IsRequired();

        builder.Property(m => m.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(m => m.Status);

        builder.Property(m => m.RejectionReason)
            .HasMaxLength(1000);

        builder.Property(m => m.PeerHelperName)
            .HasMaxLength(150);

        builder.Property(m => m.PeerHelperContact)
            .HasMaxLength(150);

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.Property(m => m.UpdatedAt);

        builder.HasOne(m => m.Subject)
            .WithMany(s => s.Materials)
            .HasForeignKey(m => m.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.UploadedByUser)
            .WithMany(u => u.UploadedMaterials)
            .HasForeignKey(m => m.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
