using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.AppFoundation.MailService;
using Microsoft.AspNetCore.Identity;
using Wolverine;

namespace AndreGoepel.AppFoundation.Components.Account;

internal sealed class IdentityEmailSender(IMessageBus MessageBus) : IEmailSender<User>
{
    public async Task SendConfirmationLinkAsync(User user, string email, string confirmationLink) =>
        await MessageBus.SendAsync(
            new MailMessage(
                email,
                "Confirm your email",
                $"Please confirm your account by <a href='{confirmationLink}'>clicking here</a>."
            )
        );

    public async Task SendPasswordResetLinkAsync(User user, string email, string resetLink) =>
        await MessageBus.SendAsync(
            new MailMessage(
                email,
                "Reset your password",
                $"Please reset your password by <a href='{resetLink}'>clicking here</a>."
            )
        );

    public async Task SendPasswordResetCodeAsync(User user, string email, string resetCode) =>
        await MessageBus.SendAsync(
            new MailMessage(
                email,
                "Reset your password",
                $"Please reset your password using the following code: {resetCode}"
            )
        );
}
