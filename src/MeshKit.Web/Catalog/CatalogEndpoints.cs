using Microsoft.AspNetCore.StaticFiles;

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
        app.MapGet("/catalog/{slug}/public/{**path}", (string slug, string path, ICatalogService catalog, HttpResponse response) =>
        {
            var file = catalog.PublicFile(slug, path);
            if (file is null)
            {
                return Results.NotFound();
            }

            response.Headers.CacheControl = "public, max-age=3600";
            var contentType = ContentTypes.TryGetContentType(file, out var ct) ? ct : "application/octet-stream";
            return Results.File(file, contentType, enableRangeProcessing: true);
        });

        return app;
    }

    public static string PublicUrl(string slug, string relativePath) =>
        relativePath.StartsWith("public/", StringComparison.Ordinal)
            ? $"/catalog/{slug}/{relativePath}"
            : throw new ArgumentException($"'{relativePath}' is not a public path.", nameof(relativePath));
}
