using AndreGoepel.Marten.Testing;

namespace AndreGoepel.AppFoundation.IntegrationTests.Infrastructure;

/// <summary>
/// Uses the base <see cref="MartenFixture"/> directly (no subclass): every document under
/// test here (<c>DataProtectionKeyDocument</c>) is a plain Marten document with no projections,
/// settings-document registration, or identity wiring for <see cref="MartenFixture.ConfigureStore"/>
/// to add — <c>AutoCreateSchemaObjects.All</c> from the base fixture is all that's needed.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<MartenFixture>
{
    public const string Name = "Integration";
}
