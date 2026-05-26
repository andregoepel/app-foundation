namespace AndreGoepel.Marten.Identity.IntegrationTests.Infrastructure;

[Collection(IntegrationCollection.Name)]
public class MartenFixtureSmokeTests(MartenFixture fixture)
{
    [Fact]
    public async Task Container_Starts_And_Store_Saves()
    {
        // Arrange / Act
        await using var session = fixture.Store.LightweightSession();
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(fixture.Store);
    }
}
