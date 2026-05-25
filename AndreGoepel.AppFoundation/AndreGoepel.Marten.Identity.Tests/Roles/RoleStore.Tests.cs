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

    private readonly record struct AppendedEvent(Guid StreamId, object Event);

    private sealed record Harness(
        RoleStore<Role> Store,
        IDocumentSession Session,
        IEventStoreOperations Events,
        List<AppendedEvent> Appended,
        UserId ActorId
    )
    {
        public IEnumerable<object> Events_ => Appended.Select(a => a.Event);
    }

    private static Harness Build()
    {
        var events = Substitute.For<IEventStoreOperations>();
        var session = Substitute.For<IDocumentSession>();
        session.Events.Returns(events);

        var appended = new List<AppendedEvent>();
        events
            .When(e => e.Append(Arg.Any<Guid>(), Arg.Any<object[]>()))
            .Do(call =>
            {
                var streamId = (Guid)call.Args()[0]!;
                var args = (object[])call.Args()[1]!;
                foreach (var ev in args)
                    appended.Add(new AppendedEvent(streamId, ev));
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

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_AppendsToStreamMatchingExistingRoleId()
    {
        // Arrange
        var harness = Build();
        var role = new Role { Name = "Admin" };
        var expectedStream = role.RoleId.Value;

        // Act
        var result = await harness.Store.CreateAsync(role, CancellationToken.None);

        // Assert
        Assert.True(result.Succeeded);
        var appended = Assert.Single(harness.Appended);
        Assert.Equal(expectedStream, appended.StreamId);
        var created = Assert.IsType<RoleCreated>(appended.Event);
        Assert.Equal(role.RoleId, created.RoleId);
    }

    [Fact]
    public async Task CreateAsync_NullName_FailsWithoutAppending()
    {
        // Arrange
        var harness = Build();
        var role = new Role { Name = null };

        // Act
        var result = await harness.Store.CreateAsync(role, CancellationToken.None);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Empty(harness.Appended);
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
        var changed = Assert.IsType<RoleChanged>(Assert.Single(harness.Events_));
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
        var changed = Assert.IsType<RoleChanged>(Assert.Single(harness.Events_));
        Assert.True(changed.Deletable);
    }

    #endregion
}
