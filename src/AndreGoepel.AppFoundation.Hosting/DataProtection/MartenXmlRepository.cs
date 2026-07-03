using System.Xml.Linq;
using Marten;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.Hosting.DataProtection;

/// <summary>
/// Stores DataProtection key ring entries as Marten documents. The document store
/// is resolved lazily so the repository can be wired into
/// <c>KeyManagementOptions</c> before Marten itself is built.
/// <see cref="IXmlRepository"/> is a synchronous contract, so the async Marten
/// calls are blocked on — key ring reads/writes are rare, startup-time operations.
/// </summary>
internal sealed class MartenXmlRepository(IServiceProvider services) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var store = services.GetRequiredService<IDocumentStore>();
        using var session = store.QuerySession();
        var documents = session
            .Query<DataProtectionKeyDocument>()
            .ToListAsync()
            .GetAwaiter()
            .GetResult();
        return ToElements(documents);
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        var store = services.GetRequiredService<IDocumentStore>();
        using var session = store.LightweightSession();
        session.Store(ToDocument(element, friendlyName));
        session.SaveChangesAsync().GetAwaiter().GetResult();
    }

    internal static IReadOnlyCollection<XElement> ToElements(
        IEnumerable<DataProtectionKeyDocument> documents
    ) => documents.Select(document => XElement.Parse(document.Xml)).ToList();

    internal static DataProtectionKeyDocument ToDocument(XElement element, string? friendlyName) =>
        new()
        {
            Id = string.IsNullOrWhiteSpace(friendlyName) ? Guid.NewGuid().ToString() : friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        };
}
