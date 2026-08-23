namespace MeshKit.Web.Data;

/// <summary>The right of a user to download a pack. Unique per (user, pack); survives refunds deliberately (manual revoke).</summary>
public sealed class Entitlement
{
    public int Id { get; set; }

    public required string UserId { get; set; }

    public required string PackSlug { get; set; }

    public int OrderId { get; set; }

    public DateTimeOffset GrantedAt { get; set; }
}
