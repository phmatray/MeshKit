namespace MeshKit.Core.Definitions;

/// <summary>
/// What the producer writes in <c>packs/&lt;slug&gt;.yaml</c>: the commercial identity of a pack
/// and the prompts that generate its models. Immutable; validated by <see cref="PackDefinitionValidator"/>.
/// </summary>
public sealed record PackDefinition(
    string Slug,
    string Name,
    string Description,
    Price Price,
    GenerationSettings Generation,
    IReadOnlyList<ModelDefinition> Models);

public sealed record ModelDefinition(
    string Slug,
    string Name,
    string Prompt,
    string? TexturePrompt);

/// <summary>Price in minor units (cents) with an ISO 4217 code in lowercase, as Stripe expects.</summary>
public sealed record Price(long Amount, string Currency);

/// <summary>Meshy generation parameters shared by every model of the pack.</summary>
/// <param name="Preview">
/// What the store shows for free in the 3D viewer: <c>textured</c> (the refined model — what buyers
/// actually judge; rippable from the viewer, which is the industry norm) or <c>clay</c> (the untextured
/// preview-stage mesh, nothing of the paid asset leaves the server).
/// </param>
public sealed record GenerationSettings(
    string AiModel,
    string ModelType,
    int? TargetPolycount,
    bool EnablePbr,
    string TextureResolution,
    IReadOnlyList<string> TargetFormats,
    string Preview)
{
    public const string PreviewTextured = "textured";
    public const string PreviewClay = "clay";

    public static readonly GenerationSettings Default = new(
        AiModel: "latest",
        ModelType: "standard",
        TargetPolycount: null,
        EnablePbr: false,
        TextureResolution: "2k",
        TargetFormats: ["glb"],
        Preview: PreviewTextured);

    public static readonly IReadOnlySet<string> KnownPreviews =
        new HashSet<string>(StringComparer.Ordinal) { PreviewTextured, PreviewClay };

    public static readonly IReadOnlySet<string> KnownAiModels =
        new HashSet<string>(StringComparer.Ordinal) { "latest", "meshy-5", "meshy-6", "meshy-7" };

    public static readonly IReadOnlySet<string> KnownModelTypes =
        new HashSet<string>(StringComparer.Ordinal) { "standard", "smart-topology", "lowpoly" };

    public static readonly IReadOnlySet<string> KnownTextureResolutions =
        new HashSet<string>(StringComparer.Ordinal) { "2k", "4k", "8k" };

    public static readonly IReadOnlySet<string> KnownFormats =
        new HashSet<string>(StringComparer.Ordinal) { "glb", "fbx", "obj", "stl", "usdz", "3mf" };
}
