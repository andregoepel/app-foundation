using Wolverine.Attributes;

namespace AndreGoepel.AppFoundation.MailService;

/// <summary>
/// In-process request to send one email. The <see cref="Body"/> typically carries a
/// short-lived auth token (email confirmation / password reset) plus the recipient's
/// address, so the message is capped with <see cref="DeliverWithinAttribute"/>: an
/// undelivered message is discarded after the window rather than lingering as a
/// token-bearing row in the durable store (#55).
/// </summary>
[DeliverWithin(DeliveryWindowSeconds)]
public record MailMessage(string Recipient, string Subject, string Body)
{
    /// <summary>
    /// Maximum time a queued email may wait for delivery before Wolverine discards it.
    /// One hour is far beyond normal (sub-second) delivery, so it never affects the
    /// happy path — it only sheds messages stuck across an outage, whose token is
    /// likely expired anyway.
    /// </summary>
    internal const int DeliveryWindowSeconds = 3600;
}
