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

    /// <summary>Ticked "email me a discount once I've tried it" at download time. Explicit consent, never inferred.</summary>
    public bool FollowUpOptIn { get; set; }

    /// <summary>When the follow-up went out (or was decided against, e.g. the pack was bought first). One per user and pack.</summary>
    public DateTimeOffset? FollowUpSentAt { get; set; }
}
