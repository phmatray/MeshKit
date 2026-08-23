using MeshKit.Core.Catalog;
using MeshKit.Core.Definitions;

namespace MeshKit.Core.Tests;

public class PackManifestTests
{
    private static PackDefinition Definition() => new(
        Slug: "props",
        Name: "Props",
        Description: "Some props",
        Price: new Price(1900, "eur"),
        Generation: GenerationSettings.Default,
        Models: [new ModelDefinition("chest", "Chest", "a chest", null, ["loot"], "furniture"), new ModelDefinition("barrel", "Barrel", "a barrel", null, [], null)],
        Tags: ["fantasy"],
        Category: "props",
        Style: "lowpoly",
        License: LicenseChoice.Default);

    [Fact]
    public void FromDefinition_creates_pending_entries_for_every_model()
    {
        var manifest = PackManifest.FromDefinition(Definition(), new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("props", manifest.Slug);
        Assert.Equal(1900, manifest.Price.Amount);
        Assert.Equal(2, manifest.Models.Count);
        Assert.All(manifest.Models, m => Assert.Equal(ModelStatus.Pending, m.Status));
        Assert.False(manifest.IsSellable);
        Assert.Equal(["fantasy"], manifest.TagList);
        Assert.Equal(("props", "lowpoly"), (manifest.Category, manifest.Style));
        Assert.Equal(["loot"], manifest.Models[0].TagList);
        Assert.Equal("furniture", manifest.Models[0].Category);
        Assert.Null(manifest.License);
    }

    [Fact]
    public void Lods_round_trip_and_AllFiles_includes_them()
    {
        var entry = PackManifest.FromDefinition(Definition(), DateTimeOffset.UnixEpoch).Models[0] with
        {
            Status = ModelStatus.Succeeded,
            Files = [new ModelFile("glb", "private/chest/chest.glb", 10)],
            Lods = [new ModelLod(1, 2000, "lod-1", [new ModelFile("glb", "private/chest/lod1/chest_lod1.glb", 5)], 1990, 5)],
            Variants = [new ModelVariant("snow", "Snow", "rt-1", [new ModelFile("glb", "private/chest/variants/snow/chest_snow.glb", 7)], "public/thumbs/chest.snow.png", "public/preview/chest.snow.glb", 10)],
        };
        var manifest = PackManifest.FromDefinition(Definition(), DateTimeOffset.UnixEpoch) with { Models = [entry] };

        var back = PackManifestSerializer.Deserialize(PackManifestSerializer.Serialize(manifest));

        Assert.Equal(manifest, back);
        var lod = Assert.Single(back.Models[0].LodList);
        Assert.Equal((1, 2000, 1990, 5), (lod.Level, lod.TargetPolycount, lod.Triangles, lod.ConsumedCredits));
        Assert.Equal(["private/chest/chest.glb", "private/chest/lod1/chest_lod1.glb", "private/chest/variants/snow/chest_snow.glb"], back.Models[0].AllFiles.Select(f => f.Path));
        Assert.Equal(("snow", "Snow", "public/preview/chest.snow.glb"), (back.Models[0].VariantList[0].Slug, back.Models[0].VariantList[0].Name, back.Models[0].VariantList[0].Preview));
        Assert.NotEqual(manifest, manifest with { Models = [entry with { Lods = null }] });
    }

    [Fact]
    public void Json_round_trip_preserves_everything()
    {
        var manifest = PackManifest.FromDefinition(Definition() with { Sample = "chest" }, DateTimeOffset.UnixEpoch) with
        {
            Models =
            [
                new ModelEntry(
                    Slug: "chest", Name: "Chest", Prompt: "a chest",
                    Status: ModelStatus.Succeeded, Error: null,
                    PreviewTaskId: "p1", RefineTaskId: "r1",
                    Thumbnail: "public/thumbs/chest.png", Preview: "public/preview/chest.glb",
                    Files: [new ModelFile("glb", "private/chest/chest.glb", 1234)],
                    ConsumedCredits: 30,
                    PreviewTextured: true,
                    Tags: ["loot"],
                    Category: "furniture",
                    Metadata: new ModelMetadata(1200, 640, 1.2, 0.8, 0.9, true, "2k", ["base_color", "normal"], 123456)),
            ],
            License = new PackLicense("meshkit-standard", "MeshKit Royalty-Free Licence", "public/LICENSE.txt", "private/LICENSE.txt"),
        };

        var json = PackManifestSerializer.Serialize(manifest);
        var back = PackManifestSerializer.Deserialize(json);

        Assert.Equal(manifest, back);
        Assert.Equal("chest", back.Sample);
        Assert.Contains("\"status\": \"succeeded\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"previewTextured\": true", json);
        Assert.Contains("\"triangles\": 1200", json);
        Assert.Equal(1200, back.Models[0].Metadata!.Triangles);
        Assert.Equal("private/LICENSE.txt", back.License!.PrivateFile);
    }

    [Fact]
    public void Manifests_written_before_previewTextured_existed_still_load()
    {
        var json = System.Text.RegularExpressions.Regex.Replace(
            PackManifestSerializer.Serialize(PackManifest.FromDefinition(Definition(), DateTimeOffset.UnixEpoch)),
            ",\\s*\"previewTextured\": false", "");
        Assert.DoesNotContain("previewTextured", json);

        var back = PackManifestSerializer.Deserialize(json);

        Assert.All(back.Models, m => Assert.False(m.PreviewTextured));
    }

    [Fact]
    public void IsSellable_requires_every_model_succeeded()
    {
        var manifest = PackManifest.FromDefinition(Definition(), DateTimeOffset.UnixEpoch);
        var ok = manifest.Models[0] with { Status = ModelStatus.Succeeded };
        var failed = manifest.Models[1] with { Status = ModelStatus.Failed, Error = "boom" };

        Assert.False((manifest with { Models = [ok, failed] }).IsSellable);
        Assert.True((manifest with { Models = [ok, manifest.Models[1] with { Status = ModelStatus.Succeeded }] }).IsSellable);
        Assert.False((manifest with { Models = [] }).IsSellable);
    }

    [Fact]
    public void Deserialize_rejects_unknown_schema_version()
    {
        var ex = Assert.Throws<PackManifestException>(() => PackManifestSerializer.Deserialize("""{"schemaVersion": 99, "slug": "x"}"""));
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Deserialize_rejects_garbage()
    {
        Assert.Throws<PackManifestException>(() => PackManifestSerializer.Deserialize("not json"));
    }

    [Theory]
    [InlineData("public/thumbs/a.png", true)]
    [InlineData("private/a/a.glb", true)]
    [InlineData("../x", false)]
    [InlineData("/abs/x", false)]
    [InlineData("C:\\x", false)]
    [InlineData("private/../../x", false)]
    [InlineData("private/..", false)]
    [InlineData("public\\a.png", false)]
    [InlineData("other/a.png", false)]
    [InlineData("public", false)]
    [InlineData("", false)]
    public void IsSafeRelative_confines_paths_to_public_or_private(string path, bool expected)
    {
        Assert.Equal(expected, PackPaths.IsSafeRelative(path));
    }

    [Fact]
    public void UnsafePaths_lists_every_offending_path_in_a_manifest()
    {
        var manifest = PackManifest.FromDefinition(Definition(), DateTimeOffset.UnixEpoch);
        var bad = manifest.Models[0] with
        {
            Thumbnail = "../evil.png",
            Preview = "public/preview/ok.glb",
            Files = [new ModelFile("glb", "/etc/passwd", 1)],
        };

        var unsafePaths = PackPaths.UnsafePaths(manifest with
        {
            Models = [bad],
            License = new PackLicense("x", "X", "../LICENSE.txt", "private/LICENSE.txt"),
        });

        Assert.Equal(["../evil.png", "/etc/passwd", "../LICENSE.txt"], unsafePaths);
    }
}
