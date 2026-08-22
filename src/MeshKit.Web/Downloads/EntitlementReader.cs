using System.Security.Claims;
using MeshKit.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace MeshKit.Web.Downloads;

public sealed class EntitlementReader(ApplicationDbContext db) : IEntitlementReader
{
    public string? UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);

    public Task<bool> OwnsAsync(string userId, string packSlug, CancellationToken cancellationToken) =>
        db.Entitlements.AnyAsync(e => e.UserId == userId && e.PackSlug == packSlug, cancellationToken);

    public async Task<IReadOnlyList<string>> OwnedPackSlugsAsync(string userId, CancellationToken cancellationToken) =>
        await db.Entitlements.Where(e => e.UserId == userId).OrderByDescending(e => e.Id).Select(e => e.PackSlug).ToListAsync(cancellationToken);
}
