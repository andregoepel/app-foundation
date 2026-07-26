using System.Xml.Linq;
using AndreGoepel.AppFoundation.Hosting.DataProtection;
using AndreGoepel.Marten.Testing;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure = AndreGoepel.AppFoundation.IntegrationTests.Infrastructure;

namespace AndreGoepel.AppFoundation.IntegrationTests.Hosting;

/// <summary>
/// Exercises <see cref="MartenXmlRepository"/> against a real Postgres via
/// <see cref="MartenFixture"/>, superseding the previous NSubstitute-mocked unit test — that
/// version only proved <c>IDocumentSession.Store</c>/<c>SaveChangesAsync</c> were called with the
/// right arguments, not that a round trip through Marten actually persists and reads back the
/// key ring. The pure, I/O-free <c>ToDocument</c>/<c>ToElements</c> conversion tests stay in
/// <c>AndreGoepel.AppFoundation.Tests</c> (see <c>MartenXmlRepositoryTests</c> there).
/// </summary>
/// <remarks>
/// The collection name is referenced via a namespace alias rather than a type-importing
/// <c>using</c>: <c>AndreGoepel.Marten.Testing</c> ships its own <c>IntegrationCollection</c> as
/// a copy-paste example (bound to the base <see cref="MartenFixture"/>, not meant to be
/// referenced across assemblies), which would otherwise collide (CS0104) with this project's own
/// <see cref="AndreGoepel.AppFoundation.IntegrationTests.Infrastructure.IntegrationCollection"/>.
/// </remarks>
[Collection(Infrastructure.IntegrationCollection.Name)]
public sealed class MartenXmlRepositoryTests(MartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static MartenXmlRepository BuildRepository(MartenFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fixture.Store);
        return new MartenXmlRepository(services.BuildServiceProvider());
    }

    [Fact]
    public void StoreElement_ThenGetAllElements_RoundTrips()
    {
        // Arrange
        var repository = BuildRepository(fixture);
        var element = new XElement("key", new XAttribute("id", "abc"));

        // Act
        repository.StoreElement(element, "key-abc");
        var elements = repository.GetAllElements();

        // Assert
        var stored = Assert.Single(elements);
        Assert.Equal("abc", stored.Attribute("id")!.Value);
    }

    [Fact]
    public void StoreElement_WithoutFriendlyName_IsRetrievableByGeneratedId()
    {
        // Arrange
        var repository = BuildRepository(fixture);
        var element = new XElement("key", new XAttribute("id", "xyz"));

        // Act
        repository.StoreElement(element, friendlyName: "");
        var elements = repository.GetAllElements();

        // Assert
        var stored = Assert.Single(elements);
        Assert.Equal("xyz", stored.Attribute("id")!.Value);
    }

    [Fact]
    public void StoreElement_CalledTwiceWithSameFriendlyName_OverwritesExistingDocument()
    {
        // Arrange
        var repository = BuildRepository(fixture);

        // Act
        repository.StoreElement(new XElement("key", new XAttribute("v", "1")), "key-abc");
        repository.StoreElement(new XElement("key", new XAttribute("v", "2")), "key-abc");
        var elements = repository.GetAllElements();

        // Assert
        var stored = Assert.Single(elements);
        Assert.Equal("2", stored.Attribute("v")!.Value);
    }
}
