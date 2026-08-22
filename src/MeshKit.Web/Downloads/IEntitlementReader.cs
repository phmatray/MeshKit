using System.Security.Claims;

namespace MeshKit.Web.Downloads;

/// <summary>Read side of entitlements, used by pages to decide Buy vs Download.</summary>
public interface IEntitlementReader
{
    string? UserId(ClaimsPrincipal user);

    Task<bool> OwnsAsync(string userId, string packSlug, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> OwnedPackSlugsAsync(string userId, CancellationToken cancellationToken);
}
