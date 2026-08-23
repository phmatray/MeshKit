namespace MeshKit.Core.Definitions;

/// <summary>
/// What the producer writes in <c>packs/&lt;slug&gt;.yaml</c>: the commercial identity of a pack
/// and the prompts that generate its models. Immutable; validated by <see cref="PackDefinitionValidator"/>.
/// </summary>
/// <param name="Sample">
/// Slug of the one model anyone with an account may download for free — "try before you buy": the
/// objection to a €19 pack is "does it hold up in Blender/Unity", and a preview in the browser cannot answer it.
/// </param>
public sealed record PackDefinition(
    string Slug,
    string Name,
    string Description,
    Price Price,
    GenerationSettings Generation,
    IReadOnlyList<ModelDefinition> Models,
    IReadOnlyList<string> Tags,
    string Category,
    string Style,
    LicenseChoice License,
    string? Sample = null,
    IReadOnlyList<VariantDefinition>? Variants = null)
{
    /// <summary>Texture variants applied to every model (Meshy Retexture, 10 credits per model and variant). Never null.</summary>
    public IReadOnlyList<VariantDefinition> VariantList => Variants ?? [];

    public const int MaxVariants = 3;
}

/// <summary>
/// One alternative texture set for the whole pack — "weathered", "snow", "faction red" — described by a style
/// prompt. Same meshes, same UVs, a different skin: level designers get variety without new geometry.
/// </summary>
public sealed record VariantDefinition(string Slug, string Name, string Prompt);

/// <param name="Tags">Model-level tags; pack tags are inherited by every model at index time.</param>
/// <param name="TexturePrompt">Overrides the pack's <c>texture_image</c> for this model (Meshy accepts one or the other, never both).</param>
/// <param name="Ultra">Overrides <see cref="GenerationSettings.UltraMode"/> for this model: hero pieces on, filler off.</param>
public sealed record ModelDefinition(
    string Slug,
    string Name,
    string Prompt,
    string? TexturePrompt,
    IReadOnlyList<string> Tags,
    string? Category,
    bool? Ultra = null);

/// <summary>
/// Either a built-in template id (<see cref="MeshKitStandard"/>) or a path to a custom text file,
/// relative to the pack definition. The pipeline writes the resolved text as <c>LICENSE.txt</c>.
/// </summary>
public sealed record LicenseChoice(string Id, string? File)
{
    public const string MeshKitStandard = "meshkit-standard";

    public static readonly IReadOnlySet<string> BuiltIn = new HashSet<string>(StringComparer.Ordinal) { MeshKitStandard };

    public static readonly LicenseChoice Default = new(MeshKitStandard, null);
}

/// <summary>Price in minor units (cents) with an ISO 4217 code in lowercase, as Stripe expects.</summary>
public sealed record Price(long Amount, string Currency);

/// <summary>Meshy generation parameters shared by every model of the pack.</summary>
/// <param name="Preview">
/// What the store shows for free in the 3D viewer: <c>textured</c> (the refined model — what buyers
/// actually judge; rippable from the viewer, which is the industry norm) or <c>clay</c> (the untextured
/// preview-stage mesh, nothing of the paid asset leaves the server).
/// </param>
/// <param name="ShouldRemesh">
/// Meshy only honours <see cref="TargetPolycount"/> and <see cref="Topology"/> during its remesh phase, which is
/// off by default on Meshy 6/7. The first fantasy pack shipped a 22,463-triangle campfire under a 4,000 budget for
/// exactly this reason.
/// </param>
/// <param name="Topology"><c>triangle</c> (decimated — what the store counts) or <c>quad</c> (quad-dominant, for editing).</param>
/// <param name="UltraMode">Finer preview geometry; +5 credits per model, Meshy 7 only. Per-model override: <see cref="ModelDefinition.Ultra"/>.</param>
/// <param name="AutoSize">Let Meshy estimate real-world size so a chest is chest-sized in the engine, not 1.9 m wide.</param>
/// <param name="OriginAt">Pivot placement when <see cref="AutoSize"/> is on: <c>bottom</c> (floor-level, game-ready) or <c>center</c>.</param>
/// <param name="AlphaThumbnail">Transparent-background thumbnails (free). On by default: the store renders them on its own surfaces.</param>
/// <param name="TextureImage">
/// Path, relative to the pack YAML, of one reference image that textures every model — the lever for a coherent palette
/// across a pack. A model's <c>texture_prompt</c> replaces it for that model.
/// </param>
public sealed record GenerationSettings(
    string AiModel,
    string ModelType,
    int? TargetPolycount,
    bool EnablePbr,
    string TextureResolution,
    IReadOnlyList<string> TargetFormats,
    string Preview,
    bool ShouldRemesh = false,
    string Topology = "triangle",
    bool UltraMode = false,
    bool AutoSize = false,
    string OriginAt = "bottom",
    bool AlphaThumbnail = true,
    string? TextureImage = null,
    IReadOnlyList<int>? Lods = null)
{
    /// <summary>
    /// Extra polycount levels produced by Meshy Remesh from the refined model (5 credits each), shipped as
    /// <c>lod1</c>, <c>lod2</c>… next to the full model. Empty by default; <see cref="Lods"/> is never null.
    /// </summary>
    public IReadOnlyList<int> LodLevels => Lods ?? [];

    public const int MaxLods = 3;

    public const string PreviewTextured = "textured";
    public const string PreviewClay = "clay";

    public const string ModelTypeStandard = "standard";
    public const string ModelTypeSmartTopology = "smart-topology";

    /// <summary>Deprecated by Meshy; kept so an old definition gets a pointed error instead of "unknown".</summary>
    public const string ModelTypeLowpoly = "lowpoly";

    /// <summary>The only model Meshy serves for <see cref="ModelTypeSmartTopology"/>.</summary>
    public const string AiModelSmartTopology = "meshy-t2";

    public static readonly GenerationSettings Default = new(
        AiModel: "latest",
        ModelType: ModelTypeStandard,
        TargetPolycount: null,
        EnablePbr: false,
        TextureResolution: "2k",
        TargetFormats: ["glb"],
        Preview: PreviewTextured);

    /// <summary>Free-form but bounded: the store facets on it, so typos would split the facet.</summary>
    public static readonly IReadOnlySet<string> KnownCategories =
        new HashSet<string>(StringComparer.Ordinal) { "props", "characters", "environment", "vehicles", "weapons", "architecture", "food", "nature", "furniture", "misc" };

    public static readonly IReadOnlySet<string> KnownStyles =
        new HashSet<string>(StringComparer.Ordinal) { "lowpoly", "stylized", "realistic", "voxel", "handpainted" };

    public static readonly IReadOnlySet<string> KnownPreviews =
        new HashSet<string>(StringComparer.Ordinal) { PreviewTextured, PreviewClay };

    public static readonly IReadOnlySet<string> KnownAiModels =
        new HashSet<string>(StringComparer.Ordinal) { "latest", "meshy-5", "meshy-6", "meshy-7", AiModelSmartTopology };

    /// <summary>Models that support <see cref="UltraMode"/> (Meshy 7 and whatever <c>latest</c> resolves to).</summary>
    public static readonly IReadOnlySet<string> UltraCapableAiModels =
        new HashSet<string>(StringComparer.Ordinal) { "latest", "meshy-7" };

    public static readonly IReadOnlySet<string> KnownModelTypes =
        new HashSet<string>(StringComparer.Ordinal) { ModelTypeStandard, ModelTypeSmartTopology };

    public static readonly IReadOnlySet<string> KnownTopologies =
        new HashSet<string>(StringComparer.Ordinal) { "triangle", "quad" };

    public static readonly IReadOnlySet<string> KnownOrigins =
        new HashSet<string>(StringComparer.Ordinal) { "bottom", "center" };

    public static readonly IReadOnlySet<string> KnownTextureResolutions =
        new HashSet<string>(StringComparer.Ordinal) { "2k", "4k", "8k" };

    public static readonly IReadOnlySet<string> KnownFormats =
        new HashSet<string>(StringComparer.Ordinal) { "glb", "fbx", "obj", "stl", "usdz", "3mf" };
}
