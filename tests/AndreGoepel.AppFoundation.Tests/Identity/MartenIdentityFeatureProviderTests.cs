using AndreGoepel.AppFoundation.Identity;
using NSubstitute;

namespace AndreGoepel.AppFoundation.Tests.Identity;

public class MartenIdentityFeatureProviderTests
{
    [Fact]
    public async Task GetAsync_MapsStoredSettingsToFlags()
    {
        // Arrange
        var store = Substitute.For<IIdentityFeatureSettingsStore>();
        store
            .LoadAsync(Arg.Any<CancellationToken>())
            .Returns(
                new IdentityFeatureSettings
                {
                    EnableUserRegistration = false,
                    EnableTwoFactor = true,
                    EnablePasskey = false,
                }
            );
        var provider = new MartenIdentityFeatureProvider(store);

        // Act
        var flags = await provider.GetAsync();

        // Assert
        Assert.False(flags.UserRegistration);
        Assert.True(flags.TwoFactor);
        Assert.False(flags.Passkey);
    }
}
