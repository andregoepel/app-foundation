using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Roles.Events;
using AndreGoepel.Marten.Identity.Services;
using AndreGoepel.Marten.Identity.Users;
using Marten;
using Marten.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AndreGoepel.Marten.Identity.Tests.Roles;

public class RoleStoreTests
{
    #region Helpers

    private sealed record Harness(
        RoleStore<Role> Store,
        IDocumentSession Session,
        IEventStoreOperations Events,
        List<object> Appended,
        UserId ActorId
    );

    private static Harness Build()
    {
        var events = Substitute.For<IEventStoreOperations>();
        var session = Substitute.For<IDocumentSession>();
        session.Events.Returns(events);

        var appended = new List<object>();
        events
            .When(e => e.Append(Arg.Any<Guid>(), Arg.Any<object[]>()))
            .Do(call =>
            {
                var args = (object[])call.Args()[1]!;
                appended.AddRange(args);
            });

        var actor = UserId.New();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.GetCurrentUserIdAsync(Arg.Any<CancellationToken>()).Returns(actor);

        var logger = Substitute.For<ILogger<RoleStore<Role>>>();

        return new Harness(
            new RoleStore<Role>(session, currentUser, logger),
            session,
            events,
            appended,
            actor
        );
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_PreservesDeletableFalse_OnRoleChangedEvent()
    {
        // Arrange
        var harness = Build();
        var role = new Role { Name = "System", Deletable = false };

        // Act
        var result = await harness.Store.UpdateAsync(role, CancellationToken.None);

        // Assert
        Assert.True(result.Succeeded);
        var changed = Assert.IsType<RoleChanged>(Assert.Single(harness.Appended));
        Assert.False(changed.Deletable);
    }

    [Fact]
    public async Task UpdateAsync_PreservesDeletableTrue_OnRoleChangedEvent()
    {
        // Arrange
        var harness = Build();
        var role = new Role { Name = "Admin", Deletable = true };

        // Act
        await harness.Store.UpdateAsync(role, CancellationToken.None);

        // Assert
        var changed = Assert.IsType<RoleChanged>(Assert.Single(harness.Appended));
        Assert.True(changed.Deletable);
    }

    #endregion
}
