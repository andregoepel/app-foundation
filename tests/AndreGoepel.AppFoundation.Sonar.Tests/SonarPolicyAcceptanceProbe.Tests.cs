using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class SonarPolicyAcceptanceProbeTests
{
    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 15)]
    [InlineData(10, 55)]
    [InlineData(11, 55)]
    public void Calculate_RepresentativeInputs_ExecutesEveryBranch(int input, int expected)
    {
        // Arrange
        var value = input;

        // Act
        var result = SonarPolicyAcceptanceProbe.Calculate(value);

        // Assert
        Assert.Equal(expected, result);
    }
}
