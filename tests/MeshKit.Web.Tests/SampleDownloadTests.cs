using System.IO.Compression;
using System.Net;
using MeshKit.Core.Catalog;
using MeshKit.Web.Search;
using Microsoft.Extensions.DependencyInjection;

namespace MeshKit.Web.Tests;

/// <summary>"Try before you buy": one model per pack is free for anyone with an account.</summary>
public sealed class SampleDownloadTests : IDisposable
{
    private readonly MeshKitWebFactory _factory = new();

    /// <summary>Two-model pack whose free sample is the lantern.</summary>
    private void WritePackWithSample(string slug = "fantasy-props", string? sample = "iron-lantern")
    {
        TestPacks.WriteRich(_factory.CatalogPath, slug, "Low-Poly Fantasy Props", "Dungeon dressing.", "props", "lowpoly", ["fantasy"], 1900,
            ("treasure-chest", "Treasure Chest", "a wooden chest", ["chest"], null, 1200, ["glb", "fbx"]),
            ("iron-lantern", "Iron Lantern", "a lantern", ["lantern"], null, 2600, ["glb", "obj"]));
        var dir = Path.Combine(_factory.CatalogPath, slug);
        File.WriteAllText(Path.Combine(dir, "private", "LICENSE.txt"), "LICENCE");
        var manifestPath = Path.Combine(dir, "manifest.json");
        var m = PackManifestSerializer.ReadFile(manifestPath);
        // the lantern ships one LOD
        Directory.CreateDirectory(Path.Combine(dir, "private", "iron-lantern", "lod1"));
        File.WriteAllText(Path.Combine(dir, "private", "iron-lantern", "lod1", "iron-lantern_lod1.glb"), "lod");
        // ...and one texture variant with its public preview and swatch
        Directory.CreateDirectory(Path.Combine(dir, "private", "iron-lantern", "variants", "snow"));
        File.WriteAllText(Path.Combine(dir, "private", "iron-lantern", "variants", "snow", "iron-lantern_snow.glb"), "snow");
        File.WriteAllText(Path.Combine(dir, "public", "preview", "iron-lantern.snow.glb"), "snow-preview");
        File.WriteAllText(Path.Combine(dir, "public", "thumbs", "iron-lantern.snow.png"), "png");
        var models = m.Models.Select(e => e.Slug == "iron-lantern"
            ? e with
            {
                Lods = [new ModelLod(1, 800, "lod-1", [new ModelFile("glb", "private/iron-lantern/lod1/iron-lantern_lod1.glb", 3)], 790, 5)],
                Variants = [new ModelVariant("snow", "Snow", "rt-1", [new ModelFile("glb", "private/iron-lantern/variants/snow/iron-lantern_snow.glb", 4)], "public/thumbs/iron-lantern.snow.png", "public/preview/iron-lantern.snow.glb", 10)],
            }
            : e).ToList();
        PackManifestSerializer.WriteFile(manifestPath, m with { Sample = sample, Models = models });
    }

    [Fact]
    public async Task Anonymous_sample_download_is_unauthorized()
    {
        WritePackWithSample();

        var response = await _factory.CreateClientAs(null).GetAsync("/packs/fantasy-props/sample");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/packs/nope/sample")]
    [InlineData("/packs/no-sample/sample")]
    public async Task Unknown_pack_or_pack_without_sample_is_not_found(string path)
    {
        WritePackWithSample("no-sample", sample: null);

        var response = await _factory.CreateClientAs("user-1").GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Logged_in_user_gets_only_the_sample_model_plus_licence_and_the_download_is_recorded()
    {
        WritePackWithSample();

        var response = await _factory.CreateClientAs("user-1").GetAsync("/packs/fantasy-props/sample");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("fantasy-props-sample-iron-lantern.zip", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync());
        Assert.Equal(
            ["fantasy-props-sample/LICENSE.txt", "fantasy-props-sample/iron-lantern/iron-lantern.glb", "fantasy-props-sample/iron-lantern/iron-lantern.obj", "fantasy-props-sample/iron-lantern/lod1/iron-lantern_lod1.glb", "fantasy-props-sample/iron-lantern/variants/snow/iron-lantern_snow.glb"],
            zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));

        var recorded = _factory.WithDb(db => db.SampleDownloads.ToList());
        var row = Assert.Single(recorded);
        Assert.Equal(("user-1", "fantasy-props", "iron-lantern"), (row.UserId, row.PackSlug, row.ModelSlug));
    }

    [Fact]
    public async Task Pack_page_offers_the_sample_and_marks_the_model_as_free()
    {
        WritePackWithSample();

        var visitor = await _factory.CreateClientAs(null).GetStringAsync("/packs/fantasy-props");
        var member = await _factory.CreateClientAs("user-1").GetStringAsync("/packs/fantasy-props");

        Assert.Contains("Free sample", visitor);
        Assert.Contains("account/login?returnUrl=", visitor);           // anonymous: sign in to download
        Assert.Contains("packs/fantasy-props/sample", member);          // member: direct download
        Assert.Contains("Iron Lantern", member);

        var lantern = await _factory.CreateClientAs(null).GetStringAsync("/packs/fantasy-props?model=iron-lantern");
        Assert.Contains("2,600 → 790 tris", lantern);                   // LOD chain on the selected model
        Assert.Contains("1 LOD", lantern);                              // in "What's inside"
        Assert.Contains("Skins:", lantern);
        Assert.Contains("2 skins", lantern);
        Assert.Contains("/catalog/fantasy-props/public/preview/iron-lantern.glb", lantern);

        var snow = await _factory.CreateClientAs(null).GetStringAsync("/packs/fantasy-props?model=iron-lantern&variant=snow");
        Assert.Contains("/catalog/fantasy-props/public/preview/iron-lantern.snow.glb", snow);   // the viewer shows the skin
        Assert.Contains("Iron Lantern · Snow", snow);
    }

    [Fact]
    public async Task Search_exposes_free_samples_as_a_facet_and_filter()
    {
        WritePackWithSample();
        var search = _factory.Services.GetRequiredService<ISearchService>();

        var all = search.Search(new SearchQuery());
        var free = search.Search(new SearchQuery(Free: true));

        Assert.Equal(2, all.Total);
        Assert.Equal(1, all.Facets.FreeSamples);
        var hit = Assert.Single(free.Hits);
        Assert.Equal("iron-lantern", hit.ModelSlug);
        Assert.True(hit.IsFree);
        Assert.False(all.Hits.Single(h => h.ModelSlug == "treasure-chest").IsFree);

        var html = await _factory.CreateClientAs(null).GetStringAsync("/search?free=true");
        Assert.Contains("Iron Lantern", html);
        Assert.DoesNotContain("Treasure Chest", html);
    }

    [Fact]
    public async Task Library_lists_samples_already_downloaded()
    {
        WritePackWithSample();
        var client = _factory.CreateClientAs("user-1");
        await client.GetAsync("/packs/fantasy-props/sample");

        var html = await client.GetStringAsync("/library");

        Assert.Contains("Free samples", html);
        Assert.Contains("Iron Lantern", html);
        Assert.Contains("packs/fantasy-props/sample", html);
    }

    public void Dispose() => _factory.Dispose();
}
