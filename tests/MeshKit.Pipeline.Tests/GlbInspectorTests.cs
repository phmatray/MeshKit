using MeshKit.Pipeline;

namespace MeshKit.Pipeline.Tests;

public sealed class GlbInspectorTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("meshkit-glb");

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir.FullName, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Reads_triangles_vertices_and_bounding_box()
    {
        var geometry = GlbInspector.Inspect(Write("cube.glb", TestGlb.Cube(size: 2.5f)));

        Assert.Equal(12, geometry.Triangles);
        Assert.Equal(8, geometry.Vertices);
        Assert.Equal((2.5, 2.5, 2.5), (geometry.Width, geometry.Height, geometry.Depth));
    }

    [Fact]
    public void Sums_across_primitives()
    {
        var geometry = GlbInspector.Inspect(Write("two.glb", TestGlb.Cube(copies: 3)));

        Assert.Equal(36, geometry.Triangles);
        Assert.Equal(24, geometry.Vertices);
    }

    [Fact]
    public void Rejects_non_glb_files()
    {
        var ex = Assert.Throws<InvalidDataException>(() => GlbInspector.Inspect(Write("nope.glb", "hello"u8.ToArray().Concat(new byte[20]).ToArray())));
        Assert.Contains("bad magic", ex.Message);
    }

    public void Dispose() => _dir.Delete(recursive: true);
}
