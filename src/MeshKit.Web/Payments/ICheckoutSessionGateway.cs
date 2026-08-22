using Stripe.Checkout;

namespace MeshKit.Web.Payments;

public sealed record CreatedCheckoutSession(string Id, Uri Url);

/// <summary>The one Stripe call the store makes; abstracted so <see cref="PackCheckoutService"/> is testable offline.</summary>
public interface ICheckoutSessionGateway
{
    Task<CreatedCheckoutSession> CreateAsync(SessionCreateOptions options, CancellationToken cancellationToken);
}
