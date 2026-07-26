using System.Xml.Linq;
using AndreGoepel.AppFoundation.Hosting.DataProtection;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

/// <summary>
/// Pure, I/O-free conversion logic only. The real Marten round trip (<c>StoreElement</c> /
/// <c>GetAllElements</c> against Postgres) lives in <c>AndreGoepel.AppFoundation.IntegrationTests</c>
/// — see <c>MartenXmlRepositoryTests</c> there — which superseded the NSubstitute-mocked version
/// of that coverage that used to live in this file.
/// </summary>
public sealed class MartenXmlRepositoryTests
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
}
