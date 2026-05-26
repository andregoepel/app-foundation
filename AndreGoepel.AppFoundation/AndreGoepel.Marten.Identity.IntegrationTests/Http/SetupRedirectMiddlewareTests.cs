using System.Reflection;
using AndreGoepel.Marten.Identity.Http;
using AndreGoepel.Marten.Identity.IntegrationTests.Infrastructure;
using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Roles.Events;
using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.Marten.Identity.Users.Events;
using Microsoft.AspNetCore.Http;

namespace AndreGoepel.Marten.Identity.IntegrationTests.Http;

[Collection(IntegrationCollection.Name)]
public class SetupRedirectMiddlewareTests(MartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync(Ct);
        // _isConfigured is static; reset between tests.
        typeof(SetupRedirectMiddleware)
            .GetField("_isConfigured", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task UnconfiguredStore_NonSetupPath_RedirectsToSetup()
    {
        // Arrange
        var (middleware, context, called) = Build("/dashboard");

        // Act
        await middleware.Invoke(context, fixture.Store.QuerySession());

        // Assert
        Assert.Equal("/Setup", context.Response.Headers.Location.ToString());
        Assert.False(called.Value);
    }

    [Fact]
    public async Task UnconfiguredStore_SetupPath_PassesThrough()
    {
        // Arrange
        var (middleware, context, called) = Build("/Setup");

        // Act
        await middleware.Invoke(context, fixture.Store.QuerySession());

        // Assert
        Assert.True(called.Value);
    }

    [Fact]
    public async Task UnconfiguredStore_StaticAsset_PassesThrough()
    {
        // Arrange
        var (middleware, context, called) = Build("/css/app.css");

        // Act
        await middleware.Invoke(context, fixture.Store.QuerySession());

        // Assert
        Assert.True(called.Value);
    }

    [Fact]
    public async Task ConfiguredStore_PassesThroughAnyPath()
    {
        // Arrange
        await SeedConfiguredAsync();
        var (middleware, context, called) = Build("/dashboard");

        // Act
        await middleware.Invoke(context, fixture.Store.QuerySession());

        // Assert
        Assert.True(called.Value);
    }

    private static (
        SetupRedirectMiddleware Middleware,
        DefaultHttpContext Context,
        Box<bool> Called
    ) Build(string path)
    {
        var called = new Box<bool>();
        var middleware = new SetupRedirectMiddleware(_ =>
        {
            called.Value = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return (middleware, context, called);
    }

    private async Task SeedConfiguredAsync()
    {
        await using var session = fixture.Store.LightweightSession();
        var roleId = RoleId.New();
        var userId = UserId.New();
        session.Events.Append(roleId.Value, new RoleCreated(roleId, "Administrator", userId));
        session.Events.Append(
            userId.Value,
            new UserCreated(userId, "alice", "alice@example.com", "hash")
        );
        await session.SaveChangesAsync(Ct);
    }

    private sealed class Box<T>
    {
        public T Value { get; set; } = default!;
    }
}
