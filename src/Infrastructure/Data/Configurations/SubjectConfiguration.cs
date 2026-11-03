using Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("subjects");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(s => s.Code)
            .IsUnique();

        builder.Property(s => s.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(s => s.YearLevel)
            .IsRequired();

        builder.HasIndex(s => s.YearLevel);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.HasOne(s => s.AcademicYear)
            .WithMany(a => a.Subjects)
            .HasForeignKey(s => s.YearLevel)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Materials)
            .WithOne(m => m.Subject)
            .HasForeignKey(m => m.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
