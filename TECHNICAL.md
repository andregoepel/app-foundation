# Technical Documentation — Members Area

## Table of Contents

1. [Solution Structure](#1-solution-structure)
2. [Technology Stack](#2-technology-stack)
3. [Architecture Overview](#3-architecture-overview)
4. [Identity & Authentication](#4-identity--authentication)
5. [Event-Sourced User Model](#5-event-sourced-user-model)
6. [Mail Service](#6-mail-service)
7. [Application Host & Infrastructure](#7-application-host--infrastructure)
8. [UI & Pages](#8-ui--pages)
9. [Configuration](#9-configuration)
10. [Testing](#10-testing)

---

## 1. Solution Structure

```
AndreGoepel.AppFoundation.slnx   (at the repo root)
│
├── AndreGoepel.AppFoundation/              # Blazor Server web application
├── AndreGoepel.Marten.Identity.Abstractions/ # Framework-light contracts (events, IDs, ICurrentUserService)
├── AndreGoepel.Marten.Identity/            # Custom ASP.NET Core Identity stores (event-sourced)
├── AndreGoepel.Marten.Identity.Blazor/     # Blazor UI components for identity flows (login, 2FA, passkeys, admin)
├── AndreGoepel.Website/                    # Portfolio site Razor class library
├── AndreGoepel.AppFoundation.MailService/  # Email service (Wolverine + MailKit)
├── AndreGoepel.AppFoundation.ServiceDefaults/ # Shared Aspire service defaults
├── AndreGoepel.AppFoundation.AppHost/      # .NET Aspire orchestration host
│
├── AndreGoepel.AppFoundation.Tests/                # Host bUnit + IdentityEmailSender
├── AndreGoepel.Marten.Identity.Tests/              # Substitute-based unit tests
├── AndreGoepel.Marten.Identity.Blazor.Tests/       # bUnit tests for identity UI
├── AndreGoepel.Marten.Identity.IntegrationTests/   # Real-Marten tests via Testcontainers
├── AndreGoepel.AppFoundation.MailService.Tests/    # Mail handler + DI validation
└── AndreGoepel.Website.Tests/                      # SiteStateService unit tests
```

### Project Dependencies

```
AppHost
  └── AppFoundation (reference + Aspire resource)

AppFoundation
  ├── Marten.Identity.Blazor
  ├── Website
  ├── MailService
  └── ServiceDefaults

Marten.Identity.Blazor
  └── Marten.Identity

Marten.Identity
  ├── Marten.Identity.Abstractions
  └── Marten.AspNetCore

MailService
  └── WolverineFx.Marten
```

---

## 2. Technology Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 |
| Web framework | ASP.NET Core / Blazor Server |
| UI components | Radzen Blazor 10.2 (Material3 theme) |
| Database | PostgreSQL |
| Document store / event store | Marten 9.0 |
| Messaging | Wolverine 6.0 (with Marten outbox) |
| Email | MailKit 4.16 |
| Observability | OpenTelemetry (traces, metrics, logs) |
| Dev infrastructure | .NET Aspire 13.3 |
| Testing | xUnit v3, bUnit, NSubstitute, Testcontainers (Postgres) |

---

## 3. Architecture Overview

The application is a Blazor Server single-page application backed by PostgreSQL. All state — users, roles, credentials — is modelled as an **event-sourced document store** using Marten. Projections rebuild read-model documents from streams of domain events.

Authentication uses **ASP.NET Core Identity** with fully custom stores that delegate to Marten instead of Entity Framework. Cookie sign-in is handled by a dedicated middleware layer to bridge the gap between Blazor's interactive rendering model and the HTTP cookie pipeline.

Email delivery is decoupled from the web application via a **Wolverine message bus** with a durable Marten outbox, ensuring emails are not lost if the SMTP server is temporarily unavailable.

```
Browser
  │ HTTPS
  ▼
ASP.NET Core pipeline
  ├── CookieLoginMiddleware   (intercepts /login, /login2fa, /loginrecovery)
  ├── Blazor Server (Interactive Server render mode)
  │     ├── Account pages     (Login, Register, 2FA, Passkeys, …)
  │     ├── Administration    (Users, Roles)
  │     └── Main pages        (Home, Setup, …)
  └── Identity endpoints

Identity layer (Marten stores)
  │
  ▼
PostgreSQL  ◄──  Marten event streams + projections

Wolverine message bus  ──►  SmtpEmailSender  ──►  SMTP server
```

---

## 4. Identity & Authentication

### 4.1 ASP.NET Core Identity Configuration

Identity is registered in `Program.cs` with the following custom stores from `AndreGoepel.Marten.Identity`:

```csharp
builder.Services
    .AddIdentityCore<User>()
    .AddRoles<Role>()
    .AddUserStore<UserStore<User>>()
    .AddRoleStore<RoleStore<Role>>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
```

`UserStore<TUser>` implements eleven Identity interfaces:

| Interface | Responsibility |
|---|---|
| `IUserStore<TUser>` | CRUD, username/ID lookup |
| `IUserPasswordStore<TUser>` | Password hash storage |
| `IUserEmailStore<TUser>` | Email address management |
| `IUserTwoFactorStore<TUser>` | Enable/disable 2FA flag |
| `IUserAuthenticatorKeyStore<TUser>` | Encrypted TOTP secret |
| `IUserTwoFactorRecoveryCodeStore<TUser>` | Encrypted recovery codes |
| `IUserPasskeyStore<TUser>` | WebAuthn credential management |
| `IUserRoleStore<TUser>` | Role assignment |
| `IUserLockoutStore<TUser>` | Account lockout tracking |
| `IQueryableUserStore<TUser>` | LINQ over Marten projections |

Sensitive values (authenticator key, recovery codes) are encrypted at rest with **ASP.NET Core Data Protection**.

### 4.2 Authentication Flows

#### Password Login

Blazor's interactive render mode cannot directly write HTTP cookies. The following pattern bridges this:

1. `LoginForm.razor` (Interactive Server) validates credentials via `UserManager`.
2. On success, the credentials are serialised and protected with `LoginTokenProtector` (an `ITimeLimitedDataProtector` wrapper with a 2-minute TTL).
3. The component navigates to `/login?token={protected}` with `forceLoad: true`.
4. `CookieLoginMiddleware` intercepts the request, unprotects the token, calls `SignInManager.PasswordSignInAsync`, and redirects to the return URL. Expired or tampered tokens redirect back to `/Account/Login`.

#### Two-Factor Authentication (TOTP)

1. After password success, `SignInResult.RequiresTwoFactor` redirects to `/Account/LoginWith2fa`.
2. The user enters the 6-digit code from their authenticator app.
3. A `TwoFactorLoginInfo` is protected via `LoginTokenProtector`, then the page navigates to `/login2fa?token={protected}`.
4. Middleware calls `SignInManager.TwoFactorAuthenticatorSignInAsync`.

#### Recovery Code Login

Same pattern as 2FA, using a protected `RecoveryCodeLoginInfo` token and the `/loginrecovery` path.

#### Passkey (WebAuthn)

- `PasskeySubmit.razor` renders a `<passkey-submit>` custom element backed by a JavaScript module.
- The JS initiates the WebAuthn ceremony, writes the credential JSON to a hidden form field, and submits.
- `Login.razor` (SSR) handles the POST via `SignInManager.PasskeySignInAsync`.
- Passkeys are stored as `Dictionary<string, UserPasskey>` on the `User` document, keyed by Base64-encoded credential ID.

### 4.3 CookieLoginMiddleware

`AndreGoepel.Marten.Identity/Http/CookieLoginMiddleware.cs`

The handoff payload (LoginInfo / TwoFactorLoginInfo / RecoveryCodeLoginInfo) is serialised and protected by `LoginTokenProtector` — a wrapper around `ITimeLimitedDataProtector` with a 2-minute TTL — into a URL-safe token. The token travels as `?token=...` on the next request; the middleware unprotects it and feeds it to `SignInManager`.

| Path | Payload | SignInManager method |
|---|---|---|
| `/login` | `LoginInfo` | `PasswordSignInAsync` |
| `/login2fa` | `TwoFactorLoginInfo` | `TwoFactorAuthenticatorSignInAsync` |
| `/loginrecovery` | `RecoveryCodeLoginInfo` | `TwoFactorRecoveryCodeSignInAsync` |

Expired or tampered tokens fall through to `/Account/Login`. There is no process-wide state: a leaked token stops working in two minutes, and the design is multi-instance safe (the data-protection key ring is shared across instances by ASP.NET Core).

---

## 5. Event-Sourced User Model

### 5.1 User

`AndreGoepel.Marten.Identity/Users/User.cs` — extends `IdentityUser`

| Property | Type | Notes |
|---|---|---|
| `UserId` | `UserId` | Strongly-typed domain ID (wraps `Guid`) |
| `StreamId` | `Guid` | Marten event stream identifier |
| `Passkeys` | `Dictionary<string, UserPasskey>` | WebAuthn credentials |
| `Roles` | `HashSet<RoleId>` | Assigned roles |
| `AuthenticatorKey` | `string?` | Encrypted TOTP secret |
| `RecoveryCodes` | `string?` | Encrypted semicolon-delimited codes |
| `RootUser` | `bool` | Cannot be deleted; protected admin account |
| `Deletable` | `bool` | Allows soft delete via UI |
| `Deleted` | `bool` | Soft-delete flag |
| `CreatedBy/At`, `ChangedBy/At`, `DeletedBy/At` | — | Full audit trail |

### 5.2 Domain Events

**User events:**

| Event | Triggered when |
|---|---|
| `UserCreated` | New user registered |
| `UserUpdated` | Profile, password, 2FA, lockout fields change |
| `UserDeleted` | Account soft-deleted |
| `UserRestored` | Soft-deleted account brought back |
| `PasskeyCreated` | New WebAuthn credential registered |
| `PasskeyUpdated` | Passkey renamed |
| `PasskeyDeleted` | Passkey removed |
| `RoleAssigned` | Role granted to user |
| `RoleUnassigned` | Role revoked from user |

**Role events:** `RoleCreated`, `RoleChanged`, `RoleDeleted`

### 5.3 Projections

`UserProjection` is a Marten **single-stream projection** (`SingleStreamProjection<User, Guid>`). Each user has one event stream; the projection applies events in order to produce the current `User` document. The same pattern applies to `RoleProjection`.

Marten is configured with `InitializeIdentity(StoreOptions)`:

```csharp
options.InitializeUsersStore();   // UserProjection + event type registrations
options.InitializeRolesStore();   // RoleProjection + event type registrations
options.InitializeUserRolesStore(); // UserRoleAssignment projection
```

### 5.4 Strongly-Typed IDs

Both `UserId` and `RoleId` are `readonly record struct` wrappers around `Guid`:

```csharp
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());
    public static UserId Parse(string value) => new(Guid.Parse(value));

    public static implicit operator Guid(UserId id) => id.Value;
    public static explicit operator UserId(Guid value) => new(value);
}
```

---

## 6. Mail Service

### Architecture

Email sending is fully decoupled from the web process:

```
IdentityEmailSender
  └── IMessageBus.SendAsync(MailMessage)    [Wolverine]
        └── SendEmailMessageHandler
              └── IEmailSender.SendAsync(MailMessage)
                    └── SmtpEmailSender     [MailKit]
                          └── SMTP server
```

Wolverine uses a Marten-backed **durable outbox**: messages are persisted to PostgreSQL before SMTP delivery, guaranteeing at-least-once delivery even if the application restarts mid-flight.

### MailMessage

```csharp
public record MailMessage(
    string To,
    string Subject,
    string Body
);
```

### Configuration

Bound from the `EmailSender` section of `appsettings.json`:

```json
{
  "EmailSender": {
    "SenderName": "",
    "SenderEmail": "",
    "Server": "",
    "Port": 587,
    "UseSsl": false,
    "Username": "",
    "Password": "",
    "Html": true
  }
}
```

In development, **MailHog** is used as the SMTP server (see §7).

---

## 7. Application Host & Infrastructure

`AndreGoepel.MembersArea.AppHost` orchestrates all infrastructure using **.NET Aspire**.

### Resources

| Resource | Type | Ports |
|---|---|---|
| `app-foundation-database` | PostgreSQL container | 5432 |
| `mailhog` | Docker container (`mailhog/mailhog`) | 1025 (SMTP), 8025 (Web UI) |
| `app-foundation` | Project reference | — |

### AppHost.cs

```csharp
var postgres = builder.AddPostgres("app-foundation-database")
    .WithDataVolume();

var mailhog = builder.AddContainer("mailhog", "mailhog/mailhog")
    .WithHttpEndpoint(port: 8025, targetPort: 8025)
    .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp");

builder.AddProject<Projects.AndreGoepel_MembersArea>("app-foundation")
    .WithReference(postgres)
    .WithEnvironment("EmailSender__Server", mailhog.GetEndpoint("smtp"));
```

### Service Defaults

`AndreGoepel.MembersArea.ServiceDefaults` registers standard Aspire extensions:

- OpenTelemetry (traces, metrics, logs) with OTLP exporter
- HTTP client resilience (retries, circuit breaker)
- Service discovery

---

## 8. UI & Pages

### Layouts

| Layout | Used by |
|---|---|
| `MainLayout` | Authenticated pages (sidebar nav + top bar) |
| `LoginLayout` | All account/auth pages (full-screen split card) |

`LoginLayout` renders a full-viewport centered container. Pages using it render a `RadzenCard` with two panels:

- **Left panel** — Brand panel with primary colour background, logo, site name, and a page-specific subtitle
- **Right panel** — Page title, form fields, and action buttons

### Account Pages

| Page | Route | Notes |
|---|---|---|
| Login | `/Account/Login` | SSR; passkey + password; `LoginForm` island |
| Register | `/Account/Register` | Interactive Server |
| Forgot password | `/Account/ForgotPassword` | Interactive Server |
| Resend confirmation | `/Account/ResendEmailConfirmation` | Interactive Server |
| Two-factor auth | `/Account/LoginWith2fa` | Interactive Server |
| Recovery code | `/Account/LoginWithRecoveryCode` | Interactive Server |
| Reset password | `/Account/ResetPassword` | Interactive Server |
| Confirm email | `/Account/ConfirmEmail` | SSR |

All account pages use `[ExcludeFromInteractiveRouting]` (via `_Imports.razor`) except those that declare `@rendermode InteractiveServer` themselves.

### Manage Pages

Located under `Components/Account/Pages/Manage/`:
Profile, Email, ChangePassword, SetPassword, TwoFactorAuthentication, EnableAuthenticator, Disable2fa, ResetAuthenticator, GenerateRecoveryCodes, Passkeys, PasskeyCreate, RenamePasskey, PersonalData, DeletePersonalData.

### Administration Pages

Located under `Components/Administration/`:

- `Users.razor` — User list with role assignment
- `Roles.razor` — Role list with user assignment
- Dialogs: `UserRoleDialog`, `RoleUserDialog`, `NewRole`

### Authorization

All pages are protected by `AuthorizeRouteView` in `Routes.razor`. Unauthenticated access redirects to `Account/Login` via `RedirectToLogin.razor`. Individual pages use `@attribute [Authorize]` where needed. `Microsoft.AspNetCore.Authorization` is globally imported via `Components/_Imports.razor`.

---

## 9. Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "app-foundation-database": "Host=...;Port=...;Username=...;Password=...;Database=..."
  },
  "EmailSender": {
    "SenderName": "",
    "SenderEmail": "",
    "Server": "",
    "Port": 587,
    "UseSsl": false,
    "Username": "",
    "Password": "",
    "Html": true
  }
}
```

In development the database connection string and `EmailSender__Server` are overridden by Aspire environment variable injection.

### Middleware Pipeline Order

```csharp
app.UseExceptionHandler("/Error");
app.UseHsts();
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.UseMiddleware<CookieLoginMiddleware>();
app.MapAdditionalIdentityEndpoints();
```

---

## 10. Testing

### Test Projects

| Project | Framework | Scope |
|---|---|---|
| `Marten.Identity.Tests` | xUnit v3 + NSubstitute | Substitute-based unit tests: ID semantics, projections, `UserExtension.AreEqual`, `RoleStore` (event-emitting paths), `CurrentUserService`, `CookieLoginMiddleware` path handling, `LoginTokenProtector` round-trip |
| `Marten.Identity.IntegrationTests` | xUnit v3 + Testcontainers (Postgres) + NSubstitute | Real-Marten coverage: `UserStore` / `RoleStore` CRUD + projections, `UserRoleAssignmentProjection`, `DeletedUserCleanupJob`, `CleanupSettingsService`, `SetupRedirectMiddleware` |
| `Marten.Identity.Blazor.Tests` | xUnit v3 + bUnit + NSubstitute | Blazor component behaviour: static account pages, interactive login/register/forgot-password forms, Manage/Profile, Manage/ChangePassword, shared bits |
| `AppFoundation.Tests` | xUnit v3 + bUnit + NSubstitute | Host wiring: `IdentityEmailSender` (Wolverine handoff) |
| `AppFoundation.MailService.Tests` | xUnit v3 + NSubstitute | `SmtpEmailSender`, `SendEmailMessageHandler`, `InitializerExtension` (DI + data-annotation validation) |
| `Website.Tests` | xUnit v3 | `SiteStateService.OnChange` firing rules |

`MartenFixture` (in `IntegrationTests/Infrastructure/`) spins a `PostgreSqlContainer` once per collection and exposes an `IDocumentStore` configured the same way `Program.cs` does. Tests inherit `IAsyncLifetime` and call `fixture.ResetAsync()` in `InitializeAsync` to wipe documents and event streams between cases.

### Marten.Identity unit-test coverage

**UserId / RoleId** — value semantics, `New()`, `Parse()`, implicit/explicit conversions, equality.

**UserProjection / RoleProjection** — each domain event applied in isolation; asserts the resulting document state (fields, audit, soft-delete, passkey dict, role set).

**UserExtension.AreEqual** — every persisted field is covered, including the lockout fields and `Deletable` that were missing pre-audit.

**CurrentUserService** — `ClaimTypes.NameIdentifier` happy path, unauthenticated principal, wrong-claim, malformed-guid, empty value.

**RoleStore** (substitute-based) — `CreateAsync` id round-trip, `UpdateAsync` preserves `Deletable`, `DeleteAsync` / `RestoreAsync` go through the event stream only.

**CookieLoginMiddleware** — all three paths, success / 2FA-required / locked-out / failed branches, code stripping, unknown-token fallbacks.

### Integration test coverage

**UserStore** — Create / Update (with AreEqual short-circuit), Delete / Restore replay, role assign/unassign, passkey CRUD, recovery codes round-trip, authenticator key data-protection round-trip.

**RoleStore** — CRUD against the real store, Restore clears Deleted/DeletedAt.

**UserRoleAssignmentProjection** — assign / unassign / idempotency.

**DeletedUserCleanupJob** — retention cutoff (purges aged, keeps recent).

**CleanupSettingsService** — defaults fallback, persistence, scheduler reschedule.

**SetupRedirectMiddleware** — unconfigured → /Setup redirect, configured → pass-through, static asset / setup-path bypass.

### Test File Naming Convention

Test files follow the `Subject.Tests.cs` convention (dot-separated):

```
UserId.Tests.cs               ✓
UserProjection.Tests.cs       ✓
SendEmailMessageHandler.Tests.cs  ✓
```

### Test Structure

Every test uses `// Arrange`, `// Act`, `// Assert` section comments. When setup and execution are inseparable (e.g. a single `Render<>()` call), they are combined as `// Arrange / Act`. The `// Arrange` section is omitted when there is no meaningful setup.
