using AndreGoepel.Marten.Identity.IntegrationTests.Infrastructure;
using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.Marten.Identity.Users.Events;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quartz;

namespace AndreGoepel.Marten.Identity.IntegrationTests.Cleanup;

[Collection(IntegrationCollection.Name)]
public class DeletedUserCleanupJobTests(MartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Execute_PurgesUsersDeletedBeyondRetention()
    {
        // Arrange
        var aged = await SeedDeletedUserAsync(DateTimeOffset.UtcNow.AddDays(-60));
        var recent = await SeedDeletedUserAsync(DateTimeOffset.UtcNow.AddDays(-5));
        var job = BuildJob(retentionDays: 30);

        // Act
        await job.Execute(Context());

        // Assert
        await using var session = fixture.Store.QuerySession();
        var agedDoc = await session.LoadAsync<User>(aged.Value, Ct);
        var recentDoc = await session.LoadAsync<User>(recent.Value, Ct);
        Assert.Null(agedDoc);
        Assert.NotNull(recentDoc);
    }

    [Fact]
    public async Task Execute_NoDeletedUsers_NoChanges()
    {
        // Arrange
        await SeedLiveUserAsync();
        var job = BuildJob(retentionDays: 30);

        // Act
        await job.Execute(Context());

        // Assert
        await using var session = fixture.Store.QuerySession();
        var users = await session.Query<User>().ToListAsync(Ct);
        Assert.Single(users);
    }

    private DeletedUserCleanupJob BuildJob(int retentionDays)
    {
        var settings = new CleanupSettingsService(
            fixture.Store,
            Options.Create(new DeletedUserCleanupOptions { RetentionDays = retentionDays }),
            Substitute.For<ISchedulerFactory>()
        );
        return new DeletedUserCleanupJob(
            fixture.Store,
            settings,
            NullLogger<DeletedUserCleanupJob>.Instance
        );
    }

    private IJobExecutionContext Context()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(Ct);
        return context;
    }

    private async Task<UserId> SeedDeletedUserAsync(DateTimeOffset deletedAt)
    {
        var userId = UserId.New();
        await using var session = fixture.Store.LightweightSession();
        session.Events.Append(
            userId.Value,
            new UserCreated(userId, "alice", "alice@example.com", "hash"),
            new UserDeleted(userId) { DeletedAt = deletedAt }
        );
        await session.SaveChangesAsync(Ct);
        return userId;
    }

    private async Task<UserId> SeedLiveUserAsync()
    {
        var userId = UserId.New();
        await using var session = fixture.Store.LightweightSession();
        session.Events.Append(
            userId.Value,
            new UserCreated(userId, "bob", "bob@example.com", "hash")
        );
        await session.SaveChangesAsync(Ct);
        return userId;
    }
}
