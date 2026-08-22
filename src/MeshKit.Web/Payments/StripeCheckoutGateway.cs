using Stripe;
using Stripe.Checkout;

namespace MeshKit.Web.Payments;

public sealed class StripeCheckoutGateway(StripeClient client) : ICheckoutSessionGateway
{
    public async Task<CreatedCheckoutSession> CreateAsync(SessionCreateOptions options, CancellationToken cancellationToken)
    {
        var session = await client.V1.Checkout.Sessions.CreateAsync(options, cancellationToken: cancellationToken);
        return new CreatedCheckoutSession(session.Id, new Uri(session.Url));
    }
}
