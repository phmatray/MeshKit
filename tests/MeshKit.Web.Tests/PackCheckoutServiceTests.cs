using MeshKit.Core.Catalog;
using MeshKit.Core.Definitions;
using MeshKit.Web.Data;
using MeshKit.Web.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe.Checkout;

namespace MeshKit.Web.Tests;

public sealed class PackCheckoutServiceTests : IDisposable
{
    private readonly SqliteDb _db = new();

    private sealed class FakeGateway : ICheckoutSessionGateway
    {
        public SessionCreateOptions? Options { get; private set; }

        public Task<CreatedCheckoutSession> CreateAsync(SessionCreateOptions options, CancellationToken cancellationToken)
        {
            Options = options;
            return Task.FromResult(new CreatedCheckoutSession("cs_test_123", new Uri("https://checkout.stripe.com/c/pay/cs_test_123")));
        }
    }

    private static PackManifest Pack() => new(
        1, "props", "Fantasy Props", "desc", new Price(1900, "eur"), DateTimeOffset.UnixEpoch,
        [new ModelEntry("chest", "Chest", "p", ModelStatus.Succeeded, null, "a", "b", "public/thumbs/chest.png", "public/preview/chest.glb", [new ModelFile("glb", "private/chest/chest.glb", 1)], 30)]);

    [Fact]
    public async Task Builds_a_one_time_payment_session_from_the_manifest_and_records_a_pending_order()
    {
        var gateway = new FakeGateway();
        using var ctx = _db.Create();
        var service = new PackCheckoutService(gateway, ctx, Options.Create(new MeshKitOptions { PublicBaseUrl = "https://shop.example/" }));

        var url = await service.CreateSessionAsync("user-1", "buyer@example.com", Pack(), CancellationToken.None);

        Assert.Equal("https://checkout.stripe.com/c/pay/cs_test_123", url.ToString());
        var o = gateway.Options!;
        Assert.Equal("payment", o.Mode);
        Assert.Equal("user-1", o.ClientReferenceId);
        Assert.Equal("buyer@example.com", o.CustomerEmail);
        Assert.Equal("https://shop.example/checkout/success?session_id={CHECKOUT_SESSION_ID}", o.SuccessUrl);
        Assert.Equal("https://shop.example/packs/props", o.CancelUrl);
        Assert.Equal("props", o.Metadata[PackCheckoutService.PackSlugMetadataKey]);
        Assert.Matches("^meshkit-pack-checkout-[a-z]{8}$", o.IntegrationIdentifier);
        Assert.Null(o.PaymentMethodTypes);
        Assert.Contains("withdrawal right", o.CustomText.Submit.Message);
        Assert.Contains("https://shop.example/legal/terms", o.CustomText.Submit.Message);
        var item = Assert.Single(o.LineItems);
        Assert.Equal(1, item.Quantity);
        Assert.Equal("eur", item.PriceData.Currency);
        Assert.Equal(1900, item.PriceData.UnitAmount);
        Assert.Equal("Fantasy Props", item.PriceData.ProductData.Name);
        Assert.Equal(["https://shop.example/catalog/props/public/thumbs/chest.png"], item.PriceData.ProductData.Images);

        var order = await ctx.Orders.SingleAsync();
        Assert.Equal(("user-1", "props", "cs_test_123", OrderStatus.Pending, 1900L, "eur"),
            (order.UserId, order.PackSlug, order.StripeSessionId, order.Status, order.AmountTotal, order.Currency));
    }

    public void Dispose() => _db.Dispose();
}
