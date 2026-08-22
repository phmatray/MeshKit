namespace MeshKit.Web.Payments;

public sealed class StripeOptions
{
    public const string Section = "Stripe";

    /// <summary>A restricted key (<c>rk_…</c>) with Checkout Sessions write access is enough.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Signing secret of the <c>/stripe/webhook</c> endpoint (<c>whsec_…</c>).</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}
