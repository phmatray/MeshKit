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

    private static PackDefinition Definition(params string[] models) => DefinitionWith(GenerationSettings.PreviewClay, models);

    private static PackDefinition DefinitionWith(string preview, params string[] models) => new(
        Slug: "props",
        Name: "Props",
        Description: "d",
        Price: new Price(1900, "eur"),
        Generation: GenerationSettings.Default with { TargetFormats = ["glb", "fbx"], EnablePbr = true, ShouldRemesh = true, TargetPolycount = 4000, Preview = preview },
        Models: models.Select(m => new ModelDefinition(m, m.ToUpperInvariant(), "prompt " + m, m == "chest" ? "oak" : null, m == "chest" ? ["loot", "wood"] : [], null)).ToList(),
        Tags: ["fantasy"],
        Category: "props",
        Style: "lowpoly",
        License: LicenseChoice.Default);

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
        Assert.False(entry.PreviewTextured);
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

        // measured from the glb the fake client "downloaded" (a unit cube)
        var meta = entry.Metadata!;
        Assert.Equal((12, 8, 1.0, 1.0, 1.0), (meta.Triangles, meta.Vertices, meta.Width, meta.Height, meta.Depth));
        Assert.True(meta.Pbr);
        Assert.Equal("2k", meta.TextureResolution);
        Assert.Equal(["base_color"], meta.TextureMaps);
        Assert.Equal(entry.Files.Sum(f => f.Bytes), meta.TotalBytes);
        Assert.Equal(["loot", "wood"], entry.TagList);
        Assert.Equal(["fantasy"], manifest.TagList);

        // licence written twice: viewable before purchase, shipped inside the zip
        Assert.Equal("meshkit-standard", manifest.License!.Id);
        var publicLicense = File.ReadAllText(Path.Combine(PackDir, "public/LICENSE.txt"));
        Assert.Equal(publicLicense, File.ReadAllText(Path.Combine(PackDir, "private/LICENSE.txt")));
        Assert.Contains("Pack:       Props (props)", publicLicense);
        Assert.Contains("BE 0744.517.956", publicLicense);
        Assert.DoesNotContain("{{", publicLicense);

        var preview = Assert.Single(_meshy.PreviewRequests);
        Assert.Equal("standard", preview.ModelType);
        Assert.True(preview.ShouldRemesh);
        Assert.Equal(4000, preview.TargetPolycount);
        Assert.True(preview.AlphaThumbnail);
        Assert.Equal(["glb"], preview.TargetFormats);
        var refine = Assert.Single(_meshy.RefineRequests);
        Assert.True(refine.EnablePbr);
        Assert.Equal("oak", refine.TexturePrompt);
        Assert.Equal(["glb", "fbx"], refine.TargetFormats);
    }

    [Fact]
    public async Task Levers_reach_both_meshy_tasks_and_ultra_is_per_model()
    {
        var definition = Definition("chest", "barrel") with
        {
            Generation = Definition().Generation with { Topology = "quad", UltraMode = true, AutoSize = true, OriginAt = "center" },
        };
        definition = definition with { Models = definition.Models.Select(m => m.Slug == "barrel" ? m with { Ultra = false } : m).ToList() };

        await Generator().GenerateAsync(definition, PackDir, Fast, CancellationToken.None);

        var chest = _meshy.PreviewRequests.Single(r => r.Prompt == "prompt chest");
        var barrel = _meshy.PreviewRequests.Single(r => r.Prompt == "prompt barrel");
        Assert.Equal(("quad", true, true, "center"), (chest.Topology, chest.UltraMode, chest.AutoSize, chest.OriginAt));
        Assert.False(barrel.UltraMode);
        Assert.All(_meshy.RefineRequests, r => Assert.Equal((true, "center", true), (r.AutoSize, r.OriginAt, r.AlphaThumbnail)));
    }

    [Fact]
    public async Task Alpha_thumbnail_is_preferred_when_meshy_returns_one()
    {
        _meshy.Outcomes["prev-prompt chest"] = () => FakeMeshyClient.Succeeded("prev-prompt chest", "glb") with { AlphaThumbnailUrl = "https://cdn.test/alpha.png" };

        await Generator().GenerateAsync(Definition("chest"), PackDir, Fast, CancellationToken.None);

        Assert.Contains(_meshy.Downloads, d => d.Url.ToString() == "https://cdn.test/alpha.png" && d.Path.EndsWith("public/thumbs/chest.png", StringComparison.Ordinal));
        Assert.DoesNotContain(_meshy.Downloads, d => d.Url.ToString() == "https://cdn.test/prev-prompt chest/thumb.png");
    }

    [Fact]
    public async Task Texture_image_is_sent_as_a_data_uri_unless_the_model_has_its_own_prompt()
    {
        var defDir = Directory.CreateDirectory(Path.Combine(_root.FullName, "packs"));
        File.WriteAllBytes(Path.Combine(defDir.FullName, "palette.png"), [0x89, 0x50, 0x4E, 0x47]);
        var definition = Definition("chest", "barrel") with { Generation = Definition().Generation with { TextureImage = "palette.png" } };

        await Generator().GenerateAsync(definition, PackDir, Fast with { DefinitionDirectory = defDir.FullName }, CancellationToken.None);

        var chest = _meshy.RefineRequests.Single(r => r.PreviewTaskId == "prev-prompt chest");
        var barrel = _meshy.RefineRequests.Single(r => r.PreviewTaskId == "prev-prompt barrel");
        Assert.Equal("oak", chest.TexturePrompt);
        Assert.Null(chest.TextureImageUrl);
        Assert.Null(barrel.TexturePrompt);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]), barrel.TextureImageUrl);
    }

    [Fact]
    public async Task Missing_texture_image_fails_before_any_meshy_call()
    {
        var definition = Definition("chest") with { Generation = Definition().Generation with { TextureImage = "missing.png" } };

        await Assert.ThrowsAsync<PackDefinitionException>(() => Generator().GenerateAsync(definition, PackDir, Fast with { DefinitionDirectory = _root.FullName }, CancellationToken.None));
        Assert.Empty(_meshy.PreviewRequests);
    }

    [Fact]
    public async Task Textured_preview_publishes_the_refined_glb_and_refine_thumbnail()
    {
        var manifest = await Generator().GenerateAsync(DefinitionWith(GenerationSettings.PreviewTextured, "chest"), PackDir, Fast, CancellationToken.None);

        var entry = Assert.Single(manifest.Models);
        Assert.True(entry.PreviewTextured);
        Assert.Equal("public/preview/chest.textured.glb", entry.Preview);
        Assert.Equal("public/thumbs/chest.textured.png", entry.Thumbnail);
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(PackDir, "private/chest/chest.glb")),
            await File.ReadAllBytesAsync(Path.Combine(PackDir, "public/preview/chest.textured.glb")));
        Assert.Contains(_meshy.Downloads, d => d.Url.ToString().Contains("ref-prev-prompt chest/thumb.png") && d.Path.EndsWith("chest.textured.png"));
        // The clay assets are kept next to them so the pack can switch modes later without Meshy.
        Assert.True(File.Exists(Path.Combine(PackDir, "public/preview/chest.glb")));
        Assert.True(File.Exists(Path.Combine(PackDir, "public/thumbs/chest.png")));
    }

    [Fact]
    public async Task Switching_an_existing_pack_to_textured_previews_costs_no_generation()
    {
        await Generator().GenerateAsync(DefinitionWith(GenerationSettings.PreviewClay, "chest"), PackDir, Fast, CancellationToken.None);
        _meshy.PreviewRequests.Clear();
        _meshy.RefineRequests.Clear();

        var manifest = await Generator().GenerateAsync(DefinitionWith(GenerationSettings.PreviewTextured, "chest"), PackDir, Fast, CancellationToken.None);

        Assert.Empty(_meshy.PreviewRequests);
        Assert.Empty(_meshy.RefineRequests);
        var entry = Assert.Single(manifest.Models);
        Assert.True(entry.PreviewTextured);
        Assert.Equal(ModelStatus.Succeeded, entry.Status);
        Assert.Equal("public/preview/chest.textured.glb", entry.Preview);
        Assert.Equal("public/thumbs/chest.textured.png", entry.Thumbnail);
        Assert.True(File.Exists(Path.Combine(PackDir, "public/preview/chest.textured.glb")));
        Assert.True(File.Exists(Path.Combine(PackDir, "public/thumbs/chest.textured.png")));
        Assert.Equal(manifest, PackManifestSerializer.ReadFile(Path.Combine(PackDir, PackManifestSerializer.FileName)));
    }

    [Fact]
    public async Task Old_pack_switching_to_textured_fetches_the_refine_thumbnail_with_a_free_get()
    {
        await Generator().GenerateAsync(DefinitionWith(GenerationSettings.PreviewClay, "chest"), PackDir, Fast, CancellationToken.None);
        File.Delete(Path.Combine(PackDir, "public/thumbs/chest.textured.png"));
        File.Delete(Path.Combine(PackDir, "public/preview/chest.textured.glb"));
        _meshy.Downloads.Clear();

        var manifest = await Generator().GenerateAsync(DefinitionWith(GenerationSettings.PreviewTextured, "chest"), PackDir, Fast, CancellationToken.None);

        var entry = Assert.Single(manifest.Models);
        Assert.Equal("public/thumbs/chest.textured.png", entry.Thumbnail);
        Assert.Equal("public/preview/chest.textured.glb", entry.Preview);
        var download = Assert.Single(_meshy.Downloads);
        Assert.Contains("ref-prev-prompt chest/thumb.png", download.Url.ToString());
    }

    [Fact]
    public async Task Old_pack_without_metadata_or_licence_gets_both_on_resume_without_meshy()
    {
        await Generator().GenerateAsync(Definition("chest"), PackDir, Fast, CancellationToken.None);
        var manifestPath = Path.Combine(PackDir, PackManifestSerializer.FileName);
        var old = PackManifestSerializer.ReadFile(manifestPath);
        PackManifestSerializer.WriteFile(manifestPath, old with { License = null, Models = [old.Models[0] with { Metadata = null, Tags = null }] });
        File.Delete(Path.Combine(PackDir, "private/LICENSE.txt"));
        _meshy.PreviewRequests.Clear();

        var manifest = await Generator().GenerateAsync(Definition("chest"), PackDir, Fast, CancellationToken.None);

        Assert.Empty(_meshy.PreviewRequests);
        Assert.Equal(12, manifest.Models[0].Metadata!.Triangles);
        Assert.Equal(["loot", "wood"], manifest.Models[0].TagList);
        Assert.NotNull(manifest.License);
        Assert.True(File.Exists(Path.Combine(PackDir, "private/LICENSE.txt")));
    }

    [Fact]
    public async Task Custom_licence_file_is_resolved_relative_to_the_definition()
    {
        var defDir = Path.Combine(_root.FullName, "packs");
        Directory.CreateDirectory(defDir);
        File.WriteAllText(Path.Combine(defDir, "my-licence.txt"), "Pack {{PACK_NAME}}: do whatever.");
        var def = Definition("chest") with { License = new LicenseChoice("custom", "my-licence.txt") };

        var manifest = await Generator().GenerateAsync(def, PackDir, Fast with { DefinitionDirectory = defDir }, CancellationToken.None);

        Assert.Equal("custom", manifest.License!.Id);
        Assert.Equal("Pack Props: do whatever.", File.ReadAllText(Path.Combine(PackDir, "public/LICENSE.txt")));
    }

    [Fact]
    public async Task Switching_back_to_clay_reuses_the_kept_clay_assets()
    {
        await Generator().GenerateAsync(DefinitionWith(GenerationSettings.PreviewTextured, "chest"), PackDir, Fast, CancellationToken.None);
        _meshy.PreviewRequests.Clear();

        var manifest = await Generator().GenerateAsync(DefinitionWith(GenerationSettings.PreviewClay, "chest"), PackDir, Fast, CancellationToken.None);

        Assert.Empty(_meshy.PreviewRequests);
        var entry = Assert.Single(manifest.Models);
        Assert.False(entry.PreviewTextured);
        Assert.Equal("public/preview/chest.glb", entry.Preview);
        Assert.Equal("public/thumbs/chest.png", entry.Thumbnail);
    }

    [Fact]
    public async Task Textured_switch_falls_back_to_clay_thumbnail_when_refine_task_is_gone()
    {
        await Generator().GenerateAsync(DefinitionWith(GenerationSettings.PreviewClay, "chest"), PackDir, Fast, CancellationToken.None);
        File.Delete(Path.Combine(PackDir, "public/thumbs/chest.textured.png")); // pack produced before textured previews existed
        _meshy.Outcomes["ref-prev-prompt chest"] = () => throw new MeshyApiException(System.Net.HttpStatusCode.NotFound, "Task not found");

        var manifest = await Generator().GenerateAsync(DefinitionWith(GenerationSettings.PreviewTextured, "chest"), PackDir, Fast, CancellationToken.None);

        var entry = Assert.Single(manifest.Models);
        Assert.True(entry.PreviewTextured);
        Assert.Equal("public/preview/chest.textured.glb", entry.Preview);
        Assert.Equal("public/thumbs/chest.png", entry.Thumbnail);
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
