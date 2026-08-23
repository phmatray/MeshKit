using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace MeshKit.Web.Catalog;

public static class CatalogEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new()
    {
        Mappings =
        {
            [".glb"] = "model/gltf-binary",
            [".gltf"] = "model/gltf+json",
            [".usdz"] = "model/vnd.usdz+zip",
        },
    };

    /// <summary>Serves <c>public/</c> files of a pack (thumbnails, untextured previews). Anything else 404s.</summary>
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        // GET and HEAD: CDNs, link checkers and <model-viewer> preflights all HEAD these assets.
        app.MapMethods("/catalog/{slug}/public/{**path}", [HttpMethods.Get, HttpMethods.Head], (string slug, string path, ICatalogService catalog, HttpResponse response) =>
        {
            var file = catalog.PublicFile(slug, path);
            if (file is null)
            {
                return Results.NotFound();
            }

            // A regenerated pack keeps the same file names, so the URL cannot promise immutability. Instead:
            // one day of free reuse, then a conditional GET that answers 304 and never re-sends the bytes.
            var info = new FileInfo(file);
            var lastModified = new DateTimeOffset(info.LastWriteTimeUtc);
            var etag = new EntityTagHeaderValue($"\"{info.Length:x}-{lastModified.ToUnixTimeSeconds():x}\"");
            response.Headers.CacheControl = "public, max-age=86400, stale-while-revalidate=604800";
            var contentType = ContentTypes.TryGetContentType(file, out var ct) ? ct : "application/octet-stream";
            return Results.File(file, contentType, lastModified: lastModified, entityTag: etag, enableRangeProcessing: true);
        });

        return app;
    }

    public static string PublicUrl(string slug, string relativePath) =>
        relativePath.StartsWith("public/", StringComparison.Ordinal)
            ? $"/catalog/{slug}/{relativePath}"
            : throw new ArgumentException($"'{relativePath}' is not a public path.", nameof(relativePath));
}
