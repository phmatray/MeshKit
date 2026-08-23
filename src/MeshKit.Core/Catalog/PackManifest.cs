using MeshKit.Core.Definitions;

namespace MeshKit.Core.Catalog;

/// <summary>
/// <c>catalog/&lt;slug&gt;/manifest.json</c>: what the pipeline produced for a pack. It is both the
/// store's read model and the pipeline's resume state, so it is written after every model.
/// All paths are relative to the pack directory and confined to <c>public/</c> or <c>private/</c>.
/// </summary>
public sealed record PackManifest(
    int SchemaVersion,
    string Slug,
    string Name,
    string Description,
    Price Price,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ModelEntry> Models,
    IReadOnlyList<string>? Tags = null,
    string? Category = null,
    string? Style = null,
    PackLicense? License = null,
    string? Sample = null)
{
    public IReadOnlyList<string> TagList => Tags ?? [];

    /// <summary>The free sample model, when the definition names one and it was generated.</summary>
    public ModelEntry? SampleModel => Sample is null ? null : Models.FirstOrDefault(m => m.Slug == Sample && m.Status == ModelStatus.Succeeded);

    public const int CurrentSchemaVersion = 1;

    /// <summary>A pack is only sold when every model came out of the pipeline.</summary>
    public bool IsSellable => Models.Count > 0 && Models.All(m => m.Status == ModelStatus.Succeeded);

    public static PackManifest FromDefinition(PackDefinition definition, DateTimeOffset generatedAt) => new(
        SchemaVersion: CurrentSchemaVersion,
        Slug: definition.Slug,
        Name: definition.Name,
        Description: definition.Description,
        Price: definition.Price,
        GeneratedAt: generatedAt,
        Models: definition.Models.Select(ModelEntry.Pending).ToList(),
        Tags: definition.Tags,
        Category: definition.Category,
        Style: definition.Style,
        License: null,
        Sample: definition.Sample);

    public bool Equals(PackManifest? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Slug == other.Slug
        && Name == other.Name
        && Description == other.Description
        && Price == other.Price
        && GeneratedAt == other.GeneratedAt
        && TagList.SequenceEqual(other.TagList)
        && Category == other.Category
        && Style == other.Style
        && License == other.License
        && Sample == other.Sample
        && Models.SequenceEqual(other.Models);

    public override int GetHashCode() => HashCode.Combine(SchemaVersion, Slug, GeneratedAt, Models.Count);
}

/// <summary>Resolved licence of a pack: a human name, the template/custom id, and the file inside the pack.</summary>
public sealed record PackLicense(string Id, string Name, string PublicFile, string PrivateFile);

/// <summary>Measured from the refined GLB by the pipeline — never typed by hand.</summary>
public sealed record ModelMetadata(
    int Triangles,
    int Vertices,
    double Width,
    double Height,
    double Depth,
    bool Pbr,
    string? TextureResolution,
    IReadOnlyList<string> TextureMaps,
    long TotalBytes)
{
    public bool Equals(ModelMetadata? other) =>
        other is not null
        && Triangles == other.Triangles && Vertices == other.Vertices
        && Width.Equals(other.Width) && Height.Equals(other.Height) && Depth.Equals(other.Depth)
        && Pbr == other.Pbr && TextureResolution == other.TextureResolution && TotalBytes == other.TotalBytes
        && TextureMaps.SequenceEqual(other.TextureMaps);

    public override int GetHashCode() => HashCode.Combine(Triangles, Vertices, TotalBytes);
}

public enum ModelStatus
{
    Pending,
    Succeeded,
    Failed,
}

public sealed record ModelEntry(
    string Slug,
    string Name,
    string Prompt,
    ModelStatus Status,
    string? Error,
    string? PreviewTaskId,
    string? RefineTaskId,
    string? Thumbnail,
    string? Preview,
    IReadOnlyList<ModelFile> Files,
    int ConsumedCredits,
    bool PreviewTextured = false,
    IReadOnlyList<string>? Tags = null,
    string? Category = null,
    ModelMetadata? Metadata = null,
    IReadOnlyList<ModelLod>? Lods = null,
    IReadOnlyList<ModelVariant>? Variants = null)
{
    public IReadOnlyList<string> TagList => Tags ?? [];

    /// <summary>Lighter remeshed copies of the full model, heaviest first. Empty when the pack defines no LODs.</summary>
    public IReadOnlyList<ModelLod> LodList => Lods ?? [];

    /// <summary>Alternative texture sets of the full model. Empty when the pack defines no variants.</summary>
    public IReadOnlyList<ModelVariant> VariantList => Variants ?? [];

    /// <summary>Every downloadable file: the full model's, every LOD's and every variant's.</summary>
    public IEnumerable<ModelFile> AllFiles => Files.Concat(LodList.SelectMany(l => l.Files)).Concat(VariantList.SelectMany(v => v.Files));

    public static ModelEntry Pending(ModelDefinition model) => new(
        Slug: model.Slug,
        Name: model.Name,
        Prompt: model.Prompt,
        Status: ModelStatus.Pending,
        Error: null,
        PreviewTaskId: null,
        RefineTaskId: null,
        Thumbnail: null,
        Preview: null,
        Files: [],
        ConsumedCredits: 0,
        PreviewTextured: false,
        Tags: model.Tags,
        Category: model.Category,
        Metadata: null);

    public bool Equals(ModelEntry? other) =>
        other is not null
        && Slug == other.Slug
        && Name == other.Name
        && Prompt == other.Prompt
        && Status == other.Status
        && Error == other.Error
        && PreviewTaskId == other.PreviewTaskId
        && RefineTaskId == other.RefineTaskId
        && Thumbnail == other.Thumbnail
        && Preview == other.Preview
        && ConsumedCredits == other.ConsumedCredits
        && PreviewTextured == other.PreviewTextured
        && TagList.SequenceEqual(other.TagList)
        && Category == other.Category
        && Equals(Metadata, other.Metadata)
        && Files.SequenceEqual(other.Files)
        && LodList.SequenceEqual(other.LodList)
        && VariantList.SequenceEqual(other.VariantList);

    public override int GetHashCode() => HashCode.Combine(Slug, Status, Files.Count);
}

/// <summary>
/// One texture variant of a model: the refined mesh retextured by Meshy from a style prompt. <paramref name="Preview"/>
/// and <paramref name="Thumbnail"/> are public so the store can show the skin; <paramref name="Files"/> are the paid ones.
/// </summary>
public sealed record ModelVariant(string Slug, string Name, string TaskId, IReadOnlyList<ModelFile> Files, string? Thumbnail, string? Preview, int ConsumedCredits)
{
    public bool Equals(ModelVariant? other) =>
        other is not null && Slug == other.Slug && Name == other.Name && TaskId == other.TaskId && Thumbnail == other.Thumbnail
        && Preview == other.Preview && ConsumedCredits == other.ConsumedCredits && Files.SequenceEqual(other.Files);

    public override int GetHashCode() => HashCode.Combine(Slug, TaskId);
}

/// <summary>
/// One LOD: the refined model remeshed by Meshy to <paramref name="TargetPolycount"/>. <paramref name="Level"/> is
/// 1-based, heaviest first (lod1 is the first step down from the full model).
/// </summary>
public sealed record ModelLod(int Level, int TargetPolycount, string TaskId, IReadOnlyList<ModelFile> Files, int? Triangles, int ConsumedCredits)
{
    public bool Equals(ModelLod? other) =>
        other is not null && Level == other.Level && TargetPolycount == other.TargetPolycount && TaskId == other.TaskId
        && Triangles == other.Triangles && ConsumedCredits == other.ConsumedCredits && Files.SequenceEqual(other.Files);

    public override int GetHashCode() => HashCode.Combine(Level, TargetPolycount, TaskId);
}

/// <summary>One downloadable file of a model: <c>format</c> is the Meshy key (glb, fbx, obj, mtl, usdz, stl, 3mf).</summary>
public sealed record ModelFile(string Format, string Path, long Bytes);
