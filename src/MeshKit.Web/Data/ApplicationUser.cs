using Microsoft.AspNetCore.Identity;

namespace MeshKit.Web.Data;

public sealed class ApplicationUser : IdentityUser
{
    /// <summary>Asked to be emailed when a new pack is released. Explicit consent on the account page; one-click opt-out in every such email.</summary>
    public bool NewReleaseOptIn { get; set; }

    public DateTimeOffset? NewReleaseOptInAt { get; set; }
}
