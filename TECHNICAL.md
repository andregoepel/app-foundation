# Technical Documentation — app-foundation

`app-foundation` is a set of reusable NuGet packages providing the backend wiring and a
Blazor management frontend for .NET 10 apps. Identity is delegated to the companion
[marten-identity](https://github.com/andregoepel/marten-identity) packages; a host app
(e.g. [andregoepel.dev](https://github.com/andregoepel/andregoepel-dev)) composes these
packages into a runnable application.

## Table of contents

1. [Solution structure](#1-solution-structure)
2. [Technology stack](#2-technology-stack)
3. [The hosting seam](#3-the-hosting-seam)
4. [Management frontend](#4-management-frontend)
5. [Mail service](#5-mail-service)
6. [Service defaults](#6-service-defaults)
7. [Configuration](#7-configuration)
8. [Identity (delegated)](#8-identity-delegated)
9. [Testing](#9-testing)
10. [Build, packaging & release](#10-build-packaging--release)

---

## 1. Solution structure

```
src/
  AndreGoepel.AppFoundation/             # Razor Class Library — management frontend
  AndreGoepel.AppFoundation.Hosting/     # umbrella backend seam (AddAppFoundation/UseAppFoundation)
  AndreGoepel.AppFoundation.MailService/ # Wolverine + MailKit email
  AndreGoepel.AppFoundation.ServiceDefaults/ # .NET Aspire service defaults
tests/
  AndreGoepel.AppFoundation.Tests/
  AndreGoepel.AppFoundation.MailService.Tests/
```

### Package dependencies

```
Hosting (umbrella)
  ├── AppFoundation          (management UI)
  ├── MailService
  ├── ServiceDefaults
  └── AndreGoepel.Marten.Identity.Blazor   (NuGet, separate repo)

AppFoundation
  ├── AndreGoepel.Marten.Identity.Blazor
  ├── Marten
  └── Radzen.Blazor

MailService
  ├── WolverineFx.Marten
  ├── MailKit
  └── Marten
```

A host references `AndreGoepel.AppFoundation.Hosting` (transitively pulls everything) and
usually `AndreGoepel.AppFoundation` directly because it renders the UI components.

---

## 2. Technology stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 |
| Web / UI | ASP.NET Core, Blazor (Interactive Server), Radzen Blazor |
| Data / events | Marten (PostgreSQL document + event store) |
| Messaging | Wolverine (Marten-backed durable outbox) |
| Email | MailKit |
| Identity | ASP.NET Core Identity, event-sourced (marten-identity) |
| Observability | OpenTelemetry (traces, metrics, logs) |
| Orchestration | .NET Aspire (in the host) |
| Testing | xUnit v3, bUnit, NSubstitute |

---

## 3. The hosting seam

`AndreGoepel.AppFoundation.Hosting` reduces a host's `Program.cs` to two calls plus its own
UI mapping. `src/AndreGoepel.AppFoundation.Hosting/Initialization.cs`.

### `AddAppFoundation(this WebApplicationBuilder, Action<AppFoundationOptions>?)`

In order:

1. Build `AppFoundationOptions` from the optional `configure` delegate.
2. **Secrets** — `AddKeyPerFile(options.SecretsDirectory, optional: true)` (from 1.1.0; see §7).
3. `AddServiceDefaults()` (§6).
4. `AddMartenIdentity()` / `AddMartenIdentityBlazor()` / `AddMartenIdentityCleanup()`.
5. Resolve the connection string (`options.DatabaseConnectionName`, throws if missing).
6. Register `IEmailSender<TUser>` → `IdentityEmailSender` (§5).
7. `AddMarten(...)` with `InitializeIdentity()` + `AutoCreate.All`, `IntegrateWithWolverine()`.
8. Memory cache, `IHttpContextAccessor`, Radzen `NotificationService`.
9. `UseWolverine(...)` — durable inbox/outbox on all endpoints, handler discovery of the MailService assembly, service name `options.WolverineServiceName`.
10. `AddEmailService()` (§5), DataProtection with a Marten-persisted key ring and optional
    certificate encryption at rest (§7), `AddRadzenComponents()`, `AddHeaderPropagation()`.

### `UseAppFoundation(this WebApplication)`

The shared request pipeline: `MapDefaultEndpoints()`, forwarded headers (`X-Forwarded-For`/`-Proto`,
known proxies/networks cleared), exception handler + HSTS (non-development), HTTPS redirection,
static files, header propagation, antiforgery, authentication, authorization, then
`UseMartenIdentityMiddleware()`.

### What the host keeps

The host owns rendering: `AddRazorComponents().AddInteractiveServerComponents()`,
`MapStaticAssets()`, `MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(...)`,
and `MapAdditionalIdentityEndpoints()` — plus its own root `App.razor` and `Routes.razor`.

### `AppFoundationOptions`

| Option | Default | Purpose |
|---|---|---|
| `DatabaseConnectionName` | `appfoundation-database` | Connection-string name |
| `WolverineServiceName` | `AppFoundation` | Durable inbox/outbox service name |
| `SecretsDirectory` | `/run/secrets` | Key-per-file secrets directory (from 1.1.0) |
| `DataProtectionApplicationDiscriminator` | `null` (→ `WolverineServiceName`) | DataProtection app isolation (from 1.1.0) |
| `ConfigureDataProtection` | `null` | Extension point on `IDataProtectionBuilder`, e.g. Key Vault or certificate rotation (from 1.1.0) |
| `ConfigureWolverine` | `null` | Extension point on `WolverineOptions`, invoked inside the foundation's `UseWolverine`; opt handler assemblies into discovery, e.g. `w => w.Discovery.IncludeAssembly(typeof(SomeHandler).Assembly)` (from 1.1.2) |

`IdentityEmailSender` (internal) bridges ASP.NET Identity's `IEmailSender<TUser>` to a
Wolverine `MailMessage` published via `IMessageBus`, decoupling email from the request.

---

## 4. Management frontend

`AndreGoepel.AppFoundation` is a Razor Class Library providing the authenticated management
shell. The host's router uses it as the default layout and includes its assembly so its
routable pages are discovered.

| Component | Role |
|---|---|
| `Layout/MainLayout` | Radzen layout: header (profile menu + logout), sidebar (brand, `NavMenu`, copyright), body |
| `Layout/NavMenu` | Setup-gated menu: authenticated **Account** submenu; **Administrator** section with an injectable admin slot + Users / Roles / User Cleanup |
| `Layout/EmptyLayout`, `Layout/ReconnectModal` | Bare layout for setup/errors; Blazor reconnect UI |
| `Pages/Home` | Dashboard landing (`/dashboard`) |
| `Pages/Setup` | First-run setup page |
| `Administration/Pages/EmailSettingsPage` | Admin-only email settings editor (`/Administration/EmailSettings`, from 1.1.0; §5) |
| `Pages/Error`, `Pages/NotFound`, `Shared/ErrorPage` | Generic error surfaces |

### Branding & extension — `AppFoundationLayoutOptions`

| Property | Purpose |
|---|---|
| `BrandName` | Sidebar brand text |
| `LogoPath` | Brand logo (default `favicon.png`) |
| `Copyright` | Sidebar footer (hidden when empty) |
| `AdminMenu` | A Razor component **type** rendered via `DynamicComponent` inside the administrator menu — how a host injects its own admin entries |

The host configures these with `services.Configure<AppFoundationLayoutOptions>(...)`. The
account/administration *identity* pages come from `AndreGoepel.Marten.Identity.Blazor`.

---

## 5. Mail service

Email is decoupled from the web request via Wolverine with a Marten-backed durable outbox:

```
IdentityEmailSender (Hosting)
  └── IMessageBus.SendAsync(MailMessage)          [Wolverine, durable outbox]
        └── SendEmailMessageHandler               [WolverineHandler]
              └── IEmailSender.SendAsync(...)
                    └── SmtpEmailSender            [MailKit] → SMTP
```

- `MailMessage(string Recipient, string Subject, string Body)` — the message contract.
- `MailConfiguration` (bound from the `EmailSender` config section, data-annotation
  validated): `SenderName`, `SenderEmail`, `Server`, `Port` (587), `UseSsl`, `Username`,
  `Password`, `Html` (true).
- `AddEmailService()` binds + validates the config and registers `SmtpEmailSender`,
  `IMailSettingsProvider` and `IEmailSettingsStore`.

Persisting the message to PostgreSQL before SMTP delivery guarantees at-least-once delivery
across restarts.

### Database-backed settings (from 1.1.0)

Email settings live in Postgres (`EmailSettingsDocument`, single record) and are editable at
runtime via the admin-only **Email Settings** page (`/Administration/EmailSettings`,
`Administrator` role). Resolution is database-first, per send — saves take effect without a
restart:

- No database record → the `EmailSender` configuration section applies (bootstrap path,
  identical to pre-1.1.0 behaviour). The admin page pre-fills from it and flags the source.
- The SMTP password is stored DataProtection-protected
  (purpose `AndreGoepel.AppFoundation.MailService.EmailSettings`) and never rendered back to
  the UI: leaving the password field blank keeps the current one; the first save without input
  falls back to the configured password.
- `IEmailSettingsStore` (public) is the load/save seam for UIs; the page also offers a
  "send test email" action using the last saved settings.

---

## 6. Service defaults

`AndreGoepel.AppFoundation.ServiceDefaults` (`AddServiceDefaults` / `MapDefaultEndpoints`):

- **OpenTelemetry** — logging, metrics (ASP.NET Core, HTTP client, runtime), tracing; OTLP
  exporter enabled when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- **Health checks** — `/health` (all) and `/alive` (liveness), mapped in Development only.
- **HTTP resilience** (standard handler) and **service discovery** on outbound `HttpClient`s.

---

## 7. Configuration

| Source | Keys |
|---|---|
| Connection string | `ConnectionStrings:appfoundation-database` (name configurable) |
| Email | `EmailSender:*` → `MailConfiguration` |
| Options (code) | `AppFoundationOptions`, `AppFoundationLayoutOptions` |

### Docker secrets (from 1.1.0)

`AddAppFoundation` loads `SecretsDirectory` (default `/run/secrets`) as key-per-file
configuration (`optional: true`, so it's a no-op locally). A secret file named
`ConnectionStrings__appfoundation-database` (content = the connection string; `__` maps to
the `:` section separator) is read straight into configuration and never enters the process
environment.

```yaml
secrets:
  ConnectionStrings__appfoundation-database:
    file: ./secrets/connectionstring   # chmod 600; or external: true under Swarm
```

### Data protection keys (from 1.1.0)

The key ring is persisted in Postgres as Marten documents (`DataProtectionKeyDocument`,
table `mt_doc_dataprotectionkeydocument`), so keys — and with them login cookies and all
`IDataProtector`-encrypted payloads — survive container rebuilds and are shared across
instances.

When `DataProtection__CertificatePath` / `DataProtection__CertificatePassword` are configured
(e.g. as key-per-file secrets), key ring entries are additionally encrypted with the X.509
certificate before being written, so a database dump alone cannot decrypt protected payloads.
Keep the PFX (and its password) backed up separately from database backups — a lost
certificate makes the key ring, and everything encrypted with it, unrecoverable. Certificate
expiry only stops *new* keys from being encrypted; decryption keeps working.

The application discriminator defaults to `WolverineServiceName`
(`DataProtectionApplicationDiscriminator` overrides). For host-specific key protection
(Azure Key Vault, DPAPI) or certificate rotation (`UnprotectKeysWithAnyCertificate`), use
`AppFoundationOptions.ConfigureDataProtection`.

---

## 8. Identity (delegated)

Authentication and user/role storage are provided by
[AndreGoepel.Marten.Identity.Blazor](https://github.com/andregoepel/marten-identity) (and its
dependencies). `app-foundation` only *wires* it (`AddMartenIdentity*`,
`UseMartenIdentityMiddleware`, `MapAdditionalIdentityEndpoints`) and surfaces its account and
administration pages through the management shell.

For identity internals — the event-sourced `UserStore`/`RoleStore`, projections, the
cookie-login handoff middleware, passkeys/2FA/recovery codes — see the marten-identity repo.

---

## 9. Testing

| Project | Scope |
|---|---|
| `AppFoundation.Tests` | Host-wiring: `IdentityEmailSender` (Wolverine handoff) and the `AddAppFoundation` Docker-secrets seam |
| `AppFoundation.MailService.Tests` | `SmtpEmailSender`, `SendEmailMessageHandler`, `InitializerExtension` (DI + data-annotation validation) |

Tests use xUnit v3 with NSubstitute (and bUnit where components are involved). Every test
follows the `// Arrange` / `// Act` / `// Assert` convention.

---

## 10. Build, packaging & release

- **Central Package Management** (`Directory.Packages.props`) with committed
  `packages.lock.json`; CI restores in `--locked-mode`.
- **Supply chain:** a vulnerability gate (`dotnet list package --vulnerable`) fails the build
  on any advisory; GitHub Actions are pinned to commit SHAs; the single package source is
  `nuget.org` (`NuGet.config`).
- **Packaging:** all four libraries are packable (`PackageId`, description, tags, README packed
  into the `.nupkg`); `Hosting` records the other three as dependencies (umbrella).
- **Release:** push a `vX.Y.Z` tag → CI packs all four at that version and publishes to NuGet
  via OIDC **trusted publishing** (no stored API key). Versioning is lockstep.
