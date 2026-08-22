namespace MeshKit.Web.Ingest;

public sealed class IngestOptions
{
    public const string Section = "MeshKit:Ingest";

    /// <summary>Bearer token the pipeline presents on <c>POST /api/ingest</c>. Empty = ingest disabled (503).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Upper bound for an uploaded pack archive.</summary>
    public long MaxUploadBytes { get; set; } = 2L * 1024 * 1024 * 1024;
}
