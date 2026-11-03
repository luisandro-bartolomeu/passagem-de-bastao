using FluentAssertions;
using Infrastructure.Security;

namespace UnitTests;

public class PasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_ShouldReturnHashedPassword_DifferentFromRaw()
    {
        const string rawPassword = "SecurePassword123!";

        var hash = _hasher.HashPassword(rawPassword);

        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().NotBe(rawPassword);
        hash.Should().StartWith("$2"); // BCrypt signature
    }

    [Fact]
    public void HashPassword_ShouldGenerateDifferentHashesForSamePassword_DueToSalting()
    {
        const string rawPassword = "IdenticalPassword!";

        var hash1 = _hasher.HashPassword(rawPassword);
        var hash2 = _hasher.HashPassword(rawPassword);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ShouldReturnTrue()
    {
        const string rawPassword = "CorrectPassword123#";
        var hash = _hasher.HashPassword(rawPassword);

        var isValid = _hasher.VerifyPassword(rawPassword, hash);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithIncorrectPassword_ShouldReturnFalse()
    {
        const string rawPassword = "CorrectPassword123#";
        const string wrongPassword = "WrongPassword123#";
        var hash = _hasher.HashPassword(rawPassword);

        var isValid = _hasher.VerifyPassword(wrongPassword, hash);

        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "some_hash")]
    [InlineData("   ", "some_hash")]
    [InlineData("password", "")]
    [InlineData("password", "   ")]
    [InlineData("password", "not_a_valid_bcrypt_hash")]
    public void VerifyPassword_WithInvalidInputs_ShouldReturnFalseSafely(string password, string hash)
    {
        var isValid = _hasher.VerifyPassword(password, hash);

        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void HashPassword_WithEmptyOrWhitespace_ShouldThrowArgumentException(string invalidPassword)
    {
        var act = () => _hasher.HashPassword(invalidPassword);

        act.Should().Throw<ArgumentException>();
    }
}
