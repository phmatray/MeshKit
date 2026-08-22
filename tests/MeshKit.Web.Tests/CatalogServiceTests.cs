using System.IO.Compression;
using MeshKit.Core.Catalog;
using MeshKit.Web.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MeshKit.Web.Tests;

public sealed class CatalogServiceTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("meshkit-catalog");

    private CatalogService Create() => new(
        Options.Create(new CatalogOptions { Path = _root.FullName }), NullLogger<CatalogService>.Instance);

    [Fact]
    public void Scans_packs_and_exposes_only_sellable_ones_in_store_order()
    {
        TestPacks.Write(_root.FullName, "zeta-pack");
        TestPacks.Write(_root.FullName, "alpha-pack");
        TestPacks.Write(_root.FullName, "broken-pack", sellable: false);

        var catalog = Create();

        Assert.Equal(["alpha-pack", "zeta-pack"], catalog.Sellable.Select(p => p.Slug));
        Assert.NotNull(catalog.Find("broken-pack"));
        Assert.False(catalog.Find("broken-pack")!.IsSellable);
        Assert.Null(catalog.Find("missing"));
    }

    [Fact]
    public void Pack_with_traversal_path_in_manifest_is_refused_entirely()
    {
        TestPacks.Write(_root.FullName, "evil", mutate: m => m with
        {
            Models = [m.Models[0] with { Thumbnail = "../../etc/passwd" }],
        });
        TestPacks.Write(_root.FullName, "good");

        var catalog = Create();

        Assert.Null(catalog.Find("evil"));
        Assert.Equal(["good"], catalog.Sellable.Select(p => p.Slug));
    }

    [Fact]
    public void Directory_name_must_match_manifest_slug()
    {
        TestPacks.Write(_root.FullName, "dir-name", mutate: m => m with { Slug = "other-slug" });

        var catalog = Create();

        Assert.Null(catalog.Find("dir-name"));
        Assert.Null(catalog.Find("other-slug"));
    }

    [Fact]
    public void Unreadable_manifest_skips_that_pack_only()
    {
        Directory.CreateDirectory(Path.Combine(_root.FullName, "garbage"));
        File.WriteAllText(Path.Combine(_root.FullName, "garbage", "manifest.json"), "{ not json");
        TestPacks.Write(_root.FullName, "good");

        var catalog = Create();

        Assert.Equal(["good"], catalog.Sellable.Select(p => p.Slug));
    }

    [Fact]
    public void Reload_picks_up_new_packs()
    {
        var catalog = Create();
        Assert.Empty(catalog.Sellable);

        TestPacks.Write(_root.FullName, "late");
        catalog.Reload();

        Assert.Equal(["late"], catalog.Sellable.Select(p => p.Slug));
    }

    [Fact]
    public void Missing_catalog_directory_is_an_empty_catalog_not_a_crash()
    {
        var catalog = new CatalogService(
            Options.Create(new CatalogOptions { Path = Path.Combine(_root.FullName, "does-not-exist") }), NullLogger<CatalogService>.Instance);

        Assert.Empty(catalog.Sellable);
    }

    [Fact]
    public async Task Private_zip_contains_only_private_files_under_a_pack_folder()
    {
        TestPacks.Write(_root.FullName, "props");
        var catalog = Create();
        using var buffer = new MemoryStream();

        await catalog.WritePrivateZipAsync("props", buffer, CancellationToken.None);

        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).Order().ToArray();
        Assert.Equal(["props/chest/chest.fbx", "props/chest/chest.glb"], names);
        using var reader = new StreamReader(zip.GetEntry("props/chest/chest.glb")!.Open());
        Assert.Equal("textured-glb", await reader.ReadToEndAsync());
    }

    [Fact]
    public void PublicFile_resolves_only_public_paths()
    {
        TestPacks.Write(_root.FullName, "props");
        var catalog = Create();

        Assert.NotNull(catalog.PublicFile("props", "thumbs/chest.png"));
        Assert.Null(catalog.PublicFile("props", "../private/chest/chest.glb"));
        Assert.Null(catalog.PublicFile("props", "thumbs/missing.png"));
        Assert.Null(catalog.PublicFile("nope", "thumbs/chest.png"));
    }

    public void Dispose() => _root.Delete(recursive: true);
}
