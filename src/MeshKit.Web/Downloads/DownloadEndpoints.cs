using System.Security.Claims;
using MeshKit.Web.Catalog;
using Microsoft.AspNetCore.Http.Features;

namespace MeshKit.Web.Downloads;

public static class DownloadEndpoints
{
    public static IEndpointRouteBuilder MapDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/library/{slug}/download", async (string slug, HttpContext http, ICatalogService catalog, IEntitlementReader entitlements, CancellationToken cancellationToken) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            if (catalog.Find(slug) is null)
            {
                return Results.NotFound();
            }

            if (!await entitlements.OwnsAsync(userId, slug, cancellationToken))
            {
                return Results.Forbid();
            }

            // ZipArchive still flushes entry trailers synchronously on dispose (even via the async API);
            // allow it for this response only rather than buffering a multi-hundred-MB pack.
            if (http.Features.Get<IHttpBodyControlFeature>() is { } body)
            {
                body.AllowSynchronousIO = true;
            }

            return Results.Stream(
                stream => catalog.WritePrivateZipAsync(slug, stream, cancellationToken),
                contentType: "application/zip",
                fileDownloadName: $"{slug}.zip");
        }).RequireAuthorization();

        return app;
    }
}
