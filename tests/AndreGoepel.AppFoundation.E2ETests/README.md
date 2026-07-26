# AndreGoepel.AppFoundation.E2ETests

End-to-end tests that drive the **real** application — the Blazor UI, the cookie-login
middleware, PostgreSQL, and email delivery — through a Chromium browser.

This suite is a **thin smoke pass** over this app's own sample host, not the exhaustive
identity-flow coverage it used to be (#150) — `marten-identity` carries the authoritative,
full identity-flow E2E suite (Login/Registration/Passkey/TwoFactor/PasswordReset/
AccountManagement/Administration) instead. What stays here proves the sample host wires
everything up correctly: one happy-path loop per area, plus anything genuinely specific to this
app (the Email Settings admin page, MailHog wiring, the Quartz persistent-store restart check).
See each `Tests/*.cs` file's summary for what was trimmed and why.

## How it works

- **`AndreGoepel.Testing.E2E`** supplies the shared fixture/test-base infrastructure (app
  boot, browser lifecycle, page helpers, MailHog client, TOTP, the virtual WebAuthn
  authenticator) — see that package's own README for what it provides. This repo's own
  `Infrastructure/` folder only holds what's genuinely app-specific:
  `AppFoundationE2EAppFixture` (configures the app via `E2EAppFixtureOptions`, plus
  `EnsureEmailConfiguredAsync` — see below) and `E2ETestBase` (closes the package's generic
  `E2ETestBase<TFixture>` over that fixture).
- **Aspire.Hosting.Testing** boots the sample `AppHost`
  (`samples/AndreGoepel.AppFoundation.AppHost`) once per test run: PostgreSQL, MailHog, and the
  sample web app (Aspire resource `web`). The fixture waits for `web` to become healthy, then
  reads its `https` endpoint.
- **Microsoft.Playwright** (Chromium) drives the browser. Each test gets a fresh
  `IBrowserContext` so cookies never leak between tests.
- **MailHog** captures every outgoing email; the package's `MailHogClient` reads the inbox over
  its HTTP API so confirmation / password-reset links are followed for real. Email settings are
  database-only (no configuration fallback), so `AppFoundationE2EAppFixture.EnsureEmailConfiguredAsync`
  saves MailHog's connection details through the real Email Settings admin page once per app
  instance, the same way a real administrator would.
- **Otp.NET**, wrapped by the package's `Totp` helper, generates TOTP codes for the authenticator
  (2FA) flow.
- A Chromium **CDP virtual authenticator** (the package's `VirtualAuthenticator`) satisfies
  WebAuthn ceremonies, so the passkey register/login flow runs headlessly with no physical device.

The suite runs **serially** inside one xUnit collection because it shares a single app instance
and database. The first test that needs it provisions the root admin exactly once via the
`/Setup` flow (`AppFoundationE2EAppFixture.ProvisionAdminAsync`, inherited from the package,
idempotent). On CI's fresh runners the Postgres volume starts empty; because the flows are
idempotent (admin provisioned once, unique emails per test) they also tolerate reused local
state.

The account pages under test (`/Account/*`, `/Administration/Users`, `/Administration/Roles`)
ship in the **`AndreGoepel.Marten.Identity.Blazor`** NuGet package the app consumes; the
foundation pages (`/Setup`, `/dashboard`, `/Administration/EmailSettings`,
`/Administration/LoginFeatures`) live in `src/AndreGoepel.AppFoundation`.

## Prerequisites

1. **A container runtime** must be running — Docker **or** Podman. The tests start real
   containers; if none is reachable the fixture fails fast.
2. **.NET 10 SDK**.
3. **Playwright browsers** installed once:

   ```bash
   # after a build, from the repo root:
   pwsh tests/AndreGoepel.AppFoundation.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium
   ```

### Using Podman instead of Docker

Aspire's orchestrator auto-detects the runtime, but if Docker Desktop's `docker.exe` is on your
PATH (even with its daemon stopped) it may be picked first. Force Podman:

1. Start a Podman machine (one-time init on Windows/macOS):

   ```powershell
   podman machine init      # only if `podman machine list` shows none
   podman machine start
   podman version           # should show a Server section
   ```

2. Select the Podman runtime, either via a user env var:

   ```powershell
   setx DOTNET_ASPIRE_CONTAINER_RUNTIME podman   # new terminals pick it up
   ```

   …or per-run with the provided settings file:

   ```bash
   dotnet test tests/AndreGoepel.AppFoundation.E2ETests --settings tests/AndreGoepel.AppFoundation.E2ETests/podman.runsettings
   ```

> First run pulls the `postgres` and `mailhog/mailhog` images. If Podman prompts to choose a
> registry, add `unqualified-search-registries = ["docker.io"]` to your `containers.conf`, or
> pre-pull: `podman pull docker.io/mailhog/mailhog:v1.0.1` and `podman pull docker.io/library/postgres`.

## Running

```bash
# from the repo root
dotnet test tests/AndreGoepel.AppFoundation.E2ETests
```

Watch the browser (debugging locally):

```bash
E2E_HEADED=true dotnet test tests/AndreGoepel.AppFoundation.E2ETests
```

The main `CI` workflow skips these (`--filter "FullyQualifiedName!~E2ETests"`); they run in the
dedicated `E2E` GitHub Actions workflow, which has Docker available.

## Coverage

| Area | Tests |
| --- | --- |
| Smoke | app boots, `/Setup` runs once, default roles created, admin login → dashboard |
| Registration | register → email confirmation → login |
| Login | logout → protected page redirects to login |
| Password reset | forgot → emailed link → reset → login |
| Two-factor (TOTP) | enable via authenticator → fresh login requires and accepts a generated code |
| Passkeys (WebAuthn) | register a credential → sign out → sign back in with it |
| Account management | change password → login with the new one |
| Administration | list users (includes the admin account); non-admin bounced away from `/Administration` |
| App pages | sample home renders; Email Settings admin page loads |
| Quartz persistent store | cleanup job/trigger re-schedules cleanly across an app restart (#129) |

The exhaustive identity-flow coverage this suite used to carry (wrong-password messaging,
lockout, form validation, recovery-code login, disabling 2FA, passkey rename/list, profile
updates, account deletion, role CRUD, the shared Login Features admin page) moved to
`marten-identity`'s own authoritative E2E suite (#150) — each trimmed `Tests/*.cs` file's summary
says what left and why.

## Tuning notes

The UI is built with **Radzen**, whose markup can shift between versions. The shared page-helper
selectors live in `AndreGoepel.Testing.E2E`'s `PageExtensions`; app-specific ones (like the login
helper `AppFoundationE2EAppFixture.EnsureEmailConfiguredAsync` needs before any test-base fixture
exists) live in `Infrastructure/LoginPageExtensions.cs`. If a selector drifts, fix it in
whichever of those two places owns it. The account-flow selectors target the current
`AndreGoepel.Marten.Identity.Blazor` package markup; verify them on the first live run after a
package bump. The 2FA and passkey tests are the most timing-sensitive (JS-driven ceremonies); if
they flake, check `WaitForBlazorAsync` and the module-load delay in
`PasskeyTests.RegisterPasskeyAsync` first.
