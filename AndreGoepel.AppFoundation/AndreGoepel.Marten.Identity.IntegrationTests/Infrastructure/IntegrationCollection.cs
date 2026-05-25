namespace AndreGoepel.Marten.Identity.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<MartenFixture>
{
    public const string Name = "Integration";
}
