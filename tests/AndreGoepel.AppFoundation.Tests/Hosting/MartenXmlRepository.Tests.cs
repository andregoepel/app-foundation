using System.Xml.Linq;
using AndreGoepel.AppFoundation.Hosting.DataProtection;
using Marten;
using NSubstitute;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public class MartenXmlRepositoryTests
{
    [Fact]
    public void ToDocument_WithFriendlyName_UsesFriendlyNameAsId()
    {
        // Arrange
        var element = new XElement("key", new XAttribute("id", "abc"));

        // Act
        var document = MartenXmlRepository.ToDocument(element, "key-abc");

        // Assert
        Assert.Equal("key-abc", document.Id);
        Assert.Equal(element.ToString(SaveOptions.DisableFormatting), document.Xml);
    }

    [Fact]
    public void ToDocument_WithoutFriendlyName_GeneratesGuidId()
    {
        // Act
        var document = MartenXmlRepository.ToDocument(new XElement("key"), "");

        // Assert
        Assert.True(Guid.TryParse(document.Id, out _));
    }

    [Fact]
    public void ToElements_ParsesStoredXml()
    {
        // Arrange
        var documents = new List<DataProtectionKeyDocument>
        {
            new() { Id = "key-1", Xml = """<key id="1" />""" },
            new() { Id = "key-2", Xml = """<key id="2" />""" },
        };

        // Act
        var elements = MartenXmlRepository.ToElements(documents);

        // Assert
        Assert.Equal(2, elements.Count);
        Assert.Equal(["1", "2"], elements.Select(e => e.Attribute("id")!.Value));
    }

    [Fact]
    public void StoreElement_PersistsDocumentAndSavesSession()
    {
        // Arrange
        var session = Substitute.For<IDocumentSession>();
        var store = Substitute.For<IDocumentStore>();
        store.LightweightSession().Returns(session);
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IDocumentStore)).Returns(store);
        var repository = new MartenXmlRepository(services);
        var element = new XElement("key", new XAttribute("id", "abc"));

        // Act
        repository.StoreElement(element, "key-abc");

        // Assert
        session
            .Received(1)
            .Store(
                Arg.Is<DataProtectionKeyDocument[]>(documents =>
                    documents.Length == 1
                    && documents[0].Id == "key-abc"
                    && documents[0].Xml == element.ToString(SaveOptions.DisableFormatting)
                )
            );
        session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
