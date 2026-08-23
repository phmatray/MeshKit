namespace MeshKit.Web.Data;

/// <summary>One "new pack" email campaign: a pack is announced at most once, however many times it is re-ingested.</summary>
public sealed class PackAnnouncement
{
    public int Id { get; set; }

    public required string PackSlug { get; set; }

    public DateTimeOffset SentAt { get; set; }

    public int Recipients { get; set; }
}
