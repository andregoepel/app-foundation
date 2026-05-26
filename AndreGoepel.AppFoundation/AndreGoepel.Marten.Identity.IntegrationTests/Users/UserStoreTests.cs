using AndreGoepel.Marten.Identity.IntegrationTests.Infrastructure;
using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Roles.Events;
using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.Marten.Identity.Users.Events;
using Marten;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.IntegrationTests.Users;

[Collection(IntegrationCollection.Name)]
public class UserStoreTests(MartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region CreateAsync / UpdateAsync

    [Fact]
    public async Task CreateAsync_PersistsUser_ProjectionWritesDocument()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var user = UserStoreTestHelpers.NewUser();

        // Act
        var result = await store.CreateAsync(user, Ct);

        // Assert
        Assert.True(result.Succeeded);
        await using var session = fixture.Store.QuerySession();
        var persisted = await session.LoadAsync<User>(user.UserId.Value, Ct);
        Assert.NotNull(persisted);
        Assert.Equal("alice@example.com", persisted.Email);
        Assert.False(persisted.Deleted);
    }

    [Fact]
    public async Task UpdateAsync_AppliesPhoneNumber()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var user = UserStoreTestHelpers.NewUser();
        await store.CreateAsync(user, Ct);

        // Act
        user.PhoneNumber = "+49 123 4567890";
        var result = await store.UpdateAsync(user, Ct);

        // Assert
        Assert.True(result.Succeeded);
        await using var session = fixture.Store.QuerySession();
        var persisted = await session.LoadAsync<User>(user.UserId.Value, Ct);
        Assert.Equal("+49 123 4567890", persisted!.PhoneNumber);
    }

    [Fact]
    public async Task UpdateAsync_NoChange_DoesNotAppendSecondEvent()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var user = UserStoreTestHelpers.NewUser();
        await store.CreateAsync(user, Ct);

        // Act
        var result = await store.UpdateAsync(user, Ct);

        // Assert
        Assert.True(result.Succeeded);
        await using var session = fixture.Store.QuerySession();
        var stream = await session.Events.FetchStreamAsync(user.UserId.Value, token: Ct);
        Assert.Single(stream);
        Assert.IsType<UserCreated>(stream[0].Data);
    }

    #endregion

    #region DeleteAsync / RestoreAsync

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var user = UserStoreTestHelpers.NewUser();
        await store.CreateAsync(user, Ct);

        // Act
        var result = await store.DeleteAsync(user, Ct);

        // Assert
        Assert.True(result.Succeeded);
        await using var session = fixture.Store.QuerySession();
        var persisted = await session.LoadAsync<User>(user.UserId.Value, Ct);
        Assert.True(persisted!.Deleted);
        Assert.Null(persisted.Email);
    }

    [Fact]
    public async Task DeleteAsync_NonDeletableUser_FailsAndAppendsNothing()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var user = UserStoreTestHelpers.NewUser();
        user.Deletable = false;
        await store.CreateAsync(user, Ct);

        // Act
        var result = await store.DeleteAsync(user, Ct);

        // Assert
        Assert.False(result.Succeeded);
        await using var session = fixture.Store.QuerySession();
        var stream = await session.Events.FetchStreamAsync(user.UserId.Value, token: Ct);
        Assert.DoesNotContain(stream, e => e.Data is UserDeleted);
    }

    [Fact]
    public async Task RestoreAsync_AfterDelete_RestoresEmailAndClearsDeleted()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var user = UserStoreTestHelpers.NewUser();
        await store.CreateAsync(user, Ct);
        await store.DeleteAsync(user, Ct);

        // Act
        var result = await store.RestoreAsync(user, Ct);

        // Assert
        Assert.True(result.Succeeded);
        await using var session = fixture.Store.QuerySession();
        var persisted = await session.LoadAsync<User>(user.UserId.Value, Ct);
        Assert.False(persisted!.Deleted);
        Assert.Equal("alice@example.com", persisted.Email);
        Assert.Equal("hash", persisted.PasswordHash);
    }

    #endregion

    #region Authenticator key / recovery codes (data protection)

    [Fact]
    public async Task AuthenticatorKey_RoundTripsThroughDataProtection()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var user = UserStoreTestHelpers.NewUser();

        // Act
        await store.SetAuthenticatorKeyAsync(user, "totp-secret", Ct);
        var read = await store.GetAuthenticatorKeyAsync(user, Ct);

        // Assert
        Assert.NotEqual("totp-secret", user.AuthenticatorKey);
        Assert.Equal("totp-secret", read);
    }

    [Fact]
    public async Task RecoveryCodes_RedeemRemovesOneCode_KeepsOthers()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var user = UserStoreTestHelpers.NewUser();
        await store.ReplaceCodesAsync(user, ["AAA", "BBB", "CCC"], Ct);

        // Act
        var redeemed = await store.RedeemCodeAsync(user, "BBB", Ct);
        var remaining = await store.CountCodesAsync(user, Ct);

        // Assert
        Assert.True(redeemed);
        Assert.Equal(2, remaining);
        Assert.False(await store.RedeemCodeAsync(user, "BBB", Ct));
    }

    #endregion

    #region Passkeys

    [Fact]
    public async Task Passkey_AddFindRemove_RoundTrip()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var user = UserStoreTestHelpers.NewUser();
        await store.CreateAsync(user, Ct);
        var passkey = TestPasskey([1, 2, 3, 4]);

        // Act
        await store.AddOrUpdatePasskeyAsync(user, passkey, Ct);
        var refreshed = await store.FindByIdAsync(user.Id, Ct);
        var found = await store.FindPasskeyAsync(refreshed!, [1, 2, 3, 4], Ct);
        await store.RemovePasskeyAsync(refreshed!, [1, 2, 3, 4], Ct);
        var afterRemoval = await store.FindByIdAsync(user.Id, Ct);
        var passkeys = await store.GetPasskeysAsync(afterRemoval!, Ct);

        // Assert
        Assert.NotNull(found);
        Assert.Empty(passkeys);
    }

    #endregion

    #region Roles

    [Fact]
    public async Task AddToRole_PopulatesGetRoles()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var user = UserStoreTestHelpers.NewUser();
        await store.CreateAsync(user, Ct);
        await SeedRoleAsync("Administrator");
        var fresh = await store.FindByIdAsync(user.Id, Ct);

        // Act
        await store.AddToRoleAsync(fresh!, "ADMINISTRATOR", Ct);
        var afterAdd = await store.FindByIdAsync(user.Id, Ct);
        var roles = await store.GetRolesAsync(afterAdd!, Ct);

        // Assert
        Assert.Contains("Administrator", roles);
    }

    [Fact]
    public async Task RemoveFromRole_RemovesFromGetRoles()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var user = UserStoreTestHelpers.NewUser();
        await store.CreateAsync(user, Ct);
        await SeedRoleAsync("Member");
        var fresh = await store.FindByIdAsync(user.Id, Ct);
        await store.AddToRoleAsync(fresh!, "MEMBER", Ct);

        // Act
        var withRole = await store.FindByIdAsync(user.Id, Ct);
        await store.RemoveFromRoleAsync(withRole!, "MEMBER", Ct);
        var after = await store.FindByIdAsync(user.Id, Ct);
        var roles = await store.GetRolesAsync(after!, Ct);

        // Assert
        Assert.Empty(roles);
    }

    [Fact]
    public async Task GetUsersInRole_ReturnsAssignedUsers()
    {
        // Arrange
        var store = UserStoreTestHelpers.BuildStore(fixture.Store);
        var alice = UserStoreTestHelpers.NewUser("alice@example.com");
        var bob = UserStoreTestHelpers.NewUser("bob@example.com");
        await store.CreateAsync(alice, Ct);
        await store.CreateAsync(bob, Ct);
        await SeedRoleAsync("Member");
        var freshAlice = await store.FindByIdAsync(alice.Id, Ct);
        await store.AddToRoleAsync(freshAlice!, "MEMBER", Ct);

        // Act
        var users = await store.GetUsersInRoleAsync("MEMBER", Ct);

        // Assert
        Assert.Single(users);
        Assert.Equal("alice@example.com", users[0].Email);
    }

    #endregion

    #region Helpers

    private static UserPasskeyInfo TestPasskey(byte[] credentialId) =>
        new(
            credentialId,
            publicKey: [9, 9, 9],
            createdAt: DateTimeOffset.UtcNow,
            signCount: 0,
            transports: null,
            isUserVerified: false,
            isBackupEligible: false,
            isBackedUp: false,
            attestationObject: [],
            clientDataJson: []
        );

    private async Task SeedRoleAsync(string name)
    {
        await using var session = fixture.Store.LightweightSession();
        var roleId = RoleId.New();
        session.Events.Append(roleId.Value, new RoleCreated(roleId, name, UserId.New()));
        await session.SaveChangesAsync(Ct);
    }

    #endregion
}
