using System.Net;
using System.Text.RegularExpressions;
using MeshKit.Core.Catalog;
using MeshKit.Web.Data;
using MeshKit.Web.Samples;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeshKit.Web.Tests;

/// <summary>The one opt-in email after a free-sample download.</summary>
public sealed partial class SampleFollowUpTests : IDisposable
{
    private readonly MeshKitWebFactory _factory = new();

    public SampleFollowUpTests()
    {
        _factory.Settings["MeshKit:SampleFollowUp:PromotionCode"] = "SAMPLE15";
        _factory.Settings["MeshKit:SampleFollowUp:DiscountLabel"] = "15% off";
        _factory.Settings["MeshKit:SampleFollowUp:DelayHours"] = "48";
    }

    private void WritePack()
    {
        TestPacks.WriteRich(_factory.CatalogPath, "fantasy-props", "Low-Poly Fantasy Props", "Dungeon dressing.", "props", "lowpoly", ["fantasy"], 1900,
            ("iron-lantern", "Iron Lantern", "a lantern", ["lantern"], null, 2600, ["glb"]));
        var manifestPath = Path.Combine(_factory.CatalogPath, "fantasy-props", "manifest.json");
        PackManifestSerializer.WriteFile(manifestPath, PackManifestSerializer.ReadFile(manifestPath) with { Sample = "iron-lantern" });
    }

    private void AddUser(string id, string email) => _factory.WithDb(db =>
    {
        db.Users.Add(new ApplicationUser { Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() });
        return db.SaveChanges();
    });

    private void AddDownload(string userId, DateTimeOffset at, bool optIn) => _factory.WithDb(db =>
    {
        db.SampleDownloads.Add(new SampleDownload { UserId = userId, PackSlug = "fantasy-props", ModelSlug = "iron-lantern", DownloadedAt = at, FollowUpOptIn = optIn });
        return db.SaveChanges();
    });

    private async Task<int> SendDueAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var service = new SampleFollowUpService(
            sp.GetRequiredService<ApplicationDbContext>(), sp.GetRequiredService<Catalog.ICatalogService>(), sp.GetRequiredService<Email.IEmailQueue>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MeshKitOptions>>(), sp.GetRequiredService<ILogger<SampleFollowUpService>>(),
            new FixedTime(now));
        return await service.SendDueAsync(CancellationToken.None);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [Fact]
    public async Task Pack_page_form_records_the_opt_in()
    {
        WritePack();
        var client = _factory.CreateClientAs("user-1");
        var html = await client.GetStringAsync("/packs/fantasy-props");
        Assert.Contains("15% off", html);
        var token = TokenPattern().Match(html).Groups[1].Value;

        var response = await client.PostAsync("/packs/fantasy-props/sample", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["followup"] = "on",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        var row = Assert.Single(_factory.WithDb(db => db.SampleDownloads.ToList()));
        Assert.True(row.FollowUpOptIn);
        Assert.Null(row.FollowUpSentAt);
    }

    [Fact]
    public async Task Form_without_a_token_is_rejected_and_get_never_opts_in()
    {
        WritePack();
        var client = _factory.CreateClientAs("user-1");

        var forged = await client.PostAsync("/packs/fantasy-props/sample", new FormUrlEncodedContent(new Dictionary<string, string> { ["followup"] = "on" }));
        var plain = await client.GetAsync("/packs/fantasy-props/sample");

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.False(Assert.Single(_factory.WithDb(db => db.SampleDownloads.ToList())).FollowUpOptIn);
    }

    [Fact]
    public async Task Due_opt_ins_get_one_email_with_the_code_then_never_again()
    {
        WritePack();
        AddUser("user-1", "tried@example.com");
        var downloaded = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        AddDownload("user-1", downloaded, optIn: true);
        AddDownload("user-1", downloaded.AddMinutes(5), optIn: true);   // downloaded twice: still one email

        Assert.Equal(0, await SendDueAsync(downloaded.AddHours(47)));     // not due yet
        Assert.Equal(1, await SendDueAsync(downloaded.AddHours(49)));
        Assert.Equal(0, await SendDueAsync(downloaded.AddHours(100)));    // settled

        var mail = Assert.Single(await _factory.WaitForEmailsAsync(1));
        Assert.Equal("tried@example.com", mail.ToAddress);
        Assert.Contains("SAMPLE15", mail.Text);
        Assert.Contains("15% off", mail.Subject);
        Assert.Contains("http://localhost/packs/fantasy-props", mail.Text);
        Assert.Contains("Iron Lantern", mail.Text);
        Assert.All(_factory.WithDb(db => db.SampleDownloads.ToList()), d => Assert.NotNull(d.FollowUpSentAt));
    }

    [Fact]
    public async Task No_email_without_opt_in_or_when_the_pack_was_bought_meanwhile()
    {
        WritePack();
        AddUser("quiet", "quiet@example.com");
        AddUser("buyer", "buyer@example.com");
        var downloaded = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        AddDownload("quiet", downloaded, optIn: false);
        AddDownload("buyer", downloaded, optIn: true);
        _factory.WithDb(db =>
        {
            var order = new Order { UserId = "buyer", PackSlug = "fantasy-props", StripeSessionId = "cs_buyer", Status = OrderStatus.Paid, CreatedAt = downloaded };
            db.Orders.Add(order);
            db.SaveChanges();
            db.Entitlements.Add(new Entitlement { UserId = "buyer", PackSlug = "fantasy-props", OrderId = order.Id, GrantedAt = downloaded });
            return db.SaveChanges();
        });

        Assert.Equal(0, await SendDueAsync(downloaded.AddDays(3)));

        await Task.Delay(100);
        Assert.Empty(_factory.Outbox.Sent);
        Assert.NotNull(_factory.WithDb(db => db.SampleDownloads.Single(d => d.UserId == "buyer")).FollowUpSentAt);   // settled, not retried
    }

    [Fact]
    public async Task Checkbox_is_hidden_when_no_promotion_code_is_configured()
    {
        using var bare = new MeshKitWebFactory();
        TestPacks.WriteRich(bare.CatalogPath, "p", "P", "d", "props", "lowpoly", [], 1900, ("m", "M", "a", [], null, 100, ["glb"]));
        var path = Path.Combine(bare.CatalogPath, "p", "manifest.json");
        PackManifestSerializer.WriteFile(path, PackManifestSerializer.ReadFile(path) with { Sample = "m" });

        var html = await bare.CreateClientAs("user-1").GetStringAsync("/packs/p");

        Assert.Contains("Download free sample", html);
        Assert.DoesNotContain("name=\"followup\"", html);
    }

    public void Dispose() => _factory.Dispose();

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
