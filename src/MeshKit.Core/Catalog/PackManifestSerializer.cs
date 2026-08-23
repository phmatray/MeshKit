using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeshKit.Core.Catalog;

public static class PackManifestSerializer
{
    public const string FileName = "manifest.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(PackManifest manifest) => JsonSerializer.Serialize(manifest, Options);

    public static PackManifest Deserialize(string json)
    {
        PackManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PackManifest>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new PackManifestException($"Manifest is not valid JSON: {ex.Message}", ex);
        }

        if (manifest is null)
        {
            throw new PackManifestException("Manifest is empty.");
        }

        if (manifest.SchemaVersion != PackManifest.CurrentSchemaVersion)
        {
            throw new PackManifestException(
                $"Manifest schema version {manifest.SchemaVersion} is not supported (expected {PackManifest.CurrentSchemaVersion}).");
        }

        return manifest with { Models = manifest.Models ?? [] };
    }

    public static PackManifest ReadFile(string path)
    {
        try
        {
            return Deserialize(File.ReadAllText(path));
        }
        catch (IOException ex)
        {
            throw new PackManifestException($"Cannot read manifest '{path}': {ex.Message}", ex);
        }
    }

    /// <summary>Writes atomically (temp file + rename) so a crash mid-write never leaves a truncated manifest.</summary>
    public static void WriteFile(string path, PackManifest manifest)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(manifest));
        File.Move(temp, path, overwrite: true);
    }
}
