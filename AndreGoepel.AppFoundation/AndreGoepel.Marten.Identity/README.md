# AndreGoepel.Marten.Identity

ASP.NET Core Identity stores backed by [Marten](https://martendb.io/) (PostgreSQL). Provides event-sourced user and role stores, cookie login middleware, and DI extensions ready to drop into any ASP.NET Core application.

## Requirements

- .NET 10
- Marten 8.x (`Marten.AspNetCore`)
- PostgreSQL

## Installation

```
dotnet add package AndreGoepel.Marten.Identity
```

## Usage

### 1. Configure Marten and wire up Identity stores

```csharp
builder.Services.AddMartenIdentity();

builder.Services.AddMarten(options =>
{
    options.Connection(connectionString);
    options.InitializeIdentity();   // registers user, role, and user-role projections
    options.AutoCreateSchemaObjects = AutoCreate.All;
})
.IntegrateWithWolverine(); // optional — only needed when using Wolverine
```

`AddMartenIdentity` accepts an optional `Action<IdentityOptions>` to customise password rules, lockout policy, etc.

### 2. Add middleware

Call `UseMartenIdentityMiddleware()` after `UseAuthentication()` / `UseAuthorization()`:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseMartenIdentityMiddleware();
```

This registers two middleware components:

| Middleware | Purpose |
|---|---|
| `SetupRedirectMiddleware` | Redirects to `/Setup` when no roles exist yet |
| `CookieLoginMiddleware` | Exchanges a one-time login key for an authentication cookie (used by the setup flow) |

## What's included

| Namespace | Contents |
|---|---|
| `AndreGoepel.Marten.Identity.Users` | `User`, `UserId`, `UserStore`, event-sourced `UserProjection`, passkey support |
| `AndreGoepel.Marten.Identity.Roles` | `Role`, `RoleId`, `RoleStore`, event-sourced `RoleProjection`, built-in `Roles` constants |
| `AndreGoepel.Marten.Identity.UserRoles` | `UserRoleAssignment` projection for efficient role queries |
| `AndreGoepel.Marten.Identity.Http` | `SetupRedirectMiddleware`, `CookieLoginMiddleware` |
| `AndreGoepel.Marten.Identity.Services` | `ICurrentUserService` / `CurrentUserService` |

## License

MIT
