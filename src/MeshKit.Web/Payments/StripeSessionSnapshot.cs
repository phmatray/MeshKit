using Stripe.Checkout;

namespace MeshKit.Web.Payments;

/// <summary>The fields of a Checkout Session that fulfilment needs, detached from the SDK type.</summary>
public sealed record StripeSessionSnapshot(
    string SessionId,
    string? PaymentIntentId,
    string? ClientReferenceId,
    string? PackSlug,
    long? AmountTotal,
    string? Currency,
    string? PaymentStatus)
{
    public bool IsPaid => string.Equals(PaymentStatus, "paid", StringComparison.Ordinal);

    public static StripeSessionSnapshot FromSession(Session session) => new(
        SessionId: session.Id,
        PaymentIntentId: session.PaymentIntentId,
        ClientReferenceId: session.ClientReferenceId,
        PackSlug: session.Metadata is { } m && m.TryGetValue(PackCheckoutService.PackSlugMetadataKey, out var slug) ? slug : null,
        AmountTotal: session.AmountTotal,
        Currency: session.Currency,
        PaymentStatus: session.PaymentStatus);
}
