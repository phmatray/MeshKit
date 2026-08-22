using System.Security.Claims;
using MeshKit.Web.Catalog;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace MeshKit.Web.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/checkout/{slug}", StartCheckoutAsync).RequireAuthorization();
        app.MapPost("/stripe/webhook", HandleWebhookAsync).DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> StartCheckoutAsync(
        string slug,
        HttpContext http,
        IAntiforgery antiforgery,
        ICatalogService catalog,
        PackCheckoutService checkout,
        IOptions<StripeOptions> stripe,
        CancellationToken cancellationToken)
    {
        // Explicit: the antiforgery middleware only enforces tokens on handlers that bind form data,
        // and this one reads nothing from the body. Without this a cross-site form could start a checkout.
        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        var pack = catalog.Find(slug);
        if (pack is null || !pack.IsSellable)
        {
            return Results.NotFound();
        }

        if (!stripe.Value.IsConfigured)
        {
            return Results.Problem("Payments are not configured on this instance.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var email = http.User.FindFirstValue(ClaimTypes.Email) ?? http.User.Identity?.Name;
        var url = await checkout.CreateSessionAsync(userId, email, pack, cancellationToken);
        return Results.Redirect(url.ToString(), permanent: false, preserveMethod: false);
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpRequest request,
        IOptions<StripeOptions> stripe,
        FulfillmentService fulfillment,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("MeshKit.Web.Payments.Webhook");
        if (string.IsNullOrWhiteSpace(stripe.Value.WebhookSecret))
        {
            logger.LogError("Stripe:WebhookSecret is not configured; refusing webhook");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var signature = request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrEmpty(signature))
        {
            logger.LogWarning("Rejected webhook: no Stripe-Signature header");
            return Results.BadRequest();
        }

        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken);
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, signature, stripe.Value.WebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning("Rejected webhook: {Message}", ex.Message);
            return Results.BadRequest();
        }

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
            case EventTypes.CheckoutSessionAsyncPaymentSucceeded:
                if (stripeEvent.Data.Object is Session session)
                {
                    var result = await fulfillment.FulfillAsync(StripeSessionSnapshot.FromSession(session), cancellationToken);
                    logger.LogInformation("{EventType} for {Session}: {Outcome} {Reason}", stripeEvent.Type, session.Id, result.Outcome, result.Reason);
                }

                break;

            case EventTypes.CheckoutSessionAsyncPaymentFailed:
                if (stripeEvent.Data.Object is Session failed)
                {
                    await fulfillment.MarkFailedAsync(failed.Id, cancellationToken);
                }

                break;

            default:
                logger.LogDebug("Ignoring event {EventType}", stripeEvent.Type);
                break;
        }

        return Results.Ok();
    }
}
