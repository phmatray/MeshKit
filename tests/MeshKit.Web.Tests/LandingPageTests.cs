using System.Net;
using MeshKit.Core.Catalog;

namespace MeshKit.Web.Tests;

/// <summary>The indexable collection pages: one per category/style with models, plus the free-samples page.</summary>
public sealed class LandingPageTests : IDisposable
{
    private readonly MeshKitWebFactory _factory = new();

    public LandingPageTests()
    {
        TestPacks.WriteRich(_factory.CatalogPath, "nature-kit", "Low-Poly Nature Kit", "Trees and rocks.", "nature", "lowpoly", ["nature"], 1900,
            ("oak-tree", "Oak Tree", "an oak", ["tree"], null, 3500, ["glb", "fbx"]),
            ("boulder", "Boulder", "a rock", ["rock"], null, 2500, ["glb"]));
        TestPacks.WriteRich(_factory.CatalogPath, "weapons", "Stylized Weapons", "Swords.", "weapons", "stylized", ["weapons"], 1900,
            ("dagger", "Dagger", "a dagger", ["blade"], null, 4600, ["glb"]));
        var path = Path.Combine(_factory.CatalogPath, "weapons", "manifest.json");
        PackManifestSerializer.WriteFile(path, PackManifestSerializer.ReadFile(path) with { Sample = "dagger" });
    }

    [Theory]
    [InlineData("/3d-models/nature", "Nature 3D models", "Oak Tree")]
    [InlineData("/3d-models/lowpoly", "Low-poly 3D models", "Boulder")]
    [InlineData("/3d-models/weapons", "Weapons 3D models", "Dagger")]
    [InlineData("/free-3d-models", "Free 3D models, game-ready", "Dagger")]
    public async Task Collection_pages_render_models_packs_promise_faq_and_json_ld(string path, string headline, string model)
    {
        var response = await _factory.CreateClientAs(null).GetAsync(path);
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(headline, html);
        Assert.Contains(model, html);
        Assert.Contains("What's in every MeshKit pack", html);
        Assert.Contains("Questions buyers ask", html);
        Assert.Contains("\"@type\":\"CollectionPage\"", html);
        Assert.Contains("\"@type\":\"FAQPage\"", html);
        Assert.Contains($"rel=\"canonical\" href=\"http://localhost{path}\"", html);
        Assert.DoesNotContain("noindex", html);
    }

    [Fact]
    public async Task Free_page_lists_only_samples_and_nature_page_only_nature()
    {
        var free = await _factory.CreateClientAs(null).GetStringAsync("/free-3d-models");
        var nature = await _factory.CreateClientAs(null).GetStringAsync("/3d-models/nature");

        Assert.Contains("Dagger", free);
        Assert.DoesNotContain("Oak Tree", free);
        Assert.Contains("Oak Tree", nature);
        Assert.DoesNotContain("Dagger", nature);
    }

    [Theory]
    [InlineData("/3d-models/food")]        // known category, no models
    [InlineData("/3d-models/nope")]        // unknown
    public async Task Empty_or_unknown_collections_are_not_pages(string path)
    {
        var html = await _factory.CreateClientAs(null).GetStringAsync(path);

        Assert.Contains("No such collection", html);
    }

    [Fact]
    public async Task Index_sitemap_and_footer_link_the_collections()
    {
        var index = await _factory.CreateClientAs(null).GetStringAsync("/3d-models");
        var sitemap = await _factory.CreateClientAs(null).GetStringAsync("/sitemap.xml");
        var home = await _factory.CreateClientAs(null).GetStringAsync("/");

        Assert.Contains("href=\"3d-models/nature\"", index);
        Assert.Contains("href=\"free-3d-models\"", index);
        Assert.Contains("<loc>http://localhost/free-3d-models</loc>", sitemap);
        Assert.Contains("<loc>http://localhost/3d-models/lowpoly</loc>", sitemap);
        Assert.DoesNotContain("3d-models/food", sitemap);
        Assert.Contains("href=\"3d-models/weapons\"", home);          // footer
    }

    public void Dispose() => _factory.Dispose();
}
