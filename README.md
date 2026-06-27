# app-foundation

A reusable **backend + management-frontend foundation** for .NET 10 / Blazor apps,
published as NuGet packages. A consuming app references the packages and gets
authentication, user/role administration, a Blazor management shell, durable email,
and .NET Aspire service defaults out of the box — then adds its own pages and branding
on top.

- **Reference consumer:** [andregoepel.dev](https://github.com/andregoepel/andregoepel-dev)
- **Identity** is provided by the companion [AndreGoepel.Marten.Identity](https://github.com/andregoepel/marten-identity) packages.

> This repository used to host both the foundation *and* a website. The website has been
> extracted to its own repo; this repository is now purely the published packages.

---

## Packages

| Package | Purpose |
|---|---|
| `AndreGoepel.AppFoundation` | Razor Class Library — the management frontend: layout, navigation, setup, dashboard, error pages. Brand/extend via `AppFoundationLayoutOptions`. |
| `AndreGoepel.AppFoundation.Hosting` | **Umbrella backend seam** — `AddAppFoundation` / `UseAppFoundation`. Transitively pulls in the other three packages + `AndreGoepel.Marten.Identity.Blazor`. |
| `AndreGoepel.AppFoundation.MailService` | Email via a Wolverine handler + MailKit SMTP, backed by a durable Marten outbox. |
| `AndreGoepel.AppFoundation.ServiceDefaults` | .NET Aspire service defaults: OpenTelemetry, health checks, HTTP resilience, service discovery. |

All four are published to NuGet with lockstep versioning. A host typically references
`AndreGoepel.AppFoundation.Hosting` (for the wiring) and `AndreGoepel.AppFoundation` (for
the UI components it renders directly).

---

## Stack

| Layer | Technology |
|---|---|
| Frontend | Blazor (.NET 10), [Radzen](https://blazor.radzen.com/) components |
| Backend | ASP.NET Core (.NET 10) |
| Persistence | [Marten](https://martendb.io/) (PostgreSQL document + event store) |
| Messaging / CQRS | [Wolverine](https://wolverine.netlify.app/) (durable outbox) |
| Email | [MailKit](https://github.com/jstedfast/MailKit) |
| Orchestration | [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/) |
| Authentication | ASP.NET Core Identity, event-sourced ([marten-identity](https://github.com/andregoepel/marten-identity)) |

---

## Using it in a host app

Reference the packages (versions are centrally managed in your repo):

```xml
<PackageReference Include="AndreGoepel.AppFoundation.Hosting" />
<PackageReference Include="AndreGoepel.AppFoundation" />
<PackageReference Include="AndreGoepel.Marten.Identity.Blazor" />
```

Wire the foundation in `Program.cs`:

```csharp
builder.AddAppFoundation(options =>
{
    options.DatabaseConnectionName = "appfoundation-database"; // default
    // options.WolverineServiceName, options.SecretsDirectory ...
});

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Brand the management shell and contribute your own admin menu entries.
builder.Services.Configure<AppFoundationLayoutOptions>(o =>
{
    o.BrandName = "your.app";
    o.Copyright = "your.app © 2026";
    o.AdminMenu = typeof(YourAdminMenu); // optional Razor component
});

var app = builder.Build();

app.UseAppFoundation();

app.MapStaticAssets();
app.MapRazorComponents<App>() // your root App.razor
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(AppFoundationLayoutOptions).Assembly,                         // AppFoundation UI
        typeof(AndreGoepel.Marten.Identity.Blazor.Initialization).Assembly   // identity pages
    );

app.MapAdditionalIdentityEndpoints();
```

The host owns its root `App.razor` and `Routes.razor`; the router includes the AppFoundation
and Marten.Identity.Blazor assemblies and uses AppFoundation's `MainLayout` for the
authenticated management area. See [andregoepel.dev](https://github.com/andregoepel/andregoepel-dev)
for a complete, working example.

---

## Configuration

A PostgreSQL connection string is required, by default under
`ConnectionStrings:appfoundation-database`.

**`AppFoundationOptions`** (backend seam):

| Option | Default | Purpose |
|---|---|---|
| `DatabaseConnectionName` | `appfoundation-database` | Connection-string name to read |
| `WolverineServiceName` | `AppFoundation` | Service name for the durable inbox/outbox |
| `SecretsDirectory` | `/run/secrets` | Key-per-file secrets directory (see below) — *from 1.1.0* |

**`AppFoundationLayoutOptions`** (management shell branding):
`BrandName`, `LogoPath`, `Copyright`, and `AdminMenu` (a Razor component type rendered as
extra administrator nav entries).

### Docker secrets (from 1.1.0)

Configuration — notably the connection string — can be supplied as files instead of
plaintext environment variables. `AddAppFoundation` loads `SecretsDirectory` (default
`/run/secrets`) key-per-file with `optional: true` (a no-op when absent, so local
development is unaffected). A secret named `ConnectionStrings__appfoundation-database`
(content = the full connection string; `__` maps to the `:` section separator) is read
straight into configuration and never enters the process environment.

```yaml
services:
  app:
    image: ghcr.io/you/your-app:latest
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    secrets:
      - ConnectionStrings__appfoundation-database

secrets:
  ConnectionStrings__appfoundation-database:
    file: ./secrets/connectionstring   # chmod 600; or `external: true` under Swarm
```

---

## Repository layout

```
src/
  AndreGoepel.AppFoundation/             # management-frontend RCL
  AndreGoepel.AppFoundation.Hosting/     # umbrella backend seam
  AndreGoepel.AppFoundation.MailService/ # Wolverine + MailKit email
  AndreGoepel.AppFoundation.ServiceDefaults/
tests/
  AndreGoepel.AppFoundation.Tests/
  AndreGoepel.AppFoundation.MailService.Tests/
```

---

## Build & release

```bash
dotnet restore     # --locked-mode in CI
dotnet build -c Release
dotnet test -c Release
```

- **Central Package Management** with committed `packages.lock.json`; CI restores in
  `--locked-mode`, runs a vulnerability gate, and pins actions to commit SHAs.
- **Publishing:** push a `vX.Y.Z` tag → CI packs all four packages and publishes them to
  NuGet via OIDC trusted publishing (no stored API key).

---

## Architecture notes

**Why Marten?** PostgreSQL as a document store removes the ORM mapping layer for most use
cases while keeping relational queries available, with event sourcing built in. No separate
NoSQL infrastructure.

**Why Wolverine?** Clean handler dispatch with built-in message persistence, retries and
outbox support — async messaging from day one, not bolted on later.

**Why .NET Aspire?** Local orchestration and service discovery that map cleanly to cloud
deployment targets.

**Why a modular monolith?** Modules are separated by namespace and handler boundary, not by
network boundary. Splitting later is possible; splitting prematurely adds operational
overhead before there's a scaling problem that justifies it.

---

## License

MIT — use freely, attribution appreciated but not required.

---

*Built by [André Göpel](https://andregoepel.dev) — Senior Web Engineer · .NET & Blazor*
