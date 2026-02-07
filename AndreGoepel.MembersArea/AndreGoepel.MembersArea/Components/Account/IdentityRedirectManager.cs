using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Radzen;

namespace AndreGoepel.MembersArea.Components.Account;

internal sealed class IdentityRedirectManager(
    NavigationManager navigationManager,
    NotificationService notificationService
)
{
    public const string StatusCookieName = "Identity.StatusMessage";

    public void RedirectTo(string? uri)
    {
        uri ??= "";

        // Prevent open redirects.
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }

        navigationManager.NavigateTo(uri);
    }

    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    public void RedirectToWithStatus(
        string uri,
        string summary,
        string message,
        NotificationSeverity severity = NotificationSeverity.Info
    )
    {
        notificationService.Notify(severity, summary, message, 5000);
        RedirectTo(uri);
    }

    private string CurrentPath =>
        navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

    public void RedirectToCurrentPageWithStatus(
        string summary,
        string message,
        NotificationSeverity severity = NotificationSeverity.Info
    ) => RedirectToWithStatus(CurrentPath, summary, message, severity);

    public void RedirectToInvalidUser(UserManager<User> userManager, HttpContext context) =>
        RedirectToWithStatus(
            "Account/InvalidUser",
            "Error",
            $"Unable to load user with ID '{userManager.GetUserId(context.User)}'.",
            NotificationSeverity.Error
        );
}
