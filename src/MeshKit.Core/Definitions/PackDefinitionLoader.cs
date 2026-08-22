using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MeshKit.Core.Definitions;

/// <summary>
/// Reads a pack YAML into a <see cref="PackDefinition"/>. Only shape is checked here (required
/// fields present); semantic rules live in <see cref="PackDefinitionValidator"/>.
/// </summary>
public static class PackDefinitionLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static PackDefinition LoadFile(string path)
    {
        try
        {
            return Load(File.ReadAllText(path));
        }
        catch (IOException ex)
        {
            throw new PackDefinitionException($"Cannot read pack definition '{path}': {ex.Message}", ex);
        }
    }

    public static PackDefinition Load(string yaml)
    {
        PackYaml raw;
        try
        {
            raw = Deserializer.Deserialize<PackYaml>(yaml) ?? throw new PackDefinitionException("Pack definition is empty.");
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new PackDefinitionException($"Pack definition is not valid YAML: {ex.Message}", ex);
        }

        var gen = raw.Generation;
        var defaults = GenerationSettings.Default;
        var generation = gen is null
            ? defaults
            : new GenerationSettings(
                AiModel: gen.AiModel ?? defaults.AiModel,
                ModelType: gen.ModelType ?? defaults.ModelType,
                TargetPolycount: gen.TargetPolycount,
                EnablePbr: gen.EnablePbr ?? defaults.EnablePbr,
                TextureResolution: gen.TextureResolution ?? defaults.TextureResolution,
                TargetFormats: gen.TargetFormats is { Count: > 0 } ? gen.TargetFormats : defaults.TargetFormats);

        var models = (raw.Models ?? []).Select((m, i) => new ModelDefinition(
            Slug: Require(m.Slug, $"models[{i}].slug"),
            Name: Require(m.Name, $"models[{i}].name"),
            Prompt: Require(m.Prompt, $"models[{i}].prompt"),
            TexturePrompt: string.IsNullOrWhiteSpace(m.TexturePrompt) ? null : m.TexturePrompt)).ToList();

        return new PackDefinition(
            Slug: Require(raw.Slug, "slug"),
            Name: Require(raw.Name, "name"),
            Description: raw.Description ?? string.Empty,
            Price: new Price(
                raw.Price?.Amount ?? throw new PackDefinitionException("Missing required field 'price.amount'."),
                Require(raw.Price.Currency, "price.currency")),
            Generation: generation,
            Models: models);
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new PackDefinitionException($"Missing required field '{field}'.")
            : value.Trim();

    // YAML shape — mutable, nullable, deliberately dumb. Converted to the immutable record above.
    private sealed class PackYaml
    {
        public string? Slug { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public PriceYaml? Price { get; set; }
        public GenerationYaml? Generation { get; set; }
        public List<ModelYaml>? Models { get; set; }
    }

    private sealed class PriceYaml
    {
        public long? Amount { get; set; }
        public string? Currency { get; set; }
    }

    private sealed class GenerationYaml
    {
        public string? AiModel { get; set; }
        public string? ModelType { get; set; }
        public int? TargetPolycount { get; set; }
        public bool? EnablePbr { get; set; }
        public string? TextureResolution { get; set; }
        public List<string>? TargetFormats { get; set; }
    }

    private sealed class ModelYaml
    {
        public string? Slug { get; set; }
        public string? Name { get; set; }
        public string? Prompt { get; set; }
        public string? TexturePrompt { get; set; }
    }
}
