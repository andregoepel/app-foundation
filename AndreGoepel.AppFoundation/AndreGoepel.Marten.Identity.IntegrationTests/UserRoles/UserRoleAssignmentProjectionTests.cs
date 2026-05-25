using AndreGoepel.Marten.Identity.IntegrationTests.Infrastructure;
using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.UserRoles;
using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.Marten.Identity.Users.Events;
using Marten;

namespace AndreGoepel.Marten.Identity.IntegrationTests.UserRoles;

[Collection(IntegrationCollection.Name)]
public class UserRoleAssignmentProjectionTests(MartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RoleAssigned_CreatesAssignmentDocument()
    {
        // Arrange
        var userId = UserId.New();
        var roleId = RoleId.New();

        // Act
        await AppendAsync(userId, new RoleAssigned(userId, roleId, userId));

        // Assert
        await using var session = fixture.Store.QuerySession();
        var assignments = await session
            .Query<UserRoleAssignment>()
            .Where(a => a.UserGuid == userId)
            .ToListAsync(Ct);
        Assert.Single(assignments);
        Assert.Equal(roleId, assignments[0].RoleId);
    }

    [Fact]
    public async Task RoleAssigned_TwiceForSameRole_StillSingleAssignment()
    {
        // Arrange
        var userId = UserId.New();
        var roleId = RoleId.New();

        // Act
        await AppendAsync(
            userId,
            new RoleAssigned(userId, roleId, userId),
            new RoleAssigned(userId, roleId, userId)
        );

        // Assert
        await using var session = fixture.Store.QuerySession();
        var assignments = await session
            .Query<UserRoleAssignment>()
            .Where(a => a.UserGuid == userId)
            .ToListAsync(Ct);
        Assert.Single(assignments);
    }

    [Fact]
    public async Task RoleUnassigned_DeletesMatchingAssignment()
    {
        // Arrange
        var userId = UserId.New();
        var roleId = RoleId.New();
        await AppendAsync(userId, new RoleAssigned(userId, roleId, userId));

        // Act
        await AppendAsync(userId, new RoleUnassigned(userId, roleId, userId));

        // Assert
        await using var session = fixture.Store.QuerySession();
        var assignments = await session
            .Query<UserRoleAssignment>()
            .Where(a => a.UserGuid == userId)
            .ToListAsync(Ct);
        Assert.Empty(assignments);
    }

    [Fact]
    public async Task RoleUnassigned_LeavesOtherRoleAssignmentsIntact()
    {
        // Arrange
        var userId = UserId.New();
        var roleA = RoleId.New();
        var roleB = RoleId.New();
        await AppendAsync(
            userId,
            new RoleAssigned(userId, roleA, userId),
            new RoleAssigned(userId, roleB, userId)
        );

        // Act
        await AppendAsync(userId, new RoleUnassigned(userId, roleA, userId));

        // Assert
        await using var session = fixture.Store.QuerySession();
        var assignments = await session
            .Query<UserRoleAssignment>()
            .Where(a => a.UserGuid == userId)
            .ToListAsync(Ct);
        Assert.Single(assignments);
        Assert.Equal(roleB, assignments[0].RoleId);
    }

    private async Task AppendAsync(UserId streamId, params object[] events)
    {
        await using var session = fixture.Store.LightweightSession();
        session.Events.Append(streamId.Value, events);
        await session.SaveChangesAsync(Ct);
    }
}
