using MeshKit.Core.Catalog;
using MeshKit.Core.Definitions;

namespace MeshKit.Web.Tests;

/// <summary>Writes realistic pack directories (manifest + public + private files) into a temp catalog.</summary>
public static class TestPacks
{
    public static PackManifest Write(string catalogRoot, string slug, bool sellable = true, long amount = 1900, string currency = "eur", Func<PackManifest, PackManifest>? mutate = null)
    {
        var dir = Path.Combine(catalogRoot, slug);
        Directory.CreateDirectory(Path.Combine(dir, "public", "thumbs"));
        Directory.CreateDirectory(Path.Combine(dir, "public", "preview"));
        Directory.CreateDirectory(Path.Combine(dir, "private", "chest"));
        File.WriteAllText(Path.Combine(dir, "public", "thumbs", "chest.png"), "png");
        File.WriteAllText(Path.Combine(dir, "public", "preview", "chest.glb"), "preview-glb");
        File.WriteAllText(Path.Combine(dir, "private", "chest", "chest.glb"), "textured-glb");
        File.WriteAllText(Path.Combine(dir, "private", "chest", "chest.fbx"), "fbx");

        var entry = new ModelEntry(
            Slug: "chest", Name: "Chest", Prompt: "a chest",
            Status: sellable ? ModelStatus.Succeeded : ModelStatus.Failed,
            Error: sellable ? null : "boom",
            PreviewTaskId: "p", RefineTaskId: "r",
            Thumbnail: "public/thumbs/chest.png", Preview: "public/preview/chest.glb",
            Files: [new ModelFile("glb", "private/chest/chest.glb", 12), new ModelFile("fbx", "private/chest/chest.fbx", 3)],
            ConsumedCredits: 30);

        var manifest = new PackManifest(
            PackManifest.CurrentSchemaVersion, slug, $"Pack {slug}", $"Description of {slug}",
            new Price(amount, currency), DateTimeOffset.UnixEpoch, [entry]);
        manifest = mutate?.Invoke(manifest) ?? manifest;
        PackManifestSerializer.WriteFile(Path.Combine(dir, PackManifestSerializer.FileName), manifest);
        return manifest;
    }
}
