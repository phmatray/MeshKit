using MeshKit.Web.Catalog;
using MeshKit.Web.Data;
using MeshKit.Web.Email;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace MeshKit.Web.Payments;

public enum FulfillmentOutcome
{
    Fulfilled,
    AlreadyFulfilled,
    NotPaid,
    Rejected,
}

public sealed record FulfillmentResult(FulfillmentOutcome Outcome, string? Reason = null);

/// <summary>
/// Grants the entitlement for a paid Checkout Session. Idempotent on the session id: Stripe retries
/// webhooks and sends both <c>completed</c> and <c>async_payment_succeeded</c>, and a replay must
/// never produce a second entitlement or order.
/// </summary>
public sealed class FulfillmentService(
    ApplicationDbContext db,
    ILogger<FulfillmentService> logger,
    TimeProvider? timeProvider = null,
    IEmailQueue? emails = null,
    ICatalogService? catalog = null,
    IOptions<MeshKitOptions>? meshKit = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<FulfillmentResult> FulfillAsync(StripeSessionSnapshot session, CancellationToken cancellationToken)
    {
        if (!session.IsPaid)
        {
            logger.LogInformation("Session {Session} not paid yet ({Status}); waiting for async_payment_succeeded", session.SessionId, session.PaymentStatus);
            return new FulfillmentResult(FulfillmentOutcome.NotPaid);
        }

        var order = await db.Orders.SingleOrDefaultAsync(o => o.StripeSessionId == session.SessionId, cancellationToken);
        var userId = order?.UserId ?? session.ClientReferenceId;
        var packSlug = order?.PackSlug ?? session.PackSlug;
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(packSlug))
        {
            logger.LogError("Session {Session} is paid but carries no user/pack (order missing, client_reference_id={User}, pack_slug={Pack})", session.SessionId, session.ClientReferenceId, session.PackSlug);
            return new FulfillmentResult(FulfillmentOutcome.Rejected, "missing user or pack");
        }

        if (order is null)
        {
            // The session was created out of band (or the pending insert was lost): reconstruct the order.
            order = new Order
            {
                UserId = userId,
                PackSlug = packSlug,
                StripeSessionId = session.SessionId,
                CreatedAt = _time.GetUtcNow(),
            };
            db.Orders.Add(order);
        }
        else if (order.Status == OrderStatus.Paid)
        {
            var hasEntitlement = await db.Entitlements.AnyAsync(e => e.UserId == userId && e.PackSlug == packSlug, cancellationToken);
            if (hasEntitlement)
            {
                return new FulfillmentResult(FulfillmentOutcome.AlreadyFulfilled);
            }
        }

        var now = _time.GetUtcNow();
        order.Status = OrderStatus.Paid;
        order.PaidAt ??= now;
        order.StripePaymentIntentId ??= session.PaymentIntentId;
        order.AmountTotal = session.AmountTotal ?? order.AmountTotal;
        order.Currency = session.Currency ?? order.Currency;

        var alreadyOwned = await db.Entitlements.AnyAsync(e => e.UserId == userId && e.PackSlug == packSlug, cancellationToken);
        if (!alreadyOwned)
        {
            await db.SaveChangesAsync(cancellationToken); // materialises order.Id for the FK
            db.Entitlements.Add(new Entitlement { UserId = userId, PackSlug = packSlug, OrderId = order.Id, GrantedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Fulfilled session {Session}: user {User} now owns {Pack}", session.SessionId, userId, packSlug);
        await SendConfirmationAsync(userId, packSlug, order, cancellationToken);
        return new FulfillmentResult(FulfillmentOutcome.Fulfilled);
    }

    private async Task SendConfirmationAsync(string userId, string packSlug, Order order, CancellationToken cancellationToken)
    {
        if (emails is null || meshKit is null)
        {
            return;
        }

        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrEmpty(email))
        {
            logger.LogWarning("No email on file for user {User}; purchase confirmation not sent", userId);
            return;
        }

        var pack = catalog?.Find(packSlug);
        emails.Enqueue(EmailTemplates.PurchaseConfirmation(
            email, pack?.Name ?? packSlug, meshKit.Value.PublicBaseUrl.TrimEnd('/'), packSlug, order.AmountTotal, order.Currency, pack?.License?.Name));
    }

    public async Task MarkFailedAsync(string sessionId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.StripeSessionId == sessionId, cancellationToken);
        if (order is null || order.Status == OrderStatus.Paid)
        {
            return;
        }

        order.Status = OrderStatus.Failed;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Session {Session} payment failed", sessionId);
    }
}
