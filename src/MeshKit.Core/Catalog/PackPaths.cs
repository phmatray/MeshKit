namespace MeshKit.Core.Catalog;

/// <summary>
/// The only two roots a manifest may reference. <c>public/</c> is served to anyone (thumbnails,
/// untextured previews); <c>private/</c> is only ever streamed to entitled buyers.
/// </summary>
public static class PackPaths
{
    public const string PublicRoot = "public";
    public const string PrivateRoot = "private";
    public const string ThumbsDir = "public/thumbs";
    public const string PreviewDir = "public/preview";

    /// <summary>
    /// True only for a forward-slash relative path under <c>public/</c> or <c>private/</c> with no
    /// <c>.</c>/<c>..</c> segments, no backslashes, no drive letters. The web app trusts nothing else.
    /// </summary>
    public static bool IsSafeRelative(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith('/'))
        {
            return false;
        }

        var segments = path.Split('/');
        if (segments.Length < 2 || segments[0] is not (PublicRoot or PrivateRoot))
        {
            return false;
        }

        return segments.All(s => s.Length > 0 && s is not ("." or ".."));
    }

    /// <summary>Every path in the manifest that fails <see cref="IsSafeRelative"/>, in document order.</summary>
    public static IReadOnlyList<string> UnsafePaths(PackManifest manifest)
    {
        var offenders = new List<string>();
        foreach (var model in manifest.Models)
        {
            foreach (var candidate in new[] { model.Thumbnail, model.Preview }.Concat(model.Files.Select(f => f.Path)))
            {
                if (candidate is not null && !IsSafeRelative(candidate))
                {
                    offenders.Add(candidate);
                }
            }
        }

        return offenders;
    }

    /// <summary>Resolves a validated relative path inside a pack directory.</summary>
    public static string Resolve(string packDirectory, string relativePath)
    {
        if (!IsSafeRelative(relativePath))
        {
            throw new PackManifestException($"Refusing to resolve unsafe path '{relativePath}'.");
        }

        return Path.Combine(packDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
