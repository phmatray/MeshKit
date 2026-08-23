namespace MeshKit.Web.Catalog;

public sealed class CatalogOptions
{
    public const string Section = "MeshKit:Catalog";

    /// <summary>Directory holding one sub-directory per pack, each with a <c>manifest.json</c>.</summary>
    public string Path { get; set; } = "catalog";
}
