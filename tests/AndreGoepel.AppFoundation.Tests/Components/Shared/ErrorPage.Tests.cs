using AndreGoepel.AppFoundation.Components.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.Tests.Components.Shared;

public sealed class ErrorPageTests : BunitContext
{
    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<ErrorPage> RenderError(string code, HttpContext? httpContext = null)
    {
        this.UseLooseJSInterop();
        Services.AddSingleton<IHttpContextAccessor>(
            new HttpContextAccessor { HttpContext = httpContext ?? new DefaultHttpContext() }
        );
        return Render<ErrorPage>(p => p.Add(c => c.Code, code));
    }

    [Fact]
    public void Render_With404_ShowsNotFoundMessage()
    {
        // Act
        var cut = RenderError("404");

        // Assert
        Assert.Contains("Page not found", cut.Markup);
        Assert.Contains("has moved", cut.Markup);
    }

    [Fact]
    public void Render_With403_ShowsAccessDeniedMessage()
    {
        // Act
        var cut = RenderError("403");

        // Assert
        Assert.Contains("Access denied", cut.Markup);
        Assert.Contains("permission", cut.Markup);
    }

    [Fact]
    public void Render_WithUnknownCode_FallsBackToNotFound()
    {
        // Act
        var cut = RenderError("500");

        // Assert
        Assert.Contains("Page not found", cut.Markup);
    }

    [Fact]
    public void Render_German_With404_ShowsGermanNotFoundMessage()
    {
        using var culture = CultureScope.UiOnly("de");

        // Act
        var cut = RenderError("404");

        // Assert
        Assert.Contains("Seite nicht gefunden", cut.Markup);
    }

    [Fact]
    public void GoToHomeButton_NavigatesToRoot()
    {
        // Arrange
        var cut = RenderError("404");

        // Act
        cut.Find("button").Click();

        // Assert
        Assert.Equal("http://localhost/", Nav.Uri);
    }

    [Fact]
    public void Render_With404_SetsResponseStatusCode404()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        RenderError("404", httpContext);

        // Assert
        Assert.Equal(404, httpContext.Response.StatusCode);
    }

    [Fact]
    public void Render_With403_SetsResponseStatusCode403()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        RenderError("403", httpContext);

        // Assert
        Assert.Equal(403, httpContext.Response.StatusCode);
    }

    [Fact]
    public void Render_WithUnknownCode_SetsResponseStatusCode404()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        RenderError("500", httpContext);

        // Assert
        Assert.Equal(404, httpContext.Response.StatusCode);
    }

    [Fact]
    public void Render_NoHttpContext_DoesNotThrow()
    {
        // Arrange
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = null });
        this.UseLooseJSInterop();

        // Act
        var cut = Render<ErrorPage>(p => p.Add(c => c.Code, "404"));

        // Assert
        Assert.Contains("Page not found", cut.Markup);
    }

    [Fact]
    public void Render_ResponseAlreadyStarted_DoesNotOverwriteStatusCode()
    {
        // Arrange — a response that has already begun sending (e.g. an interactive
        // circuit re-rendering after the initial static-SSR response completed).
        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        var httpContext = new DefaultHttpContext(features) { Response = { StatusCode = 200 } };

        // Act
        RenderError("404", httpContext);

        // Assert
        Assert.Equal(200, httpContext.Response.StatusCode);
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
