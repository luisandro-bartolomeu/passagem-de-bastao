using Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class AcademicYearConfiguration : IEntityTypeConfiguration<AcademicYear>
{
    public void Configure(EntityTypeBuilder<AcademicYear> builder)
    {
        builder.ToTable("academic_years");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.YearLevel)
            .IsRequired();

        builder.Property(a => a.Name)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasData(
            new AcademicYear { Id = 1, YearLevel = 1, Name = "1.º Ano" },
            new AcademicYear { Id = 2, YearLevel = 2, Name = "2.º Ano" },
            new AcademicYear { Id = 3, YearLevel = 3, Name = "3.º Ano" },
            new AcademicYear { Id = 4, YearLevel = 4, Name = "4.º Ano" }
        );
    }
}
