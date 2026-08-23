using System.Security.Claims;
using MeshKit.Web.Catalog;
using Microsoft.AspNetCore.Antiforgery;
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
        // POST is the pack-page form (it carries the follow-up opt-in); GET is the re-download from the library.
        app.MapPost("/packs/{slug}/sample", async (string slug, HttpContext http, IAntiforgery antiforgery, ICatalogService catalog, ApplicationDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest();
            }

            var form = await http.Request.ReadFormAsync(cancellationToken);
            var optIn = form["followup"] is { Count: > 0 } v && v[0] is "on" or "true" or "1";
            return await SampleAsync(slug, http, catalog, db, time, optIn, cancellationToken);
        }).RequireAuthorization();

        app.MapGet("/packs/{slug}/sample", (string slug, HttpContext http, ICatalogService catalog, ApplicationDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
            SampleAsync(slug, http, catalog, db, time, optIn: false, cancellationToken)).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> SampleAsync(string slug, HttpContext http, ICatalogService catalog, ApplicationDbContext db, TimeProvider time, bool optIn, CancellationToken cancellationToken)
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

        db.SampleDownloads.Add(new SampleDownload { UserId = userId, PackSlug = slug, ModelSlug = sample.Slug, DownloadedAt = time.GetUtcNow(), FollowUpOptIn = optIn });
        await db.SaveChangesAsync(cancellationToken);

        if (http.Features.Get<IHttpBodyControlFeature>() is { } body)
        {
            body.AllowSynchronousIO = true;
        }

        return Results.Stream(
            stream => catalog.WriteModelZipAsync(slug, sample.Slug, stream, cancellationToken),
            contentType: "application/zip",
            fileDownloadName: $"{slug}-sample-{sample.Slug}.zip");
    }
}
