using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.Attributes;

namespace AndreGoepel.AppFoundation.MailService;

[WolverineHandler]
public class SendEmailMessageHandler(
    IEmailSender EmailSender,
    ILogger<SendEmailMessageHandler> Logger
)
{
    public async Task Handle(MailMessage message, Envelope envelope)
    {
        // MailMessage is an internal, in-process contract. Refuse to act on one that
        // arrived over an external transport, so a consumer that (accidentally) exposes
        // this message type on an untrusted transport cannot turn it into an
        // arbitrary-email / phishing primitive (#57). Messages published in-process are
        // routed to a local:// queue; anything else is dropped.
        if (!IsLocalOrigin(envelope.Destination))
        {
            Logger.LogWarning(
                "Dropping MailMessage received from non-local endpoint {Destination}. "
                    + "MailMessage may only be handled from the in-process transport.",
                envelope.Destination
            );
            return;
        }

        await EmailSender.SendAsync(message.Recipient, message.Subject, message.Body);
    }

    /// <summary>
    /// A MailMessage is trusted only when published in-process: Wolverine routes such
    /// messages to a <c>local://</c> queue, whereas an external transport carries its
    /// own scheme. A null destination (e.g. direct in-process invocation) is treated as
    /// local so the normal send path is never blocked.
    /// </summary>
    internal static bool IsLocalOrigin(Uri? destination) =>
        destination is null || destination.Scheme == "local";
}
