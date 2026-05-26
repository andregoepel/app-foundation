using AndreGoepel.Marten.Identity.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quartz;

namespace AndreGoepel.Marten.Identity.IntegrationTests.Cleanup;

[Collection(IntegrationCollection.Name)]
public class CleanupSettingsServiceTests(MartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetAsync_NoStoredSettings_ReturnsDefaults()
    {
        // Arrange
        var service = BuildService(defaultRetention: 14, defaultCron: "0 0 1 * * ?");

        // Act
        var settings = await service.GetAsync(Ct);

        // Assert
        Assert.Equal(14, settings.RetentionDays);
        Assert.Equal("0 0 1 * * ?", settings.CronSchedule);
    }

    [Fact]
    public async Task GetAsync_WithStoredSettings_ReturnsStored()
    {
        // Arrange
        await using (var seed = fixture.Store.LightweightSession())
        {
            seed.Store(new CleanupSettings { RetentionDays = 90, CronSchedule = "0 0 3 * * ?" });
            await seed.SaveChangesAsync(Ct);
        }
        var service = BuildService(defaultRetention: 14, defaultCron: "0 0 1 * * ?");

        // Act
        var settings = await service.GetAsync(Ct);

        // Assert
        Assert.Equal(90, settings.RetentionDays);
        Assert.Equal("0 0 3 * * ?", settings.CronSchedule);
    }

    [Fact]
    public async Task SaveAsync_PersistsAndReschedulesTrigger()
    {
        // Arrange
        var scheduler = Substitute.For<IScheduler>();
        var schedulerFactory = Substitute.For<ISchedulerFactory>();
        schedulerFactory.GetScheduler(Arg.Any<CancellationToken>()).Returns(scheduler);
        var service = new CleanupSettingsService(
            fixture.Store,
            Options.Create(new DeletedUserCleanupOptions()),
            schedulerFactory
        );
        var newSettings = new CleanupSettings { RetentionDays = 7, CronSchedule = "0 0 4 * * ?" };

        // Act
        await service.SaveAsync(newSettings, Ct);

        // Assert
        await using var session = fixture.Store.QuerySession();
        var persisted = await session.LoadAsync<CleanupSettings>(CleanupSettings.DocumentId, Ct);
        Assert.NotNull(persisted);
        Assert.Equal(7, persisted.RetentionDays);
        Assert.Equal("0 0 4 * * ?", persisted.CronSchedule);

        await scheduler
            .Received(1)
            .RescheduleJob(
                Arg.Any<TriggerKey>(),
                Arg.Any<ITrigger>(),
                Arg.Any<CancellationToken>()
            );
    }

    private CleanupSettingsService BuildService(int defaultRetention, string defaultCron) =>
        new(
            fixture.Store,
            Options.Create(
                new DeletedUserCleanupOptions
                {
                    RetentionDays = defaultRetention,
                    CronSchedule = defaultCron,
                }
            ),
            Substitute.For<ISchedulerFactory>()
        );
}
