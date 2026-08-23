using System.Net;
using MeshKit.Web.Data;

namespace MeshKit.Web.Tests;

public sealed class AdminTests : IDisposable
{
    private readonly MeshKitWebFactory _factory = new();

    public AdminTests()
    {
        _factory.Settings["MeshKit:OwnerEmails"] = "owner@example.com, other-owner@example.com";
    }

    [Fact]
    public async Task Only_configured_owners_can_open_the_dashboard()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClientAs(null).GetAsync("/admin")).StatusCode);   // test scheme: 401 (cookies: redirect to login)
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateClientAs("visitor").GetAsync("/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClientAs("owner").GetAsync("/admin")).StatusCode);

        var nav = await _factory.CreateClientAs("visitor").GetStringAsync("/packs");
        Assert.DoesNotContain("href=\"admin\"", nav);
        var ownerNav = await _factory.CreateClientAs("owner").GetStringAsync("/packs");
        Assert.Contains("href=\"admin\"", ownerNav);
    }

    [Fact]
    public async Task Nobody_is_an_owner_when_the_setting_is_empty()
    {
        using var bare = new MeshKitWebFactory();

        Assert.Equal(HttpStatusCode.Forbidden, (await bare.CreateClientAs("owner").GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_shows_revenue_orders_samples_and_tried_not_bought()
    {
        TestPacks.Write(_factory.CatalogPath, "props", amount: 1900);
        _factory.WithDb(db =>
        {
            db.Users.Add(new ApplicationUser { Id = "buyer", UserName = "buyer@example.com", Email = "buyer@example.com" });
            db.Users.Add(new ApplicationUser { Id = "tried", UserName = "tried@example.com", Email = "tried@example.com" });
            var paid = new Order { UserId = "buyer", PackSlug = "props", StripeSessionId = "cs_1", AmountTotal = 1900, Currency = "eur", Status = OrderStatus.Paid, CreatedAt = DateTimeOffset.UnixEpoch };
            var abandoned = new Order { UserId = "tried", PackSlug = "props", StripeSessionId = "cs_2", AmountTotal = 1900, Currency = "eur", Status = OrderStatus.Pending, CreatedAt = DateTimeOffset.UnixEpoch };
            db.Orders.AddRange(paid, abandoned);
            db.SaveChanges();
            db.Entitlements.Add(new Entitlement { UserId = "buyer", PackSlug = "props", OrderId = paid.Id, GrantedAt = DateTimeOffset.UnixEpoch });
            db.SampleDownloads.Add(new SampleDownload { UserId = "buyer", PackSlug = "props", ModelSlug = "chest", DownloadedAt = DateTimeOffset.UnixEpoch });
            db.SampleDownloads.Add(new SampleDownload { UserId = "tried", PackSlug = "props", ModelSlug = "chest", DownloadedAt = DateTimeOffset.UnixEpoch, FollowUpOptIn = true });
            return db.SaveChanges();
        });

        var html = System.Net.WebUtility.HtmlDecode(await _factory.CreateClientAs("owner").GetStringAsync("/admin"));

        Assert.Contains("€19.00", html);                 // revenue
        Assert.Contains("1 abandoned at checkout", html);
        Assert.Contains("2 people", html);               // sample users
        Assert.Contains("50%", html);                    // tried → bought: 1 of 2
        Assert.Contains("tried@example.com", html);      // in "tried, not bought"
        Assert.Contains("opted in, pending", html);
        Assert.Contains("buyer@example.com", html);      // in recent orders
        Assert.Contains("noindex", html);
    }

    public void Dispose() => _factory.Dispose();
}
