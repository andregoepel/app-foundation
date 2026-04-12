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
AndreGoepel.MembersArea.slnx
│
├── AndreGoepel.MembersArea/               # Blazor Server web application
├── AndreGoepel.Marten.Identity/           # Custom ASP.NET Core Identity stores (event-sourced)
├── AndreGoepel.MembersArea.MailService/   # Email service (Wolverine + MailKit)
├── AndreGoepel.MembersArea.ServiceDefaults/  # Shared Aspire service defaults
├── AndreGoepel.MembersArea.AppHost/       # .NET Aspire orchestration host
│
├── AndreGoepel.MembersArea.Tests/         # Blazor component tests (bUnit)
├── AndreGoepel.Marten.Identity.Tests/     # Identity layer unit tests
└── AndreGoepel.MembersArea.MailService.Tests/  # Mail service unit tests
```

### Project Dependencies

```
AppHost
  └── MembersArea (reference + Aspire resource)

MembersArea
  ├── Marten.Identity
  ├── MailService
  └── ServiceDefaults

Marten.Identity
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
| Document store / event store | Marten 8.28 |
| Messaging | Wolverine 5.27 (with Marten outbox) |
| Email | MailKit 4.15 |
| Observability | OpenTelemetry (traces, metrics, logs) |
| Dev infrastructure | .NET Aspire 13.2 |
| Testing | xUnit, bUnit, NSubstitute |

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
| `IUserClaimStore<TUser>` | Claims (stub) |

Sensitive values (authenticator key, recovery codes) are encrypted at rest with **ASP.NET Core Data Protection**.

### 4.2 Authentication Flows

#### Password Login

Blazor's interactive render mode cannot directly write HTTP cookies. The following pattern bridges this:

1. `LoginForm.razor` (Interactive Server) validates credentials via `UserManager`.
2. On success, a `LoginInfo` entry is stored in `CookieLoginMiddleware.Logins` under a new GUID key.
3. The component navigates to `/login?key={guid}` with `forceLoad: true`.
4. `CookieLoginMiddleware` intercepts the request, retrieves the entry, calls `SignInManager.PasswordSignInAsync`, and redirects to the return URL.

#### Two-Factor Authentication (TOTP)

1. After password success, `SignInResult.RequiresTwoFactor` redirects to `/Account/LoginWith2fa`.
2. The user enters the 6-digit code from their authenticator app.
3. `TwoFactorLoginInfo` is stored in `CookieLoginMiddleware.TwoFactorLogins`, then the page navigates to `/login2fa?key={guid}`.
4. Middleware calls `SignInManager.TwoFactorAuthenticatorSignInAsync`.

#### Recovery Code Login

Same pattern as 2FA, using `CookieLoginMiddleware.RecoveryCodeLogins` and the `/loginrecovery` path.

#### Passkey (WebAuthn)

- `PasskeySubmit.razor` renders a `<passkey-submit>` custom element backed by a JavaScript module.
- The JS initiates the WebAuthn ceremony, writes the credential JSON to a hidden form field, and submits.
- `Login.razor` (SSR) handles the POST via `SignInManager.PasskeySignInAsync`.
- Passkeys are stored as `Dictionary<string, UserPasskey>` on the `User` document, keyed by Base64-encoded credential ID.

### 4.3 CookieLoginMiddleware

`Components/Account/CookieLoginMiddleware.cs`

```csharp
// In-memory credential stores (keyed by one-time GUID)
public static ConcurrentDictionary<Guid, LoginInfo>           Logins
public static ConcurrentDictionary<Guid, TwoFactorLoginInfo>  TwoFactorLogins
public static ConcurrentDictionary<Guid, RecoveryCodeLoginInfo> RecoveryCodeLogins
```

The middleware handles three paths:

| Path | Dictionary | SignInManager method |
|---|---|---|
| `/login` | `Logins` | `PasswordSignInAsync` |
| `/login2fa` | `TwoFactorLogins` | `TwoFactorAuthenticatorSignInAsync` |
| `/loginrecovery` | `RecoveryCodeLogins` | `TwoFactorRecoveryCodeSignInAsync` |

After sign-in, the entry is removed and the user is redirected to `ReturnUrl` or `/`.

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
| `UserLogin` | Successful sign-in |
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
| `members-area-database` | PostgreSQL container | 5432 |
| `mailhog` | Docker container (`mailhog/mailhog`) | 1025 (SMTP), 8025 (Web UI) |
| `members-area` | Project reference | — |

### AppHost.cs

```csharp
var postgres = builder.AddPostgres("members-area-database")
    .WithDataVolume();

var mailhog = builder.AddContainer("mailhog", "mailhog/mailhog")
    .WithHttpEndpoint(port: 8025, targetPort: 8025)
    .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp");

builder.AddProject<Projects.AndreGoepel_MembersArea>("members-area")
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
    "members-area-database": "Host=...;Port=...;Username=...;Password=...;Database=..."
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
| `MembersArea.Tests` | xUnit + bUnit + NSubstitute | Blazor component behaviour |
| `Marten.Identity.Tests` | xUnit | Domain model & projection unit tests |
| `MailService.Tests` | xUnit + NSubstitute | Mail handler & SMTP sender unit tests |

### Marten.Identity Test Coverage

**UserId / RoleId** — value semantics, `New()`, `Parse()`, implicit/explicit conversions, equality.

**UserProjection** — applies each domain event in isolation and asserts the resulting `User` document:
- `UserCreated`: all fields set, email normalised, audit populated
- `UserUpdated`: each mutable field (email, password hash, 2FA, lockout, …) updated independently
- `UserDeleted`: sensitive fields cleared, soft-delete flag set, deleted audit populated
- `PasskeyCreated` / `PasskeyUpdated` / `PasskeyDeleted`: dictionary mutations
- `RoleAssigned` / `RoleUnassigned`: set mutations, duplicate handling

**UserPasskey** — equality comparison, Base64 credential ID encoding.

**RoleProjection** — mirrors `UserProjection` tests for `Role` documents.

### Test File Naming Convention

Test files follow the `Subject.Tests.cs` convention (dot-separated):

```
UserId.Tests.cs               ✓
UserProjection.Tests.cs       ✓
SendEmailMessageHandler.Tests.cs  ✓
```

### Test Structure

Every test uses `// Arrange`, `// Act`, `// Assert` section comments. When setup and execution are inseparable (e.g. a single `Render<>()` call), they are combined as `// Arrange / Act`. The `// Arrange` section is omitted when there is no meaningful setup.
