using MeshKit.Core.Catalog;
using MeshKit.Core.Definitions;
using MeshKit.Meshy;
using MeshKit.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeshKit.Pipeline.Tests;

public sealed class PackGeneratorTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("meshkit-gen");
    private readonly FakeMeshyClient _meshy = new();

    private static readonly GeneratorOptions Fast = new(
        Concurrency: 2, PollInterval: TimeSpan.FromMilliseconds(1), TaskTimeout: TimeSpan.FromSeconds(5));

    private static PackDefinition Definition(params string[] models) => new(
        Slug: "props",
        Name: "Props",
        Description: "d",
        Price: new Price(1900, "eur"),
        Generation: GenerationSettings.Default with { TargetFormats = ["glb", "fbx"], EnablePbr = true, ModelType = "lowpoly" },
        Models: models.Select(m => new ModelDefinition(m, m.ToUpperInvariant(), "prompt " + m, m == "chest" ? "oak" : null)).ToList());

    private PackGenerator Generator() => new(_meshy, NullLogger<PackGenerator>.Instance);

    private string PackDir => Path.Combine(_root.FullName, "props");

    [Fact]
    public async Task Happy_path_downloads_preview_and_refined_assets_and_writes_manifest()
    {
        var manifest = await Generator().GenerateAsync(Definition("chest"), PackDir, Fast, CancellationToken.None);

        var entry = Assert.Single(manifest.Models);
        Assert.Equal(ModelStatus.Succeeded, entry.Status);
        Assert.Equal("prev-prompt chest", entry.PreviewTaskId);
        Assert.Equal("ref-prev-prompt chest", entry.RefineTaskId);
        Assert.Equal("public/thumbs/chest.png", entry.Thumbnail);
        Assert.Equal("public/preview/chest.glb", entry.Preview);
        Assert.Equal(30, entry.ConsumedCredits);
        Assert.True(manifest.IsSellable);

        var formats = entry.Files.Select(f => f.Format).Order().ToArray();
        Assert.Equal(["base_color", "fbx", "glb"], formats);
        Assert.Equal("private/chest/chest.glb", entry.Files.Single(f => f.Format == "glb").Path);
        Assert.Equal("private/chest/textures/base_color.png", entry.Files.Single(f => f.Format == "base_color").Path);
        Assert.All(entry.Files, f => Assert.True(File.Exists(Path.Combine(PackDir, f.Path)), f.Path));
        Assert.All(entry.Files, f => Assert.True(f.Bytes > 0));
        Assert.True(File.Exists(Path.Combine(PackDir, "public/thumbs/chest.png")));
        Assert.True(File.Exists(Path.Combine(PackDir, "public/preview/chest.glb")));

        var onDisk = PackManifestSerializer.ReadFile(Path.Combine(PackDir, PackManifestSerializer.FileName));
        Assert.Equal(manifest, onDisk);

        var preview = Assert.Single(_meshy.PreviewRequests);
        Assert.Equal("lowpoly", preview.ModelType);
        Assert.Equal(["glb"], preview.TargetFormats);
        var refine = Assert.Single(_meshy.RefineRequests);
        Assert.True(refine.EnablePbr);
        Assert.Equal("oak", refine.TexturePrompt);
        Assert.Equal(["glb", "fbx"], refine.TargetFormats);
    }

    [Fact]
    public async Task Completed_models_with_files_on_disk_are_skipped_on_rerun()
    {
        await Generator().GenerateAsync(Definition("chest", "barrel"), PackDir, Fast, CancellationToken.None);
        var before = _meshy.PreviewRequests.Count;

        var second = await Generator().GenerateAsync(Definition("chest", "barrel"), PackDir, Fast, CancellationToken.None);

        Assert.Equal(before, _meshy.PreviewRequests.Count);
        Assert.True(second.IsSellable);
    }

    [Fact]
    public async Task Completed_model_whose_files_went_missing_is_regenerated()
    {
        await Generator().GenerateAsync(Definition("chest"), PackDir, Fast, CancellationToken.None);
        File.Delete(Path.Combine(PackDir, "private/chest/chest.glb"));

        await Generator().GenerateAsync(Definition("chest"), PackDir, Fast, CancellationToken.None);

        Assert.Equal(2, _meshy.PreviewRequests.Count);
        Assert.True(File.Exists(Path.Combine(PackDir, "private/chest/chest.glb")));
    }

    [Fact]
    public async Task Failed_model_is_recorded_and_the_others_still_succeed()
    {
        _meshy.Outcomes["prev-prompt barrel"] = () => FakeMeshyClient.Failed("prev-prompt barrel", "moderation");

        var manifest = await Generator().GenerateAsync(Definition("chest", "barrel"), PackDir, Fast, CancellationToken.None);

        var barrel = manifest.Models.Single(m => m.Slug == "barrel");
        Assert.Equal(ModelStatus.Failed, barrel.Status);
        Assert.Contains("moderation", barrel.Error);
        Assert.Equal(ModelStatus.Succeeded, manifest.Models.Single(m => m.Slug == "chest").Status);
        Assert.False(manifest.IsSellable);
    }

    [Fact]
    public async Task Failed_model_is_retried_on_rerun()
    {
        _meshy.Outcomes["prev-prompt barrel"] = () => FakeMeshyClient.Failed("prev-prompt barrel", "moderation");
        await Generator().GenerateAsync(Definition("barrel"), PackDir, Fast, CancellationToken.None);
        _meshy.Outcomes.Clear();

        var manifest = await Generator().GenerateAsync(Definition("barrel"), PackDir, Fast, CancellationToken.None);

        Assert.True(manifest.IsSellable);
        Assert.Null(manifest.Models[0].Error);
    }

    [Fact]
    public async Task Out_of_credits_aborts_the_run_but_keeps_the_manifest_for_resume()
    {
        _meshy.CreateFailures["prompt barrel"] = new MeshyOutOfCreditsException("no credits");

        await Assert.ThrowsAsync<MeshyOutOfCreditsException>(
            () => Generator().GenerateAsync(Definition("chest", "barrel"), PackDir, new GeneratorOptions(1, Fast.PollInterval, Fast.TaskTimeout), CancellationToken.None));

        var onDisk = PackManifestSerializer.ReadFile(Path.Combine(PackDir, PackManifestSerializer.FileName));
        Assert.Equal(ModelStatus.Succeeded, onDisk.Models.Single(m => m.Slug == "chest").Status);
        Assert.Equal(ModelStatus.Pending, onDisk.Models.Single(m => m.Slug == "barrel").Status);
    }

    [Fact]
    public async Task Concurrency_is_bounded()
    {
        var options = new GeneratorOptions(2, Fast.PollInterval, Fast.TaskTimeout);

        await Generator().GenerateAsync(Definition("a", "b", "c", "d", "e"), PackDir, options, CancellationToken.None);

        Assert.InRange(_meshy.MaxObservedConcurrency, 1, 2);
    }

    [Fact]
    public async Task Definition_metadata_wins_over_stale_manifest_metadata()
    {
        await Generator().GenerateAsync(Definition("chest"), PackDir, Fast, CancellationToken.None);

        var renamed = Definition("chest") with { Name = "Renamed", Price = new Price(2900, "usd") };
        var manifest = await Generator().GenerateAsync(renamed, PackDir, Fast, CancellationToken.None);

        Assert.Equal("Renamed", manifest.Name);
        Assert.Equal(2900, manifest.Price.Amount);
        Assert.Single(_meshy.PreviewRequests);
    }

    [Fact]
    public async Task Model_removed_from_definition_disappears_from_manifest()
    {
        await Generator().GenerateAsync(Definition("chest", "barrel"), PackDir, Fast, CancellationToken.None);

        var manifest = await Generator().GenerateAsync(Definition("chest"), PackDir, Fast, CancellationToken.None);

        Assert.Equal(["chest"], manifest.Models.Select(m => m.Slug));
    }

    public void Dispose() => _root.Delete(recursive: true);
}
