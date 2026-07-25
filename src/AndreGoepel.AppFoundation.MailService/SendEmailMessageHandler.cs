using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace AndreGoepel.AppFoundation.MailService;

[WolverineHandler]
public sealed class SendEmailMessageHandler(
    IEmailSender EmailSender,
    ILogger<SendEmailMessageHandler> Logger
)
{
    /// <summary>
    /// Wolverine convention: customizes this handler's error handling. A MailMessage
    /// body carries a short-lived token and the recipient's address, so on repeated
    /// failure it is <b>discarded</b> after a few retries rather than moved to the
    /// dead-letter table, where the token-bearing body would otherwise persist. The
    /// token expires and the user can re-request; the synchronous admin "send test"
    /// path remains the way to diagnose SMTP problems (#55).
    /// </summary>
    public static void Configure(HandlerChain chain) =>
        chain
            .OnAnyException()
            .RetryWithCooldown(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(5)
            )
            .Then.Discard();

    public async Task Handle(
        MailMessage message,
        Envelope envelope,
        CancellationToken cancellationToken
    )
    {
        // MailMessage is an internal, in-process contract; refuse to act on one that arrived over an external
        // transport, so an accidental exposure can't be turned into an arbitrary-email/phishing primitive (#57).
        if (!IsLocalOrigin(envelope.Destination))
        {
            Logger.LogWarning(
                "Dropping MailMessage received from non-local endpoint {Destination}. "
                    + "MailMessage may only be handled from the in-process transport.",
                envelope.Destination
            );
            return;
        }

        await EmailSender.SendAsync(
            message.Recipient,
            message.Subject,
            message.Body,
            cancellationToken
        );
    }

    // Trusted only when published in-process (routed to a local:// queue); a null destination (direct in-process
    // invocation) is treated as local so the normal send path is never blocked.
    internal static bool IsLocalOrigin(Uri? destination) =>
        destination is null || destination.Scheme == "local";
}
