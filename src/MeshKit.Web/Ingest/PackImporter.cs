using System.IO.Compression;
using MeshKit.Core.Catalog;
using MeshKit.Web.Catalog;

namespace MeshKit.Web.Ingest;

public sealed record ImportResult(bool Success, string? Slug, string? Error, bool Sellable);

/// <summary>
/// Installs a pack archive into the catalog: extract to a staging directory next to the catalog,
/// validate the manifest (schema, slug, path confinement, files present), then swap it in atomically
/// and reload. A bad archive never touches the live pack directory.
/// </summary>
public sealed class PackImporter(ICatalogService catalog, ILogger<PackImporter> logger)
{
    public async Task<ImportResult> ImportAsync(Stream archive, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(catalog.RootPath);
        var staging = Path.Combine(catalog.RootPath, $".staging-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            var zipPath = Path.Combine(staging, "upload.zip");
            await using (var file = File.Create(zipPath))
            {
                await archive.CopyToAsync(file, cancellationToken);
            }

            var extracted = Path.Combine(staging, "pack");
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extracted, overwriteFiles: true);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                return Fail($"Archive cannot be extracted: {ex.Message}");
            }

            var manifestPath = Path.Combine(extracted, PackManifestSerializer.FileName);
            if (!File.Exists(manifestPath))
            {
                return Fail($"Archive has no {PackManifestSerializer.FileName} at its root.");
            }

            PackManifest manifest;
            try
            {
                manifest = PackManifestSerializer.ReadFile(manifestPath);
            }
            catch (PackManifestException ex)
            {
                return Fail(ex.Message);
            }

            if (!Core.Definitions.Slug.IsValid(manifest.Slug))
            {
                return Fail($"Manifest slug '{manifest.Slug}' is invalid.");
            }

            var unsafePaths = PackPaths.UnsafePaths(manifest);
            if (unsafePaths.Count > 0)
            {
                return Fail($"Manifest references unsafe path(s): {string.Join(", ", unsafePaths)}");
            }

            var missing = manifest.Models
                .Where(m => m.Status == ModelStatus.Succeeded)
                .SelectMany(m => new[] { m.Thumbnail, m.Preview }.Concat(m.Files.Select(f => f.Path)))
                .OfType<string>()
                .Where(p => !File.Exists(PackPaths.Resolve(extracted, p)))
                .ToList();
            if (missing.Count > 0)
            {
                return Fail($"Manifest lists files absent from the archive: {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? ", …" : "")}");
            }

            var target = Path.Combine(catalog.RootPath, manifest.Slug);
            var retired = Path.Combine(staging, "previous");
            if (Directory.Exists(target))
            {
                Directory.Move(target, retired);
            }

            Directory.Move(extracted, target);
            catalog.Reload();
            logger.LogInformation("Imported pack {Slug} ({Models} models, sellable: {Sellable})", manifest.Slug, manifest.Models.Count, manifest.IsSellable);
            return new ImportResult(true, manifest.Slug, null, manifest.IsSellable);
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (IOException ex)
            {
                logger.LogWarning("Could not clean staging directory {Dir}: {Message}", staging, ex.Message);
            }
        }

        ImportResult Fail(string error)
        {
            logger.LogWarning("Pack import refused: {Error}", error);
            return new ImportResult(false, null, error, false);
        }
    }
}
