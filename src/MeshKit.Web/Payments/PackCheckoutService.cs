using MeshKit.Core.Catalog;
using MeshKit.Web.Catalog;
using MeshKit.Web.Data;
using Microsoft.Extensions.Options;
using Stripe.Checkout;

namespace MeshKit.Web.Payments;

/// <summary>Builds a Stripe Checkout Session for one pack and records the pending order before redirecting.</summary>
public sealed class PackCheckoutService(
    ICheckoutSessionGateway gateway,
    ApplicationDbContext db,
    IOptions<MeshKitOptions> meshKit,
    TimeProvider? timeProvider = null)
{
    /// <summary>Dashboard label for this flow; the 8-letter suffix is the one Stripe asks for, generated once.</summary>
    public const string IntegrationIdentifier = "meshkit-pack-checkout-qvzrbtlm";

    public const string PackSlugMetadataKey = "pack_slug";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<Uri> CreateSessionAsync(string userId, string? email, PackManifest pack, CancellationToken cancellationToken)
    {
        var baseUrl = meshKit.Value.PublicBaseUrl.TrimEnd('/');
        var thumbnail = pack.Models.Select(m => m.Thumbnail).FirstOrDefault(t => t is not null);

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            ClientReferenceId = userId,
            CustomerEmail = email,
            SuccessUrl = $"{baseUrl}/checkout/success?session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{baseUrl}/packs/{pack.Slug}",
            IntegrationIdentifier = IntegrationIdentifier,
            Metadata = new Dictionary<string, string> { [PackSlugMetadataKey] = pack.Slug },
            // Digital content, delivered immediately: the buyer's consent to lose the 14-day withdrawal
            // right (EU 2011/83) is recorded on the Stripe page itself, next to the pay button.
            CustomText = new SessionCustomTextOptions
            {
                Submit = new SessionCustomTextSubmitOptions
                {
                    Message = $"Digital download, delivered immediately. By paying you accept the terms of sale ({baseUrl}/legal/terms) and the pack licence, and agree to immediate delivery, which ends the 14-day withdrawal right.",
                },
            },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = pack.Price.Currency,
                        UnitAmount = pack.Price.Amount,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = pack.Name,
                            Description = $"{pack.Models.Count} 3D models · GLB, FBX, OBJ, USDZ",
                            Images = thumbnail is null ? null : [$"{baseUrl}{CatalogEndpoints.PublicUrl(pack.Slug, thumbnail)}"],
                        },
                    },
                },
            ],
        };

        var session = await gateway.CreateAsync(options, cancellationToken);

        db.Orders.Add(new Order
        {
            UserId = userId,
            PackSlug = pack.Slug,
            StripeSessionId = session.Id,
            AmountTotal = pack.Price.Amount,
            Currency = pack.Price.Currency,
            Status = OrderStatus.Pending,
            CreatedAt = _time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);

        return session.Url;
    }
}
