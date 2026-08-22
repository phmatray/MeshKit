using System.Net;
using System.Text;
using MeshKit.Web.Data;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace MeshKit.Web.Tests;

public sealed class StripeWebhookTests : IDisposable
{
    private readonly MeshKitWebFactory _factory = new();

    private static string SessionEvent(string type, string sessionId, string paymentStatus, string userId, string pack) => $$"""
        {
          "id": "evt_{{sessionId}}",
          "object": "event",
          "api_version": "2026-07-29.dahlia",
          "created": 1700000000,
          "livemode": false,
          "type": "{{type}}",
          "data": {
            "object": {
              "id": "{{sessionId}}",
              "object": "checkout.session",
              "payment_status": "{{paymentStatus}}",
              "status": "complete",
              "mode": "payment",
              "client_reference_id": "{{userId}}",
              "metadata": { "pack_slug": "{{pack}}" },
              "amount_total": 1900,
              "currency": "eur",
              "payment_intent": "pi_{{sessionId}}"
            }
          }
        }
        """;

    private static HttpRequestMessage Signed(string payload, string secret = MeshKitWebFactory.WebhookSecret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = EventUtility.ComputeSignature(secret, timestamp.ToString(), payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "/stripe/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={signature}");
        return request;
    }

    [Fact]
    public async Task Completed_paid_session_is_fulfilled()
    {
        TestPacks.Write(_factory.CatalogPath, "props");
        var client = _factory.CreateClientAs(null);

        var response = await client.SendAsync(Signed(SessionEvent("checkout.session.completed", "cs_a", "paid", "user-7", "props")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entitlement = _factory.WithDb(db => db.Entitlements.Single());
        Assert.Equal(("user-7", "props"), (entitlement.UserId, entitlement.PackSlug));
        Assert.Equal(OrderStatus.Paid, _factory.WithDb(db => db.Orders.Single(o => o.StripeSessionId == "cs_a")).Status);
    }

    [Fact]
    public async Task Completed_but_unpaid_session_waits_then_async_success_fulfils()
    {
        var client = _factory.CreateClientAs(null);

        var first = await client.SendAsync(Signed(SessionEvent("checkout.session.completed", "cs_b", "unpaid", "user-8", "props")));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(0, _factory.WithDb(db => db.Entitlements.Count()));

        var second = await client.SendAsync(Signed(SessionEvent("checkout.session.async_payment_succeeded", "cs_b", "paid", "user-8", "props")));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, _factory.WithDb(db => db.Entitlements.Count(e => e.UserId == "user-8")));
    }

    [Fact]
    public async Task Async_payment_failed_marks_the_order_failed()
    {
        _factory.WithDb(db =>
        {
            db.Orders.Add(new Order { UserId = "user-9", PackSlug = "props", StripeSessionId = "cs_c", CreatedAt = DateTimeOffset.UnixEpoch });
            return db.SaveChanges();
        });
        var client = _factory.CreateClientAs(null);

        var response = await client.SendAsync(Signed(SessionEvent("checkout.session.async_payment_failed", "cs_c", "unpaid", "user-9", "props")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.Failed, _factory.WithDb(db => db.Orders.Single(o => o.StripeSessionId == "cs_c")).Status);
    }

    [Fact]
    public async Task Bad_signature_is_rejected_and_nothing_is_fulfilled()
    {
        var client = _factory.CreateClientAs(null);

        var response = await client.SendAsync(Signed(SessionEvent("checkout.session.completed", "cs_d", "paid", "user-10", "props"), secret: "whsec_wrong"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.WithDb(db => db.Entitlements.Count()));
    }

    [Fact]
    public async Task Missing_signature_header_is_rejected()
    {
        var client = _factory.CreateClientAs(null);

        var response = await client.PostAsync("/stripe/webhook", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unrelated_event_types_are_acknowledged()
    {
        var client = _factory.CreateClientAs(null);
        var payload = """{"id":"evt_x","object":"event","api_version":"2026-07-29.dahlia","type":"customer.created","data":{"object":{"id":"cus_1","object":"customer"}}}""";

        var response = await client.SendAsync(Signed(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
