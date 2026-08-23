using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MeshKit.Web.Tests;

public sealed class SeoAndPagesTests : IDisposable
{
    private readonly MeshKitWebFactory _factory = new();

    public SeoAndPagesTests()
    {
        TestPacks.WriteRich(_factory.CatalogPath, "fantasy-props", "Low-Poly Fantasy Props", "Dungeon dressing.", "props", "lowpoly", ["fantasy"], 1900,
            ("treasure-chest", "Treasure Chest", "a wooden chest", ["chest", "wooden"], null, 1200, ["glb", "fbx"]),
            ("iron-lantern", "Iron Lantern", "a lantern", ["lantern"], null, 2600, ["glb"]));
        var dir = Path.Combine(_factory.CatalogPath, "fantasy-props");
        File.WriteAllText(Path.Combine(dir, "public", "LICENSE.txt"), "MESHKIT LICENCE TEXT for Low-Poly Fantasy Props");
        File.WriteAllText(Path.Combine(dir, "private", "LICENSE.txt"), "MESHKIT LICENCE TEXT for Low-Poly Fantasy Props");
        var manifestPath = Path.Combine(dir, "manifest.json");
        var m = Core.Catalog.PackManifestSerializer.ReadFile(manifestPath);
        Core.Catalog.PackManifestSerializer.WriteFile(manifestPath, m with { License = new Core.Catalog.PackLicense("meshkit-standard", "MeshKit Royalty-Free Asset Licence", "public/LICENSE.txt", "private/LICENSE.txt") });
    }

    [Fact]
    public async Task Public_catalog_files_revalidate_with_etag_instead_of_resending_bytes()
    {
        var client = _factory.CreateClientAs(null);

        var first = await client.GetAsync("/catalog/fantasy-props/public/thumbs/treasure-chest.png");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("public, max-age=86400, stale-while-revalidate=604800", first.Headers.CacheControl!.ToString());
        Assert.NotNull(first.Headers.ETag);
        Assert.NotNull(first.Content.Headers.LastModified);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/catalog/fantasy-props/public/thumbs/treasure-chest.png");
        conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/search")]
    [InlineData("/search?q=chest&tag=wooden&format=fbx&sort=name")]
    [InlineData("/legal/terms")]
    [InlineData("/legal/privacy")]
    [InlineData("/legal/licence")]
    [InlineData("/packs/fantasy-props/licence")]
    public async Task Public_pages_render(string path)
    {
        var response = await _factory.CreateClientAs(null).GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_page_shows_hits_facets_and_tag_links()
    {
        var html = await _factory.CreateClientAs(null).GetStringAsync("/search?q=chest");

        Assert.Contains("Treasure Chest", html);
        Assert.DoesNotContain("Iron Lantern", html);
        Assert.Contains("search?tag=wooden", html);
        Assert.Contains("1 model match", html);
    }

    [Fact]
    public async Task Search_api_returns_json_hits_and_facets()
    {
        var json = await _factory.CreateClientAs(null).GetFromJsonAsync<JsonElement>("/api/search?q=lantern");

        Assert.Equal(1, json.GetProperty("total").GetInt32());
        var hit = json.GetProperty("hits")[0];
        Assert.Equal("iron-lantern", hit.GetProperty("model").GetString());
        Assert.Equal("/packs/fantasy-props?model=iron-lantern", hit.GetProperty("url").GetString());
        Assert.Equal(2600, hit.GetProperty("triangles").GetInt32());
        Assert.True(json.GetProperty("facets").GetProperty("tags").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Pack_page_carries_open_graph_json_ld_specs_and_licence_link()
    {
        var html = await _factory.CreateClientAs(null).GetStringAsync("/packs/fantasy-props?model=treasure-chest");

        Assert.Contains("<meta property=\"og:type\" content=\"product\"", html);
        Assert.Contains("og:image\" content=\"http://localhost/catalog/fantasy-props/public/thumbs/treasure-chest.png\"", html);
        Assert.Contains("<link rel=\"canonical\" href=\"http://localhost/packs/fantasy-props\"", html);
        Assert.Contains("\"@type\":\"Product\"", html);
        Assert.Contains("\"priceCurrency\":\"EUR\"", html);
        Assert.Contains("1,200", html);               // triangles, formatted
        Assert.Contains("packs/fantasy-props/licence", html);
        Assert.Contains("withdrawal right", html);
        Assert.Contains("search?tag=wooden", html);
    }

    [Fact]
    public async Task Licence_page_shows_the_shipped_text()
    {
        var html = await _factory.CreateClientAs(null).GetStringAsync("/packs/fantasy-props/licence");

        Assert.Contains("MESHKIT LICENCE TEXT for Low-Poly Fantasy Props", html);
    }

    [Fact]
    public async Task Home_has_organization_json_ld_with_vat()
    {
        var html = await _factory.CreateClientAs(null).GetStringAsync("/");

        Assert.Contains("\"@type\":\"Organization\"", html);
        Assert.Contains("BE 0744.517.956", html);
        Assert.Contains("og-default.png", html);
    }

    [Fact]
    public async Task Robots_and_sitemap()
    {
        var client = _factory.CreateClientAs(null);
        var robots = await client.GetStringAsync("/robots.txt");
        var sitemap = await client.GetStringAsync("/sitemap.xml");

        Assert.Contains("Disallow: /library", robots);
        Assert.Contains("Sitemap: http://localhost/sitemap.xml", robots);
        Assert.Contains("<loc>http://localhost/packs/fantasy-props</loc>", sitemap);
        Assert.Contains("<loc>http://localhost/legal/terms</loc>", sitemap);
        Assert.DoesNotContain("library", sitemap);
    }

    public void Dispose() => _factory.Dispose();
}
