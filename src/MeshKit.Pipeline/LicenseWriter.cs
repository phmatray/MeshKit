using System.Globalization;
using System.Reflection;
using MeshKit.Core.Catalog;
using MeshKit.Core.Definitions;

namespace MeshKit.Pipeline;

/// <summary>Resolves a pack's licence choice to text and writes it into the pack (public copy for the store page, private copy for the download zip).</summary>
public sealed class LicenseWriter(LicenseWriter.Licensor licensor)
{
    public sealed record Licensor(string Name, string Vat, string StoreUrl)
    {
        public static readonly Licensor AtypicalConsulting = new("Atypical Consulting (Philippe Matray)", "BE 0744.517.956", "https://meshkit.atypical.consulting");
    }

    public const string PublicFile = $"{PackPaths.PublicRoot}/LICENSE.txt";
    public const string PrivateFile = $"{PackPaths.PrivateRoot}/LICENSE.txt";

    private static readonly IReadOnlyDictionary<string, string> BuiltInNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [LicenseChoice.MeshKitStandard] = "MeshKit Royalty-Free Asset Licence",
    };

    public PackLicense Write(PackDefinition definition, string packDirectory, string? definitionDirectory, DateTimeOffset now)
    {
        var (id, name, text) = Resolve(definition.License, definitionDirectory);
        text = text
            .Replace("{{PACK_NAME}}", definition.Name, StringComparison.Ordinal)
            .Replace("{{PACK_SLUG}}", definition.Slug, StringComparison.Ordinal)
            .Replace("{{LICENSOR}}", licensor.Name, StringComparison.Ordinal)
            .Replace("{{LICENSOR_VAT}}", licensor.Vat, StringComparison.Ordinal)
            .Replace("{{STORE_URL}}", licensor.StoreUrl, StringComparison.Ordinal)
            .Replace("{{DATE}}", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);

        foreach (var relative in new[] { PublicFile, PrivateFile })
        {
            var full = PackPaths.Resolve(packDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        return new PackLicense(id, name, PublicFile, PrivateFile);
    }

    private static (string Id, string Name, string Text) Resolve(LicenseChoice choice, string? definitionDirectory)
    {
        if (choice.File is { } file)
        {
            var path = Path.IsPathRooted(file) ? file : Path.Combine(definitionDirectory ?? ".", file);
            if (!File.Exists(path))
            {
                throw new PackDefinitionException($"license.file '{file}' not found (looked at {Path.GetFullPath(path)}).");
            }

            return ("custom", "Custom licence", File.ReadAllText(path));
        }

        if (!BuiltInNames.TryGetValue(choice.Id, out var name))
        {
            throw new PackDefinitionException($"Unknown built-in licence '{choice.Id}'.");
        }

        var resource = $"MeshKit.Pipeline.Licenses.{choice.Id}.txt";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded licence template {resource} is missing.");
        using var reader = new StreamReader(stream);
        return (choice.Id, name, reader.ReadToEnd());
    }
}
