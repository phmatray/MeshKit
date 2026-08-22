using System.IO.Compression;
using MeshKit.Pipeline;

namespace MeshKit.Pipeline.Tests;

public sealed class PackArchiverTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("meshkit-zip");

    [Fact]
    public void Zip_contains_pack_files_with_forward_slash_relative_names()
    {
        var pack = Path.Combine(_root.FullName, "props");
        Directory.CreateDirectory(Path.Combine(pack, "public", "thumbs"));
        Directory.CreateDirectory(Path.Combine(pack, "private", "chest"));
        File.WriteAllText(Path.Combine(pack, "manifest.json"), "{}");
        File.WriteAllText(Path.Combine(pack, "public", "thumbs", "chest.png"), "png");
        File.WriteAllText(Path.Combine(pack, "private", "chest", "chest.glb"), "glb");
        File.WriteAllText(Path.Combine(pack, "private", "chest", "chest.glb.part"), "partial download");
        var zipPath = Path.Combine(_root.FullName, "props.zip");

        PackArchiver.Zip(pack, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).Order().ToArray();
        Assert.Equal(["manifest.json", "private/chest/chest.glb", "public/thumbs/chest.png"], names);
    }

    [Fact]
    public void Unzip_restores_the_tree()
    {
        var pack = Path.Combine(_root.FullName, "props");
        Directory.CreateDirectory(Path.Combine(pack, "private", "chest"));
        File.WriteAllText(Path.Combine(pack, "manifest.json"), "{}");
        File.WriteAllText(Path.Combine(pack, "private", "chest", "chest.glb"), "glb");
        var zipPath = Path.Combine(_root.FullName, "props.zip");
        PackArchiver.Zip(pack, zipPath);
        var target = Path.Combine(_root.FullName, "restored");

        PackArchiver.Unzip(zipPath, target);

        Assert.Equal("glb", File.ReadAllText(Path.Combine(target, "private", "chest", "chest.glb")));
        Assert.True(File.Exists(Path.Combine(target, "manifest.json")));
    }

    [Fact]
    public void Unzip_refuses_entries_that_escape_the_target()
    {
        var zipPath = Path.Combine(_root.FullName, "evil.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../escape.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("x");
        }

        Assert.ThrowsAny<IOException>(() => PackArchiver.Unzip(zipPath, Path.Combine(_root.FullName, "target")));
        Assert.False(File.Exists(Path.Combine(_root.FullName, "escape.txt")));
    }

    public void Dispose() => _root.Delete(recursive: true);
}
