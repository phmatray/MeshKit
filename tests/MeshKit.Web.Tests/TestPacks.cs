using MeshKit.Core.Catalog;
using MeshKit.Core.Definitions;

namespace MeshKit.Web.Tests;

/// <summary>Writes realistic pack directories (manifest + public + private files) into a temp catalog.</summary>
public static class TestPacks
{
    /// <summary>A richer pack for search tests: several named, tagged, measured models.</summary>
    public static PackManifest WriteRich(string catalogRoot, string slug, string name, string description, string category, string style, string[] packTags, long amount,
        params (string Slug, string Name, string Prompt, string[] Tags, string? Category, int Tris, string[] Formats)[] models)
    {
        var dir = Path.Combine(catalogRoot, slug);
        Directory.CreateDirectory(Path.Combine(dir, "public", "thumbs"));
        Directory.CreateDirectory(Path.Combine(dir, "public", "preview"));
        var entries = new List<ModelEntry>();
        foreach (var m in models)
        {
            Directory.CreateDirectory(Path.Combine(dir, "private", m.Slug));
            File.WriteAllText(Path.Combine(dir, "public", "thumbs", $"{m.Slug}.png"), "png");
            File.WriteAllText(Path.Combine(dir, "public", "preview", $"{m.Slug}.glb"), "glb");
            var files = m.Formats.Select(f =>
            {
                File.WriteAllText(Path.Combine(dir, "private", m.Slug, $"{m.Slug}.{f}"), f);
                return new ModelFile(f, $"private/{m.Slug}/{m.Slug}.{f}", f.Length);
            }).ToList();
            entries.Add(new ModelEntry(m.Slug, m.Name, m.Prompt, ModelStatus.Succeeded, null, "p", "r",
                $"public/thumbs/{m.Slug}.png", $"public/preview/{m.Slug}.glb", files, 30, true, m.Tags, m.Category,
                new ModelMetadata(m.Tris, m.Tris / 2, 1, 1, 1, true, "2k", ["base_color"], 100)));
        }

        var manifest = new PackManifest(PackManifest.CurrentSchemaVersion, slug, name, description, new Price(amount, "eur"), DateTimeOffset.UnixEpoch, entries, packTags, category, style, null);
        PackManifestSerializer.WriteFile(Path.Combine(dir, PackManifestSerializer.FileName), manifest);
        return manifest;
    }

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
