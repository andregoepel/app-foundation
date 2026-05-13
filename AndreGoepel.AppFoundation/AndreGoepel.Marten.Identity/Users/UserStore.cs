using System.Security.Claims;
using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Services;
using AndreGoepel.Marten.Identity.UserRoles;
using AndreGoepel.Marten.Identity.Users.Events;
using Marten;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AndreGoepel.Marten.Identity.Users;

public class UserStore<TUser>(
    IDocumentStore documentStore,
    IQuerySession querySession,
    IDataProtectionProvider dataProtectionProvider,
    ICurrentUserService currentUserService,
    ILogger<UserStore<TUser>> logger
)
    : IUserStore<TUser>,
        IUserPasswordStore<TUser>,
        IUserEmailStore<TUser>,
        IUserPhoneNumberStore<TUser>,
        IUserTwoFactorStore<TUser>,
        IUserAuthenticatorKeyStore<TUser>,
        IUserTwoFactorRecoveryCodeStore<TUser>,
        IQueryableUserStore<TUser>,
        IUserClaimStore<TUser>,
        IUserPasskeyStore<TUser>,
        IUserRoleStore<TUser>,
        IUserLockoutStore<TUser>
    where TUser : User
{
    private const string _userDataProtectionPurpose = "UserDataProtection";

    public IQueryable<TUser> Users => querySession.Query<TUser>();

    public Task<string> GetUserIdAsync(TUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(TUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.UserName);

    public Task SetUserNameAsync(TUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(
        TUser user,
        CancellationToken cancellationToken
    ) => Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(
        TUser user,
        string? normalizedName,
        CancellationToken cancellationToken
    )
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(TUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var userId = UserId.Parse(user.Id);

            using var session = documentStore.LightweightSession();

            session.Events.Append(
                userId.Value,
                new UserCreated(userId, user.UserName, user.Email, user.PasswordHash)
                {
                    RootUser = user.RootUser,
                    Deletable = user.Deletable,
                }
            );
            await session.SaveChangesAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create the user in Marten.");
            return IdentityResult.Failed(
                new IdentityError() { Description = "Something went wrong saving the user." }
            );
        }
    }

    public async Task<IdentityResult> UpdateAsync(TUser user, CancellationToken cancellationToken)
    {
        try
        {
            var userId = UserId.Parse(user.Id);

            var existingUser = await querySession
                .Query<TUser>()
                .FirstOrDefaultAsync(x => x.Id == user.Id, token: cancellationToken);

            if (existingUser != null && existingUser.AreEqual(user))
                return IdentityResult.Success;

            using var session = documentStore.LightweightSession();

            session.Events.Append(
                userId.Value,
                new UserUpdated(userId)
                {
                    UserName = user.UserName,
                    Email = user.Email,
                    PasswordHash = user.PasswordHash,
                    EmailConfirmed = user.EmailConfirmed,
                    PhoneNumber = user.PhoneNumber,
                    AuthenticatorKey = user.AuthenticatorKey,
                    RecoveryCodes = user.RecoveryCodes,
                    TwoFactorEnabled = user.TwoFactorEnabled,
                    Deletable = user.Deletable,
                    LockoutEnabled = user.LockoutEnabled,
                    LockoutEnd = user.LockoutEnd,
                    AccessFailedCount = user.AccessFailedCount,
                }
            );
            await session.SaveChangesAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update the user in Marten.");
            return IdentityResult.Failed(
                new IdentityError() { Description = "Something went wrong saving the user." }
            );
        }
    }

    public async Task<IdentityResult> DeleteAsync(TUser user, CancellationToken cancellationToken)
    {
        try
        {
            if (!user.Deletable)
            {
                return IdentityResult.Failed(
                    new IdentityError() { Description = "This user cannot be deleted." }
                );
            }

            var userId = UserId.Parse(user.Id);

            using var session = documentStore.LightweightSession();

            session.Events.Append(userId.Value, new UserDeleted(userId));
            await session.SaveChangesAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Failed to delete the user in Marten.");
            }
            return IdentityResult.Failed(
                new IdentityError() { Description = "Something went wrong deleting the user." }
            );
        }
    }

    public async Task<TUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        await querySession
            .Query<TUser>()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

    public async Task<TUser?> FindByNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken
    ) =>
        await querySession
            .Query<TUser>()
            .FirstOrDefaultAsync(
                x => x.NormalizedUserName == normalizedUserName,
                cancellationToken
            );

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    // IUserPasswordStore

    public Task SetPasswordHashAsync(
        TUser user,
        string? passwordHash,
        CancellationToken cancellationToken
    )
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(TUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(TUser user, CancellationToken cancellationToken)
    {
        bool hasPassword = !string.IsNullOrEmpty(user.PasswordHash);
        return Task.FromResult(hasPassword);
    }

    // IUserEmailStore

    public Task SetEmailAsync(TUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(TUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(TUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(
        TUser user,
        bool confirmed,
        CancellationToken cancellationToken
    )
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public async Task<TUser?> FindByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken
    ) =>
        await querySession
            .Query<TUser>()
            .FirstOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<string?> GetNormalizedEmailAsync(TUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(
        TUser user,
        string? normalizedEmail,
        CancellationToken cancellationToken
    )
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    // IUserPhoneNumberStore

    public Task SetPhoneNumberAsync(
        TUser user,
        string? phoneNumber,
        CancellationToken cancellationToken
    )
    {
        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    public Task<string?> GetPhoneNumberAsync(TUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PhoneNumber);

    public Task<bool> GetPhoneNumberConfirmedAsync(
        TUser user,
        CancellationToken cancellationToken
    ) => Task.FromResult(user.PhoneNumberConfirmed);

    public Task SetPhoneNumberConfirmedAsync(
        TUser user,
        bool confirmed,
        CancellationToken cancellationToken
    )
    {
        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }

    // IUserTwoFactorStore

    public Task SetTwoFactorEnabledAsync(
        TUser user,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task<bool> GetTwoFactorEnabledAsync(TUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.TwoFactorEnabled);
    }

    // IUserAuthenticatorKeyStore

    public Task SetAuthenticatorKeyAsync(
        TUser user,
        string key,
        CancellationToken cancellationToken
    )
    {
        var protector = dataProtectionProvider.CreateProtector(_userDataProtectionPurpose);

        user.AuthenticatorKey = protector.Protect(key);
        return Task.CompletedTask;
    }

    public Task<string?> GetAuthenticatorKeyAsync(TUser user, CancellationToken cancellationToken)
    {
        var protector = dataProtectionProvider.CreateProtector(_userDataProtectionPurpose);
        return Task.FromResult(
            user.AuthenticatorKey == null ? null : protector.Unprotect(user.AuthenticatorKey)
        );
    }

    // IUserTwoFactorRecoveryCodeStore

    public Task ReplaceCodesAsync(
        TUser user,
        IEnumerable<string> recoveryCodes,
        CancellationToken cancellationToken
    )
    {
        var protector = dataProtectionProvider.CreateProtector(_userDataProtectionPurpose);

        user.RecoveryCodes = protector.Protect(string.Join(';', recoveryCodes));
        return Task.CompletedTask;
    }

    public Task<bool> RedeemCodeAsync(TUser user, string code, CancellationToken cancellationToken)
    {
        var protector = dataProtectionProvider.CreateProtector(_userDataProtectionPurpose);

        var codes = (user.RecoveryCodes == null ? "" : protector.Unprotect(user.RecoveryCodes))
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var idx = codes.FindIndex(c => string.Equals(c, code, StringComparison.Ordinal));
        if (idx >= 0)
        {
            codes.RemoveAt(idx);
            user.RecoveryCodes = protector.Protect(string.Join(";", codes));
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<int> CountCodesAsync(TUser user, CancellationToken cancellationToken)
    {
        var protector = dataProtectionProvider.CreateProtector(_userDataProtectionPurpose);
        var recoveryCodes = (
            user.RecoveryCodes == null ? "" : protector.Unprotect(user.RecoveryCodes)
        );

        var count = string.IsNullOrEmpty(recoveryCodes)
            ? 0
            : recoveryCodes.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;
        return Task.FromResult(count);
    }

    // IUserClaimStore<TUser>

    public async Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken cancellationToken)
    {
        var resolvedUser = await querySession
            .Query<TUser>()
            .FirstOrDefaultAsync(x => x.NormalizedEmail == user.NormalizedEmail, cancellationToken);

        var claimsList = new List<Claim>();
        //if (resolvedUser.RoleClaims != null)
        //{
        //    foreach (string roleClaim in resolvedUser.RoleClaims)
        //    {
        //        claimsList.Add(new Claim(ClaimTypes.Role, roleClaim));
        //    }
        //}

        return claimsList;
    }

    public async Task AddClaimsAsync(
        TUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var session = documentStore.LightweightSession();

            var userRoleClaims = new List<string>();
            foreach (Claim claimItem in claims)
            {
                if (claimItem.Type == ClaimTypes.Role)
                {
                    userRoleClaims.Add(claimItem.Value);
                }
            }

            //user.RoleClaims = userRoleClaims;

            session.Store(user);
            await session.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(
                    ex,
                    "Failed to add claims to the user {Email} in Marten.",
                    user.Email
                );
            }
        }
    }

    public async Task ReplaceClaimAsync(
        TUser user,
        Claim claim,
        Claim newClaim,
        CancellationToken cancellationToken
    )
    {
        if (claim.Type != ClaimTypes.Role || newClaim.Type != ClaimTypes.Role)
        {
            return;
        }

        var existingClaims = await GetClaimsAsync(user, cancellationToken);
        if (existingClaims != null)
        {
            List<Claim> claimsList = [.. existingClaims];
            int index = claimsList.FindIndex(x => x.Value == claim.Value);
            claimsList.RemoveAt(index);
            claimsList.Add(newClaim);

            await AddClaimsAsync(user, claimsList, cancellationToken);
        }
    }

    public async Task RemoveClaimsAsync(
        TUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var existingClaims = await GetClaimsAsync(user, cancellationToken);
            if (existingClaims != null)
            {
                var newClaims = existingClaims.ToList();
                foreach (Claim claimToRemove in claims)
                {
                    int index = newClaims.FindIndex(x =>
                        x.Type == claimToRemove.Type && x.Value == claimToRemove.Value
                    );
                    newClaims.RemoveAt(index);
                }

                await AddClaimsAsync(user, newClaims, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(
                    ex,
                    "Failed to add claims to the user {Email} in Marten.",
                    user.Email
                );
            }
        }
    }

    public async Task<IList<TUser>> GetUsersForClaimAsync(
        Claim claim,
        CancellationToken cancellationToken
    ) => [.. await querySession.Query<TUser>().ToListAsync(cancellationToken)];

    public async Task AddOrUpdatePasskeyAsync(
        TUser user,
        UserPasskeyInfo passkey,
        CancellationToken cancellationToken
    )
    {
        var userId = UserId.Parse(user.Id);
        var credentialId = Convert.ToBase64String(passkey.CredentialId);

        var userEntity =
            await querySession
                .Query<TUser>()
                .FirstOrDefaultAsync(x => x.Id == user.Id, cancellationToken)
            ?? throw new Exception("User not found");

        var isUpdate = userEntity.Passkeys.ContainsKey(credentialId);

        if (isUpdate && userEntity.Passkeys[credentialId].PasskeyInfo.OnlyCountChanged(passkey))
            return;

        using var session = documentStore.LightweightSession();

        session.Events.Append(
            userId.Value,
            isUpdate ? new PasskeyUpdated(userId, passkey) : new PasskeyCreated(userId, passkey)
        );

        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task<IList<UserPasskeyInfo>> GetPasskeysAsync(
        TUser user,
        CancellationToken cancellationToken
    )
    {
        var userId = UserId.Parse(user.Id);

        var userEntity =
            await querySession
                .Query<TUser>()
                .FirstOrDefaultAsync(x => x.Id == user.Id, cancellationToken)
            ?? throw new Exception("User not found");

        return [.. userEntity.Passkeys.Select(kvp => kvp.Value.PasskeyInfo)];
    }

    public async Task<TUser?> FindByPasskeyIdAsync(
        byte[] credentialId,
        CancellationToken cancellationToken
    ) =>
        await querySession
            .Query<TUser>()
            .FirstOrDefaultAsync(
                x => x.Passkeys.Keys.Contains(Convert.ToBase64String(credentialId)),
                cancellationToken
            );

    public async Task<UserPasskeyInfo?> FindPasskeyAsync(
        TUser user,
        byte[] credentialId,
        CancellationToken cancellationToken
    )
    {
        var userEntity =
            await querySession
                .Query<TUser>()
                .FirstOrDefaultAsync(x => x.Id == user.Id, cancellationToken)
            ?? throw new Exception("User not found");

        return userEntity.Passkeys.TryGetValue(
            Convert.ToBase64String(credentialId),
            out var passkey
        )
            ? passkey.PasskeyInfo
            : null;
    }

    public async Task RemovePasskeyAsync(
        TUser user,
        byte[] credentialId,
        CancellationToken cancellationToken
    )
    {
        using var session = documentStore.LightweightSession();
        session.Events.Append(user.UserId.Value, new PasskeyDeleted(user.UserId, credentialId));
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task AddToRoleAsync(
        TUser user,
        string roleName,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name cannot be null or empty.", nameof(roleName));

        var role =
            querySession
                .Query<Role>()
                .FirstOrDefault(role => role.NormalizedName == roleName.ToUpperInvariant())
            ?? throw new InvalidOperationException($"Role '{roleName}' does not exist.");

        using var session = documentStore.LightweightSession();
        session.Events.Append(
            user.StreamId,
            new RoleAssigned(
                user.UserId,
                role.RoleId,
                await currentUserService.GetCurrentUserIdAsync()
            )
        );
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveFromRoleAsync(
        TUser user,
        string roleName,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name cannot be null or empty.", nameof(roleName));

        var role =
            querySession
                .Query<Role>()
                .FirstOrDefault(role => role.NormalizedName == roleName.ToUpperInvariant())
            ?? throw new InvalidOperationException($"Role '{roleName}' does not exist.");

        using var session = documentStore.LightweightSession();
        session.Events.Append(
            user.StreamId,
            new RoleUnassigned(
                user.UserId,
                role.RoleId,
                await currentUserService.GetCurrentUserIdAsync()
            )
        );
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task<IList<string>> GetRolesAsync(TUser user, CancellationToken cancellationToken)
    {
        var result = new List<string>();

        foreach (var userRole in user.Roles)
        {
            var role = await querySession
                .Query<Role>()
                .FirstOrDefaultAsync(r => r.StreamId == userRole.Value);

            if (role?.Name != null)
                result.Add(role.Name);
        }

        return result;
    }

    public async Task<bool> IsInRoleAsync(
        TUser user,
        string roleName,
        CancellationToken cancellationToken
    )
    {
        var role =
            await querySession
                .Query<Role>()
                .FirstOrDefaultAsync(
                    role => role.NormalizedName == roleName.ToUpperInvariant(),
                    cancellationToken
                )
            ?? throw new InvalidOperationException($"Role '{roleName}' does not exist.");

        return user.Roles.Any(r => r == role.RoleId);
    }

    // IUserLockoutStore

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(
        TUser user,
        CancellationToken cancellationToken
    ) => Task.FromResult(user.LockoutEnd);

    public Task SetLockoutEndDateAsync(
        TUser user,
        DateTimeOffset? lockoutEnd,
        CancellationToken cancellationToken
    )
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(
        TUser user,
        CancellationToken cancellationToken
    )
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(TUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(TUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(TUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnabled);

    public Task SetLockoutEnabledAsync(
        TUser user,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    public async Task<IList<TUser>> GetUsersInRoleAsync(
        string roleName,
        CancellationToken cancellationToken
    )
    {
        var roleId =
            querySession
                .Query<Role>()
                .FirstOrDefault(role => role.NormalizedName == roleName.ToUpperInvariant())
                ?.RoleId
            ?? throw new InvalidOperationException($"Role '{roleName}' does not exist.");

        var userIds = (
            await querySession
                .Query<UserRoleAssignment>()
                .Where(userRole => userRole.RoleGuid == roleId)
                .Select(ura => ura.UserGuid)
                .ToListAsync(cancellationToken)
        ).ToArray();

        return
        [
            .. await querySession
                .Query<TUser>()
                .Where(user => userIds.Contains(user.StreamId))
                .ToListAsync(cancellationToken),
        ];
    }
}
