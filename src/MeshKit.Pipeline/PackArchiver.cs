using System.IO.Compression;

namespace MeshKit.Pipeline;

/// <summary>Pack directory ⇄ zip, entries named relative to the pack root with forward slashes.</summary>
public static class PackArchiver
{
    public static void Zip(string packDirectory, string zipPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var root = Path.GetFullPath(packDirectory);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (file.EndsWith(".part", StringComparison.Ordinal) || file.EndsWith(".tmp", StringComparison.Ordinal))
            {
                continue; // half-written downloads are not part of a pack
            }

            var entryName = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
    }

    /// <summary>Extracts into <paramref name="targetDirectory"/>; entries escaping it make the framework throw.</summary>
    public static void Unzip(string zipPath, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        ZipFile.ExtractToDirectory(zipPath, targetDirectory, overwriteFiles: true);
    }
}
