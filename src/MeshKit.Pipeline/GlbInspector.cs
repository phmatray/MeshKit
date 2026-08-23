using System.Buffers.Binary;
using System.Text.Json;
using MeshKit.Core.Catalog;

namespace MeshKit.Pipeline;

/// <summary>
/// Reads what a glTF 2.0 binary says about itself — triangle/vertex counts from accessors and the
/// bounding box from POSITION min/max — without decoding geometry. Good enough for a spec strip;
/// not a renderer. Fails loudly on malformed files so the manifest never carries made-up numbers.
/// </summary>
public static class GlbInspector
{
    public sealed record Geometry(int Triangles, int Vertices, double Width, double Height, double Depth);

    public static Geometry Inspect(string glbPath)
    {
        using var stream = File.OpenRead(glbPath);
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x46546C67) // "glTF"
        {
            throw new InvalidDataException($"{Path.GetFileName(glbPath)} is not a GLB (bad magic).");
        }

        Span<byte> chunkHeader = stackalloc byte[8];
        stream.ReadExactly(chunkHeader);
        var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader));
        if (BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]) != 0x4E4F534A) // "JSON"
        {
            throw new InvalidDataException($"{Path.GetFileName(glbPath)}: first chunk is not JSON.");
        }

        var json = new byte[jsonLength];
        stream.ReadExactly(json);
        using var doc = JsonDocument.Parse(json);
        return Measure(doc.RootElement);
    }

    private static Geometry Measure(JsonElement root)
    {
        var accessors = root.TryGetProperty("accessors", out var a) ? a : default;
        long triangles = 0, vertices = 0;
        double[] min = [double.MaxValue, double.MaxValue, double.MaxValue];
        double[] max = [double.MinValue, double.MinValue, double.MinValue];
        var sawPosition = false;

        if (root.TryGetProperty("meshes", out var meshes))
        {
            foreach (var mesh in meshes.EnumerateArray())
            {
                if (!mesh.TryGetProperty("primitives", out var primitives))
                {
                    continue;
                }

                foreach (var primitive in primitives.EnumerateArray())
                {
                    var mode = primitive.TryGetProperty("mode", out var m) ? m.GetInt32() : 4;
                    int? positionCount = null;
                    if (primitive.TryGetProperty("attributes", out var attributes) && attributes.TryGetProperty("POSITION", out var posIdx))
                    {
                        var position = accessors[posIdx.GetInt32()];
                        positionCount = position.GetProperty("count").GetInt32();
                        vertices += positionCount.Value;
                        if (position.TryGetProperty("min", out var mn) && position.TryGetProperty("max", out var mx))
                        {
                            sawPosition = true;
                            for (var i = 0; i < 3; i++)
                            {
                                min[i] = Math.Min(min[i], mn[i].GetDouble());
                                max[i] = Math.Max(max[i], mx[i].GetDouble());
                            }
                        }
                    }

                    var indexCount = primitive.TryGetProperty("indices", out var idx)
                        ? accessors[idx.GetInt32()].GetProperty("count").GetInt32()
                        : positionCount ?? 0;
                    triangles += mode switch
                    {
                        4 => indexCount / 3,            // TRIANGLES
                        5 or 6 => Math.Max(0, indexCount - 2), // STRIP / FAN
                        _ => 0,                          // points / lines
                    };
                }
            }
        }

        if (!sawPosition)
        {
            min = [0, 0, 0];
            max = [0, 0, 0];
        }

        return new Geometry(
            checked((int)triangles),
            checked((int)vertices),
            Math.Round(max[0] - min[0], 3),
            Math.Round(max[1] - min[1], 3),
            Math.Round(max[2] - min[2], 3));
    }

    /// <summary>Full model metadata: geometry from the glb plus what the file list says about textures.</summary>
    public static ModelMetadata Describe(string packDirectory, IReadOnlyList<ModelFile> files, bool pbr, string textureResolution)
    {
        var glb = files.First(f => f.Format == "glb");
        var geometry = Inspect(PackPaths.Resolve(packDirectory, glb.Path));
        var maps = files.Where(f => f.Path.Contains("/textures/", StringComparison.Ordinal)).Select(f => f.Format).Order(StringComparer.Ordinal).ToList();
        return new ModelMetadata(
            geometry.Triangles,
            geometry.Vertices,
            geometry.Width,
            geometry.Height,
            geometry.Depth,
            Pbr: pbr,
            TextureResolution: maps.Count > 0 ? textureResolution : null,
            TextureMaps: maps,
            TotalBytes: files.Sum(f => f.Bytes));
    }
}
