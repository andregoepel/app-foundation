using AndreGoepel.AppFoundation.Components.Pages;
using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.Tests.Components.Pages;

public sealed class NotFoundTests : BunitContext
{
    [Fact]
    public void Render_ShowsNotFoundMessage()
    {
        // Arrange — NotFound renders ErrorPage, which needs IHttpContextAccessor (#128).
        this.UseLooseJSInterop();
        Services.AddSingleton<IHttpContextAccessor>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() }
        );

        // Act
        var cut = Render<NotFound>();

        // Assert
        Assert.Contains("Page not found", cut.Markup);
    }
}
