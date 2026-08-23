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
        Models: [new ModelDefinition("chest", "Chest", "a chest", null), new ModelDefinition("barrel", "Barrel", "a barrel", null)]);

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
    }

    [Fact]
    public void Json_round_trip_preserves_everything()
    {
        var manifest = PackManifest.FromDefinition(Definition(), DateTimeOffset.UnixEpoch) with
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
                    PreviewTextured: true),
            ],
        };

        var json = PackManifestSerializer.Serialize(manifest);
        var back = PackManifestSerializer.Deserialize(json);

        Assert.Equal(manifest, back);
        Assert.Contains("\"status\": \"succeeded\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"previewTextured\": true", json);
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

        var unsafePaths = PackPaths.UnsafePaths(manifest with { Models = [bad] });

        Assert.Equal(["../evil.png", "/etc/passwd"], unsafePaths);
    }
}
