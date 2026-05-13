# AndreGoepel.Marten.Identity.Blazor

Blazor Server UI for [AndreGoepel.Marten.Identity](https://www.nuget.org/packages/AndreGoepel.Marten.Identity) — a complete set of ready-to-use Razor components for authentication and user management built on [Radzen Blazor](https://blazor.radzen.com/).

## Requirements

- .NET 10
- AndreGoepel.Marten.Identity 1.x
- Radzen.Blazor 10.x

## Installation

```
dotnet add package AndreGoepel.Marten.Identity.Blazor
```

## Usage

### 1. Register services

```csharp
builder.Services.AddMartenIdentity();       // from AndreGoepel.Marten.Identity
builder.Services.AddMartenIdentityBlazor(); // registers CascadingAuthenticationState
                                            // and IdentityRevalidatingAuthenticationStateProvider
builder.Services.AddRadzenComponents();
```

### 2. Map endpoints

```csharp
app.MapAdditionalIdentityEndpoints(); // /Account/Logout, passkey endpoints, personal data download, etc.
```

### 3. Include the component assembly in routing

```csharp
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(typeof(AndreGoepel.Marten.Identity.Blazor.Initialization).Assembly);
```

This makes the routable pages in the RCL (e.g. `/Account/Login`) discoverable by the Blazor router.

## What's included

| Area | Components / Pages |
|---|---|
| **Account** | Login, Register, ForgotPassword, ResetPassword, ConfirmEmail, Lockout, AccessDenied, and more |
| **Account / Manage** | Profile, Email, ChangePassword, SetPassword, TwoFactorAuthentication, EnableAuthenticator, GenerateRecoveryCodes, Passkeys, PersonalData, DeletePersonalData |
| **Administration** | Users grid, Roles grid, user/role assignment dialogs |
| **Shared** | `RedirectToLogin`, `ShowRecoveryCodes`, `PasskeySubmit` |
| **Layout** | `LoginLayout` |

## License

MIT
