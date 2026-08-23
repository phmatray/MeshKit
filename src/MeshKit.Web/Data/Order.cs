namespace MeshKit.Web.Data;

public enum OrderStatus
{
    Pending,
    Paid,
    Failed,
}

/// <summary>One Stripe Checkout Session for one pack. Created before the redirect, settled by the webhook.</summary>
public sealed class Order
{
    public int Id { get; set; }

    public required string UserId { get; set; }

    public required string PackSlug { get; set; }

    /// <summary>Idempotency key for fulfilment: a session is fulfilled at most once.</summary>
    public required string StripeSessionId { get; set; }

    public string? StripePaymentIntentId { get; set; }

    public long AmountTotal { get; set; }

    public string Currency { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PaidAt { get; set; }
}
