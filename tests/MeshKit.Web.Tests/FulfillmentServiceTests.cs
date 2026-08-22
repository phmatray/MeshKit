using MeshKit.Web.Data;
using MeshKit.Web.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeshKit.Web.Tests;

public sealed class FulfillmentServiceTests : IDisposable
{
    private readonly SqliteDb _db = new();

    private static StripeSessionSnapshot Paid(string sessionId = "cs_1", string user = "user-1", string pack = "props") =>
        new(sessionId, "pi_1", user, pack, 1900, "eur", "paid");

    private FulfillmentService Service(ApplicationDbContext db) => new(db, NullLogger<FulfillmentService>.Instance);

    [Fact]
    public async Task Paid_session_with_pending_order_marks_it_paid_and_grants_entitlement()
    {
        using (var db = _db.Create())
        {
            db.Orders.Add(new Order { UserId = "user-1", PackSlug = "props", StripeSessionId = "cs_1", AmountTotal = 1900, Currency = "eur", CreatedAt = DateTimeOffset.UnixEpoch });
            await db.SaveChangesAsync();
        }

        using var ctx = _db.Create();
        var result = await Service(ctx).FulfillAsync(Paid(), CancellationToken.None);

        Assert.Equal(FulfillmentOutcome.Fulfilled, result.Outcome);
        var order = await ctx.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal("pi_1", order.StripePaymentIntentId);
        Assert.NotNull(order.PaidAt);
        var entitlement = await ctx.Entitlements.SingleAsync();
        Assert.Equal(("user-1", "props", order.Id), (entitlement.UserId, entitlement.PackSlug, entitlement.OrderId));
    }

    [Fact]
    public async Task Replaying_the_same_session_is_a_no_op()
    {
        using var ctx = _db.Create();
        var service = Service(ctx);
        await service.FulfillAsync(Paid(), CancellationToken.None);

        var second = await service.FulfillAsync(Paid(), CancellationToken.None);
        using var fresh = _db.Create();
        var third = await Service(fresh).FulfillAsync(Paid(), CancellationToken.None);

        Assert.Equal(FulfillmentOutcome.AlreadyFulfilled, second.Outcome);
        Assert.Equal(FulfillmentOutcome.AlreadyFulfilled, third.Outcome);
        Assert.Equal(1, await fresh.Orders.CountAsync());
        Assert.Equal(1, await fresh.Entitlements.CountAsync());
    }

    [Fact]
    public async Task Unknown_session_is_reconstructed_from_session_fields()
    {
        using var ctx = _db.Create();

        var result = await Service(ctx).FulfillAsync(Paid("cs_unknown"), CancellationToken.None);

        Assert.Equal(FulfillmentOutcome.Fulfilled, result.Outcome);
        var order = await ctx.Orders.SingleAsync();
        Assert.Equal(("user-1", "props", 1900L, "eur", OrderStatus.Paid), (order.UserId, order.PackSlug, order.AmountTotal, order.Currency, order.Status));
        Assert.Equal(1, await ctx.Entitlements.CountAsync());
    }

    [Fact]
    public async Task Unpaid_session_is_not_fulfilled()
    {
        using var ctx = _db.Create();
        ctx.Orders.Add(new Order { UserId = "user-1", PackSlug = "props", StripeSessionId = "cs_1", CreatedAt = DateTimeOffset.UnixEpoch });
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).FulfillAsync(Paid() with { PaymentStatus = "unpaid" }, CancellationToken.None);

        Assert.Equal(FulfillmentOutcome.NotPaid, result.Outcome);
        Assert.Equal(OrderStatus.Pending, (await ctx.Orders.SingleAsync()).Status);
        Assert.Equal(0, await ctx.Entitlements.CountAsync());
    }

    [Fact]
    public async Task Paid_session_without_user_or_pack_and_no_order_is_rejected()
    {
        using var ctx = _db.Create();

        var result = await Service(ctx).FulfillAsync(Paid() with { ClientReferenceId = null }, CancellationToken.None);

        Assert.Equal(FulfillmentOutcome.Rejected, result.Outcome);
        Assert.Equal(0, await ctx.Orders.CountAsync());
    }

    [Fact]
    public async Task Second_purchase_of_an_owned_pack_does_not_duplicate_the_entitlement()
    {
        using var ctx = _db.Create();
        var service = Service(ctx);
        await service.FulfillAsync(Paid("cs_1"), CancellationToken.None);

        var result = await service.FulfillAsync(Paid("cs_2"), CancellationToken.None);

        Assert.Equal(FulfillmentOutcome.Fulfilled, result.Outcome);
        Assert.Equal(2, await ctx.Orders.CountAsync());
        Assert.Equal(1, await ctx.Entitlements.CountAsync());
    }

    [Fact]
    public async Task MarkFailed_sets_pending_order_failed_but_never_downgrades_a_paid_one()
    {
        using var ctx = _db.Create();
        ctx.Orders.Add(new Order { UserId = "u", PackSlug = "props", StripeSessionId = "cs_pending", CreatedAt = DateTimeOffset.UnixEpoch });
        await ctx.SaveChangesAsync();
        var service = Service(ctx);
        await service.FulfillAsync(Paid("cs_paid"), CancellationToken.None);

        await service.MarkFailedAsync("cs_pending", CancellationToken.None);
        await service.MarkFailedAsync("cs_paid", CancellationToken.None);
        await service.MarkFailedAsync("cs_missing", CancellationToken.None);

        Assert.Equal(OrderStatus.Failed, (await ctx.Orders.SingleAsync(o => o.StripeSessionId == "cs_pending")).Status);
        Assert.Equal(OrderStatus.Paid, (await ctx.Orders.SingleAsync(o => o.StripeSessionId == "cs_paid")).Status);
    }

    public void Dispose() => _db.Dispose();
}
