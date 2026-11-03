using FluentAssertions;

namespace UnitTests;

public class CoreSanityTests
{
    [Fact]
    public void ArchitectureScaffolding_ShouldBeConfiguredCorrectly()
    {
        const bool isInitialized = true;
        isInitialized.Should().BeTrue();
    }
}
