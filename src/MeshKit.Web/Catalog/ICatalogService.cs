using MeshKit.Core.Catalog;

namespace MeshKit.Web.Catalog;

public interface ICatalogService
{
    /// <summary>Packs the store may sell, ordered by name.</summary>
    IReadOnlyList<PackManifest> Sellable { get; }

    /// <summary>Any loaded pack, sellable or not (the library shows packs a buyer already owns).</summary>
    PackManifest? Find(string slug);

    /// <summary>Absolute path of a file under the pack's <c>public/</c>, or null when unknown/unsafe/missing.</summary>
    string? PublicFile(string slug, string relativePath);

    /// <summary>Streams a zip of the pack's <c>private/</c> tree, entries prefixed with the slug.</summary>
    Task WritePrivateZipAsync(string slug, Stream destination, CancellationToken cancellationToken);

    /// <summary>Absolute pack directory for a slug (exists only for loaded packs).</summary>
    string? PackDirectory(string slug);

    /// <summary>Root directory of the catalog (where ingest writes).</summary>
    string RootPath { get; }

    /// <summary>Increments on every <see cref="Reload"/>; derived indexes compare it to know they are stale.</summary>
    int Version { get; }

    void Reload();
}
