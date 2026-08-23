namespace MeshKit.Web.Data;

/// <summary>
/// One free-sample download. Not an entitlement — the sample is free for every account — but the
/// list of who tried which pack is the lead list a store lives on, and the library re-offers them.
/// </summary>
public sealed class SampleDownload
{
    public int Id { get; set; }

    public required string UserId { get; set; }

    public required string PackSlug { get; set; }

    public required string ModelSlug { get; set; }

    public DateTimeOffset DownloadedAt { get; set; }
}
