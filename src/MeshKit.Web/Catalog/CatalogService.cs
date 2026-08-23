using System.IO.Compression;
using MeshKit.Core.Catalog;
using Microsoft.Extensions.Options;

namespace MeshKit.Web.Catalog;

/// <summary>
/// In-memory view of the catalog directory. Scans once at construction and on <see cref="Reload"/>;
/// a pack whose manifest is unreadable, mis-named or references a path outside <c>public/</c> /
/// <c>private/</c> is skipped with an error log, never served.
/// </summary>
public sealed class CatalogService : ICatalogService
{
    private readonly ILogger<CatalogService> _logger;
    private volatile Dictionary<string, LoadedPack> _packs = new(StringComparer.Ordinal);

    public CatalogService(IOptions<CatalogOptions> options, ILogger<CatalogService> logger)
    {
        _logger = logger;
        RootPath = Path.GetFullPath(options.Value.Path);
        Reload();
    }

    public string RootPath { get; }

    public IReadOnlyList<PackManifest> Sellable =>
        _packs.Values.Select(p => p.Manifest).Where(m => m.IsSellable).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public PackManifest? Find(string slug) => _packs.GetValueOrDefault(slug)?.Manifest;

    public string? PackDirectory(string slug) => _packs.GetValueOrDefault(slug)?.Directory;

    public string? PublicFile(string slug, string relativePath)
    {
        if (!_packs.TryGetValue(slug, out var pack))
        {
            return null;
        }

        var candidate = $"{PackPaths.PublicRoot}/{relativePath}";
        if (!PackPaths.IsSafeRelative(candidate))
        {
            return null;
        }

        var full = PackPaths.Resolve(pack.Directory, candidate);
        return File.Exists(full) ? full : null;
    }

    public async Task WritePrivateZipAsync(string slug, Stream destination, CancellationToken cancellationToken)
    {
        if (!_packs.TryGetValue(slug, out var pack))
        {
            throw new KeyNotFoundException($"Pack '{slug}' is not in the catalog.");
        }

        // Fully async: the destination is the HTTP response body, which forbids synchronous writes.
        var privateRoot = Path.Combine(pack.Directory, PackPaths.PrivateRoot);
        await using var zip = await ZipArchive.CreateAsync(destination, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        foreach (var file in Directory.EnumerateFiles(privateRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(privateRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            var entry = zip.CreateEntry($"{slug}/{relative}", CompressionLevel.Fastest);
            await using var source = File.OpenRead(file);
            await using var target = await entry.OpenAsync(cancellationToken);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    public void Reload()
    {
        var packs = new Dictionary<string, LoadedPack>(StringComparer.Ordinal);
        if (!Directory.Exists(RootPath))
        {
            _logger.LogWarning("Catalog directory {Path} does not exist; the store is empty", RootPath);
            _packs = packs;
            return;
        }

        foreach (var dir in Directory.EnumerateDirectories(RootPath).Order(StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(dir, PackManifestSerializer.FileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var loaded = TryLoad(dir, manifestPath);
            if (loaded is not null)
            {
                packs[loaded.Manifest.Slug] = loaded;
            }
        }

        _packs = packs;
        _logger.LogInformation("Catalog loaded: {Total} pack(s), {Sellable} sellable", packs.Count, packs.Values.Count(p => p.Manifest.IsSellable));
    }

    private LoadedPack? TryLoad(string dir, string manifestPath)
    {
        PackManifest manifest;
        try
        {
            manifest = PackManifestSerializer.ReadFile(manifestPath);
        }
        catch (PackManifestException ex)
        {
            _logger.LogError("Skipping {Dir}: {Message}", dir, ex.Message);
            return null;
        }

        var dirName = Path.GetFileName(dir);
        if (!string.Equals(dirName, manifest.Slug, StringComparison.Ordinal))
        {
            _logger.LogError("Skipping {Dir}: directory name does not match manifest slug '{Slug}'", dir, manifest.Slug);
            return null;
        }

        var unsafePaths = PackPaths.UnsafePaths(manifest);
        if (unsafePaths.Count > 0)
        {
            _logger.LogError("Skipping {Dir}: manifest references unsafe path(s) {Paths}", dir, string.Join(", ", unsafePaths));
            return null;
        }

        return new LoadedPack(manifest, Path.GetFullPath(dir));
    }

    private sealed record LoadedPack(PackManifest Manifest, string Directory);
}
