using System.IO.Compression;
using System.Net;
using MeshKit.Web.Data;

namespace MeshKit.Web.Tests;

public sealed class DownloadTests : IDisposable
{
    private readonly MeshKitWebFactory _factory = new();

    private void Grant(string userId, string pack) => _factory.WithDb(db =>
    {
        var order = new Order { UserId = userId, PackSlug = pack, StripeSessionId = $"cs_{userId}_{pack}", Status = OrderStatus.Paid, CreatedAt = DateTimeOffset.UnixEpoch };
        db.Orders.Add(order);
        db.SaveChanges();
        db.Entitlements.Add(new Entitlement { UserId = userId, PackSlug = pack, OrderId = order.Id, GrantedAt = DateTimeOffset.UnixEpoch });
        return db.SaveChanges();
    });

    [Fact]
    public async Task Anonymous_download_is_unauthorized()
    {
        TestPacks.Write(_factory.CatalogPath, "props");

        var response = await _factory.CreateClientAs(null).GetAsync("/library/props/download");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Download_without_entitlement_is_forbidden()
    {
        TestPacks.Write(_factory.CatalogPath, "props");

        var response = await _factory.CreateClientAs("user-1").GetAsync("/library/props/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_of_unknown_pack_is_not_found()
    {
        var response = await _factory.CreateClientAs("user-1").GetAsync("/library/nope/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Entitled_user_gets_a_zip_of_the_private_files()
    {
        TestPacks.Write(_factory.CatalogPath, "props");
        Grant("user-1", "props");

        var response = await _factory.CreateClientAs("user-1").GetAsync("/library/props/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("props.zip", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync());
        Assert.Equal(["props/chest/chest.fbx", "props/chest/chest.glb"], zip.Entries.Select(e => e.FullName).Order());
    }

    [Fact]
    public async Task Pack_page_offers_download_to_owners_and_buy_to_everyone_else()
    {
        TestPacks.Write(_factory.CatalogPath, "props");
        Grant("owner", "props");

        var ownerHtml = await _factory.CreateClientAs("owner").GetStringAsync("/packs/props");
        var visitorHtml = await _factory.CreateClientAs("visitor").GetStringAsync("/packs/props");

        Assert.Contains("library/props/download", ownerHtml);
        Assert.DoesNotContain("checkout/props", ownerHtml);
        Assert.Contains("checkout/props", visitorHtml);
    }

    [Fact]
    public async Task Library_lists_owned_packs_only()
    {
        TestPacks.Write(_factory.CatalogPath, "owned-pack");
        TestPacks.Write(_factory.CatalogPath, "other-pack");
        Grant("user-1", "owned-pack");

        var html = await _factory.CreateClientAs("user-1").GetStringAsync("/library");

        Assert.Contains("Pack owned-pack", html);
        Assert.DoesNotContain("Pack other-pack", html);
    }

    [Fact]
    public async Task Checkout_refuses_unknown_pack_and_requires_login()
    {
        TestPacks.Write(_factory.CatalogPath, "props");

        var anonymous = await _factory.CreateClientAs(null).PostAsync("/checkout/props", new FormUrlEncodedContent([]));
        var unknown = await _factory.CreateClientAs("user-1").PostAsync("/checkout/nope", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode); // antiforgery runs before the catalog lookup
    }

    public void Dispose() => _factory.Dispose();
}
