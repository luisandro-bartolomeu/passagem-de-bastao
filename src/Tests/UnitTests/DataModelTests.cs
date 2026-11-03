using Core.Entities;
using Core.Enums;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace UnitTests;

public class DataModelTests
{
    [Fact]
    public void AppDbContext_ModelCreation_ShouldContainAllEntitiesAndConfigurations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy")
            .Options;

        using var context = new AppDbContext(options);
        var model = context.Model;

        model.FindEntityType(typeof(User)).Should().NotBeNull();
        model.FindEntityType(typeof(RefreshToken)).Should().NotBeNull();
        model.FindEntityType(typeof(AcademicYear)).Should().NotBeNull();
        model.FindEntityType(typeof(Subject)).Should().NotBeNull();
        model.FindEntityType(typeof(Material)).Should().NotBeNull();
    }

    [Fact]
    public void UserEntity_DefaultValues_ShouldBeSetCorrectly()
    {
        var user = new User
        {
            Name = "John Doe",
            Email = "john@isutic.ed.ao",
            PasswordHash = "hash",
            Role = UserRole.Delegado
        };

        user.IsActive.Should().BeTrue();
        user.RefreshTokens.Should().BeEmpty();
        user.UploadedMaterials.Should().BeEmpty();
        user.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RefreshTokenEntity_IsActive_ShouldReturnCorrectStatus()
    {
        var activeToken = new RefreshToken
        {
            TokenHash = "hash",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false
        };

        var expiredToken = new RefreshToken
        {
            TokenHash = "hash",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            IsRevoked = false
        };

        var revokedToken = new RefreshToken
        {
            TokenHash = "hash",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = true
        };

        activeToken.IsActive.Should().BeTrue();
        expiredToken.IsActive.Should().BeFalse();
        revokedToken.IsActive.Should().BeFalse();
    }

    [Fact]
    public void MaterialEntity_DefaultValues_ShouldBePending()
    {
        var material = new Material
        {
            Title = "Exame de Cálculo I",
            Description = "Enunciado do exame",
            MaterialType = MaterialType.Exam,
            FileStorageKey = "materials/2026/exame1.pdf",
            OriginalFileName = "exame1.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024
        };

        material.Status.Should().Be(MaterialStatus.Pending);
        material.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        material.UpdatedAt.Should().BeNull();
    }
}
