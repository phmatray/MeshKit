using System.Security.Claims;
using MeshKit.Web.Catalog;
using MeshKit.Web.Data;
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

        // The free sample: any account, no entitlement. The account is the point — every sample is a lead.
        app.MapGet("/packs/{slug}/sample", async (string slug, HttpContext http, ICatalogService catalog, ApplicationDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var pack = catalog.Find(slug);
            if (pack is not { IsSellable: true } || pack.SampleModel is not { } sample)
            {
                return Results.NotFound();
            }

            db.SampleDownloads.Add(new SampleDownload { UserId = userId, PackSlug = slug, ModelSlug = sample.Slug, DownloadedAt = time.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);

            if (http.Features.Get<IHttpBodyControlFeature>() is { } body)
            {
                body.AllowSynchronousIO = true;
            }

            return Results.Stream(
                stream => catalog.WriteModelZipAsync(slug, sample.Slug, stream, cancellationToken),
                contentType: "application/zip",
                fileDownloadName: $"{slug}-sample-{sample.Slug}.zip");
        }).RequireAuthorization();

        return app;
    }
}
