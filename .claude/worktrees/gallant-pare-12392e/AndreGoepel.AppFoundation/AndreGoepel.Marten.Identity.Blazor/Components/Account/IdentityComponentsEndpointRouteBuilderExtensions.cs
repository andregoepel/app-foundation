using System.Buffers.Text;
using System.Security.Claims;
using System.Text.Json;
using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AndreGoepel.Marten.Identity.Blazor.Components.Account;

public static class IdentityComponentsEndpointRouteBuilderExtensions
{
    // These endpoints are required by the Identity Razor components defined in the /Components/Account/Pages directory of this project.
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(
        this IEndpointRouteBuilder endpoints
    )
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost(
            "/Logout",
            async (
                ClaimsPrincipal user,
                [FromServices] SignInManager<User> signInManager,
                [FromForm] string returnUrl
            ) =>
            {
                await signInManager.SignOutAsync();
                return TypedResults.LocalRedirect($"~{returnUrl}");
            }
        );

        accountGroup.MapGet(
            "/SignOutAndRedirect",
            async ([FromServices] SignInManager<User> signInManager) =>
            {
                await signInManager.SignOutAsync();
                return TypedResults.LocalRedirect("~/");
            }
        );

        accountGroup.MapPost(
            "/PasskeyCreationOptions",
            async (
                HttpContext context,
                [FromServices] UserManager<User> userManager,
                [FromServices] SignInManager<User> signInManager
            ) =>
            {
                var user = await userManager.GetUserAsync(context.User);
                if (user is null)
                {
                    return Results.NotFound(
                        $"Unable to load user with ID '{userManager.GetUserId(context.User)}'."
                    );
                }

                var userId = await userManager.GetUserIdAsync(user);
                var userName = await userManager.GetUserNameAsync(user) ?? "User";
                var optionsJson = await signInManager.MakePasskeyCreationOptionsAsync(
                    new()
                    {
                        Id = userId,
                        Name = userName,
                        DisplayName = userName,
                    }
                );
                return TypedResults.Content(optionsJson, contentType: "application/json");
            }
        );

        accountGroup.MapPost(
            "/PasskeyRequestOptions",
            async (
                [FromServices] UserManager<User> userManager,
                [FromServices] SignInManager<User> signInManager,
                [FromQuery] string? username
            ) =>
            {
                var user = string.IsNullOrEmpty(username)
                    ? null
                    : await userManager.FindByNameAsync(username);
                var optionsJson = await signInManager.MakePasskeyRequestOptionsAsync(user);
                return TypedResults.Content(optionsJson, contentType: "application/json");
            }
        );

        accountGroup.MapPost(
            "/PasskeyAssertion",
            async (
                HttpContext context,
                [FromServices] SignInManager<User> signInManager,
                [FromQuery] string? returnUrl
            ) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var credentialJson = await reader.ReadToEndAsync();

                var result = await signInManager.PasskeySignInAsync(credentialJson);

                var safeReturnUrl =
                    !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') ? returnUrl : "/";

                if (result.Succeeded)
                    return Results.Content(safeReturnUrl, contentType: "text/plain");
                if (result.IsLockedOut)
                    return Results.Content("/Account/Lockout", contentType: "text/plain");
                if (result.RequiresTwoFactor)
                    return Results.Content(
                        $"/Account/LoginWith2fa?returnUrl={Uri.EscapeDataString(safeReturnUrl)}",
                        contentType: "text/plain"
                    );

                return Results.Content(
                    "Invalid passkey login attempt.",
                    contentType: "text/plain",
                    statusCode: 400
                );
            }
        );

        var manageGroup = accountGroup.MapGroup("/Manage").RequireAuthorization();

        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var resetAuthenticatorLogger = loggerFactory.CreateLogger("ResetAuthenticator");
        var downloadLogger = loggerFactory.CreateLogger("DownloadPersonalData");

        manageGroup.MapPost(
            "/PasskeyAttestation",
            async (
                HttpContext context,
                [FromServices] UserManager<User> userManager,
                [FromServices] SignInManager<User> signInManager
            ) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var credentialJson = await reader.ReadToEndAsync();

                var user = await userManager.GetUserAsync(context.User);
                if (user is null)
                    return Results.Unauthorized();

                var attestationResult = await signInManager.PerformPasskeyAttestationAsync(
                    credentialJson
                );
                if (!attestationResult.Succeeded)
                    return Results.Content(
                        attestationResult.Failure?.Message ?? "Attestation failed.",
                        contentType: "text/plain",
                        statusCode: 400
                    );

                var addResult = await userManager.AddOrUpdatePasskeyAsync(
                    user,
                    attestationResult.Passkey
                );
                if (!addResult.Succeeded)
                    return Results.Content(
                        addResult.Errors.FirstOrDefault()?.Description ?? "Could not save passkey.",
                        contentType: "text/plain",
                        statusCode: 400
                    );

                var credentialId = Base64Url.EncodeToString(attestationResult.Passkey.CredentialId);
                return Results.Content(credentialId, contentType: "text/plain");
            }
        );

        manageGroup.MapPost(
            "/ResetAuthenticator/ConfirmReset",
            async (
                HttpContext context,
                [FromServices] UserManager<User> userManager,
                [FromServices] SignInManager<User> signInManager
            ) =>
            {
                var user = await userManager.GetUserAsync(context.User);
                if (user is null)
                {
                    return Results.NotFound(
                        $"Unable to load user with ID '{userManager.GetUserId(context.User)}'."
                    );
                }

                await userManager.SetTwoFactorEnabledAsync(user, false);
                await userManager.ResetAuthenticatorKeyAsync(user);
                var userId = await userManager.GetUserIdAsync(user);
                resetAuthenticatorLogger.LogInformation(
                    "User with ID '{UserId}' has reset their authentication app key.",
                    userId
                );

                await signInManager.RefreshSignInAsync(user);

                return TypedResults.LocalRedirect("~/Account/Manage/EnableAuthenticator");
            }
        );

        manageGroup.MapPost(
            "/DownloadPersonalData",
            async (
                HttpContext context,
                [FromServices] UserManager<User> userManager,
                [FromServices] AuthenticationStateProvider authenticationStateProvider
            ) =>
            {
                var user = await userManager.GetUserAsync(context.User);
                if (user is null)
                {
                    return Results.NotFound(
                        $"Unable to load user with ID '{userManager.GetUserId(context.User)}'."
                    );
                }

                var userId = await userManager.GetUserIdAsync(user);
                downloadLogger.LogInformation(
                    "User with ID '{UserId}' asked for their personal data.",
                    userId
                );

                // Only include personal data for download
                var personalData = new Dictionary<string, string>();
                var personalDataProps = typeof(User)
                    .GetProperties()
                    .Where(prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
                foreach (var p in personalDataProps)
                {
                    personalData.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
                }

                var logins = await userManager.GetLoginsAsync(user);
                foreach (var l in logins)
                {
                    personalData.Add(
                        $"{l.LoginProvider} external login provider key",
                        l.ProviderKey
                    );
                }

                personalData.Add(
                    "Authenticator Key",
                    (await userManager.GetAuthenticatorKeyAsync(user))!
                );
                var fileBytes = JsonSerializer.SerializeToUtf8Bytes(personalData);

                context.Response.Headers.TryAdd(
                    "Content-Disposition",
                    "attachment; filename=PersonalData.json"
                );
                return TypedResults.File(
                    fileBytes,
                    contentType: "application/json",
                    fileDownloadName: "PersonalData.json"
                );
            }
        );

        return accountGroup;
    }
}
