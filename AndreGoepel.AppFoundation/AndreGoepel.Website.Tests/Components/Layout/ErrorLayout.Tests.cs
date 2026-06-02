using AndreGoepel.Website.Layout;
using AndreGoepel.Website.Tests.Components;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace AndreGoepel.Website.Tests.Components.Layout;

public class ErrorLayoutTests : ErrorComponentContext
{
    [Fact]
    public void Render_RendersBodyContent()
    {
        // Arrange
        RenderFragment body = builder =>
            builder.AddMarkupContent(0, "<p data-test=\"body\">page body</p>");

        // Act
        var cut = Render<ErrorLayout>(p => p.Add(x => x.Body, body));

        // Assert
        Assert.Equal("page body", cut.Find("p[data-test=body]").TextContent);
    }
}
