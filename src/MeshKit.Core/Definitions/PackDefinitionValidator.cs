using System.Text.RegularExpressions;

namespace MeshKit.Core.Definitions;

/// <summary>Semantic rules a pack must satisfy before the pipeline spends Meshy credits on it.</summary>
public static partial class PackDefinitionValidator
{
    /// <summary>Meshy's documented maximum for <c>prompt</c> and <c>texture_prompt</c>.</summary>
    public const int MaxPromptLength = 600;

    [GeneratedRegex("^[a-z]{3}$")]
    private static partial Regex CurrencyPattern();

    /// <summary>Returns every violation; an empty list means the definition is valid.</summary>
    public static IReadOnlyList<string> Validate(PackDefinition pack)
    {
        var errors = new List<string>();

        if (!Slug.IsValid(pack.Slug))
        {
            errors.Add($"Pack slug '{pack.Slug}' is invalid: use lowercase letters, digits and single dashes.");
        }

        if (pack.Price.Amount <= 0)
        {
            errors.Add($"price.amount must be positive (got {pack.Price.Amount}).");
        }

        if (!CurrencyPattern().IsMatch(pack.Price.Currency))
        {
            errors.Add($"price.currency '{pack.Price.Currency}' must be a three-letter lowercase ISO 4217 code.");
        }

        ValidateGeneration(pack.Generation, errors);

        if (!GenerationSettings.KnownCategories.Contains(pack.Category))
        {
            errors.Add($"category '{pack.Category}' is unknown (expected one of {Join(GenerationSettings.KnownCategories)}).");
        }

        if (!GenerationSettings.KnownStyles.Contains(pack.Style))
        {
            errors.Add($"style '{pack.Style}' is unknown (expected one of {Join(GenerationSettings.KnownStyles)}).");
        }

        ValidateTags(pack.Tags, "tags", errors);

        if (pack.License.File is null && !LicenseChoice.BuiltIn.Contains(pack.License.Id))
        {
            errors.Add($"license.id '{pack.License.Id}' is unknown (expected one of {Join(LicenseChoice.BuiltIn)}, or license.file).");
        }

        if (pack.Models.Count == 0)
        {
            errors.Add("A pack needs at least one model.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in pack.Models)
        {
            if (!Slug.IsValid(model.Slug))
            {
                errors.Add($"Model slug '{model.Slug}' is invalid: use lowercase letters, digits and single dashes.");
            }
            else if (!seen.Add(model.Slug))
            {
                errors.Add($"Duplicate model slug '{model.Slug}'.");
            }

            if (model.Prompt.Length > MaxPromptLength)
            {
                errors.Add($"Model '{model.Slug}': prompt is {model.Prompt.Length} characters, Meshy allows at most {MaxPromptLength}.");
            }

            ValidateTags(model.Tags, $"models[{model.Slug}].tags", errors);
            if (model.Category is not null && !GenerationSettings.KnownCategories.Contains(model.Category))
            {
                errors.Add($"Model '{model.Slug}': category '{model.Category}' is unknown (expected one of {Join(GenerationSettings.KnownCategories)}).");
            }

            if (model.Ultra == true && !GenerationSettings.UltraCapableAiModels.Contains(pack.Generation.AiModel))
            {
                errors.Add($"Model '{model.Slug}': ultra needs ai_model 'latest' or 'meshy-7' (got '{pack.Generation.AiModel}').");
            }

            if (model.TexturePrompt is { Length: > MaxPromptLength })
            {
                errors.Add($"Model '{model.Slug}': texture_prompt is {model.TexturePrompt.Length} characters, Meshy allows at most {MaxPromptLength}.");
            }
        }

        if (pack.Sample is { } sample && !pack.Models.Any(m => m.Slug == sample))
        {
            errors.Add($"sample '{sample}' is not one of the pack's models (expected one of {Join(pack.Models.Select(m => m.Slug))}).");
        }

        return errors;
    }

    public const int MaxTags = 20;

    private static void ValidateTags(IReadOnlyList<string> tags, string field, List<string> errors)
    {
        if (tags.Count > MaxTags)
        {
            errors.Add($"{field}: at most {MaxTags} tags (got {tags.Count}).");
        }

        foreach (var tag in tags.Where(t => !Slug.IsValid(t)))
        {
            errors.Add($"{field}: tag '{tag}' is invalid: use lowercase letters, digits and single dashes.");
        }
    }

    private static void ValidateGeneration(GenerationSettings gen, List<string> errors)
    {
        if (!GenerationSettings.KnownAiModels.Contains(gen.AiModel))
        {
            errors.Add($"generation.ai_model '{gen.AiModel}' is unknown (expected one of {Join(GenerationSettings.KnownAiModels)}).");
        }

        if (gen.ModelType == GenerationSettings.ModelTypeLowpoly)
        {
            errors.Add("generation.model_type 'lowpoly' is deprecated by Meshy: use 'standard' with should_remesh: true and a target_polycount, or 'smart-topology' with ai_model: meshy-t2.");
        }
        else if (!GenerationSettings.KnownModelTypes.Contains(gen.ModelType))
        {
            errors.Add($"generation.model_type '{gen.ModelType}' is unknown (expected one of {Join(GenerationSettings.KnownModelTypes)}).");
        }
        else if (gen.ModelType == GenerationSettings.ModelTypeSmartTopology && gen.AiModel != GenerationSettings.AiModelSmartTopology)
        {
            errors.Add($"generation.ai_model must be '{GenerationSettings.AiModelSmartTopology}' when model_type is 'smart-topology' (got '{gen.AiModel}').");
        }
        else if (gen.ModelType == GenerationSettings.ModelTypeStandard && gen.AiModel == GenerationSettings.AiModelSmartTopology)
        {
            errors.Add($"generation.ai_model '{GenerationSettings.AiModelSmartTopology}' only serves model_type 'smart-topology'.");
        }

        if (gen.TargetPolycount is not null && !gen.ShouldRemesh && gen.ModelType != GenerationSettings.ModelTypeSmartTopology)
        {
            errors.Add("generation.target_polycount is ignored by Meshy unless should_remesh: true (remesh is off by default on Meshy 6/7).");
        }

        if (!GenerationSettings.KnownTopologies.Contains(gen.Topology))
        {
            errors.Add($"generation.topology '{gen.Topology}' is unknown (expected one of {Join(GenerationSettings.KnownTopologies)}).");
        }

        if (!GenerationSettings.KnownOrigins.Contains(gen.OriginAt))
        {
            errors.Add($"generation.origin_at '{gen.OriginAt}' is unknown (expected one of {Join(GenerationSettings.KnownOrigins)}).");
        }

        if (gen.UltraMode && !GenerationSettings.UltraCapableAiModels.Contains(gen.AiModel))
        {
            errors.Add($"generation.ultra_mode needs ai_model 'latest' or 'meshy-7' (got '{gen.AiModel}').");
        }

        var lods = gen.LodLevels;
        if (lods.Count > GenerationSettings.MaxLods)
        {
            errors.Add($"generation.lods: at most {GenerationSettings.MaxLods} levels (got {lods.Count}).");
        }

        if (lods.Distinct().Count() != lods.Count)
        {
            errors.Add("generation.lods: levels must be distinct.");
        }

        foreach (var lod in lods.Where(l => l < 100))
        {
            errors.Add($"generation.lods: level {lod} is below Meshy's minimum of 100 polygons.");
        }

        if (gen.TargetPolycount is { } budget)
        {
            foreach (var lod in lods.Where(l => l >= budget))
            {
                errors.Add($"generation.lods: level {lod} is not below target_polycount {budget}; a LOD must be lighter than the full model.");
            }
        }

        if (gen.TextureImage is { } image && !IsRelativeInsideDefinitionDirectory(image))
        {
            errors.Add($"generation.texture_image '{image}' must be a relative path inside the packs directory (no '..', no absolute path).");
        }

        if (!GenerationSettings.KnownTextureResolutions.Contains(gen.TextureResolution))
        {
            errors.Add($"generation.texture_resolution '{gen.TextureResolution}' is unknown (expected one of {Join(GenerationSettings.KnownTextureResolutions)}).");
        }

        if (!GenerationSettings.KnownPreviews.Contains(gen.Preview))
        {
            errors.Add($"generation.preview '{gen.Preview}' is unknown (expected one of {Join(GenerationSettings.KnownPreviews)}).");
        }

        if (gen.TargetPolycount is { } polycount && polycount < 100)
        {
            errors.Add($"generation.target_polycount must be at least 100 (got {polycount}).");
        }

        foreach (var format in gen.TargetFormats.Where(f => !GenerationSettings.KnownFormats.Contains(f)))
        {
            errors.Add($"generation.target_formats contains unknown format '{format}' (expected a subset of {Join(GenerationSettings.KnownFormats)}).");
        }

        if (!gen.TargetFormats.Contains("glb"))
        {
            errors.Add("generation.target_formats must include 'glb': the in-browser preview needs it.");
        }
    }

    /// <summary>Relative, forward-slash, never climbing above the directory of the pack YAML.</summary>
    private static bool IsRelativeInsideDefinitionDirectory(string path) =>
        !path.Contains('\\') && !path.Contains(':') && !path.StartsWith('/')
        && path.Split('/').All(segment => segment is not ("" or "." or ".."));

    private static string Join(IEnumerable<string> values) => string.Join(", ", values.Order(StringComparer.Ordinal));
}
