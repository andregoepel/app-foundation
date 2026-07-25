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
public sealed record MailMessage(string Recipient, string Subject, string Body)
{
    // One hour is far beyond normal delivery, so it only sheds messages stuck across an outage.
    internal const int DeliveryWindowSeconds = 3600;
}
