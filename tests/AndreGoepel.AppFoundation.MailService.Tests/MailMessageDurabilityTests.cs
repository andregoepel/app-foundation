using System.Reflection;
using AndreGoepel.AppFoundation.MailService;
using Wolverine.Attributes;
using Wolverine.Runtime.Handlers;

namespace AndreGoepel.AppFoundation.MailService.Tests;

// MailMessage bodies carry short-lived tokens + recipient PII, so they must not linger
// in the durable store. The two controls are framework-driven (a Wolverine attribute
// and the Configure convention), so these guard that they stay present and correctly
// shaped — a silent removal would re-open the issue without any compile error.
public class MailMessageDurabilityTests
{
    [Fact]
    public void MailMessage_IsCappedWithDeliverWithin()
    {
        // Act
        var attribute = typeof(MailMessage).GetCustomAttribute<DeliverWithinAttribute>();

        // Assert
        Assert.NotNull(attribute);
    }

    [Fact]
    public void MailMessage_DeliveryWindow_IsBoundedAndGenerous()
    {
        // Assert — a positive cap that is well beyond normal delivery latency.
        Assert.InRange(MailMessage.DeliveryWindowSeconds, 60, 24 * 3600);
    }

    [Fact]
    public void SendEmailMessageHandler_DeclaresConfigureConvention()
    {
        // Act — Wolverine discovers a static Configure(HandlerChain) to apply the
        // discard-on-failure policy; assert it exists with that exact signature.
        var configure = typeof(SendEmailMessageHandler).GetMethod(
            "Configure",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(HandlerChain)]
        );

        // Assert
        Assert.NotNull(configure);
        Assert.Equal(typeof(void), configure.ReturnType);
    }
}
