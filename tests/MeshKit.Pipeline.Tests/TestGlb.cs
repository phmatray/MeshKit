using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace MeshKit.Pipeline.Tests;

/// <summary>Builds a minimal but valid GLB: a unit cube (8 vertices, 12 triangles, 1×1×1 m) so the inspector has real numbers to read.</summary>
public static class TestGlb
{
    public static byte[] Cube(float size = 1f, int copies = 1)
    {
        var positions = new List<float>();
        var indices = new List<ushort>();
        for (var c = 0; c < copies; c++)
        {
            var b = positions.Count / 3;
            foreach (var (x, y, z) in new[] { (0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0), (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1) })
            {
                positions.AddRange([x * size, y * size, z * size]);
            }

            foreach (var i in new ushort[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0 })
            {
                indices.Add((ushort)(b + i));
            }
        }

        var posBytes = new byte[positions.Count * 4];
        for (var i = 0; i < positions.Count; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(posBytes.AsSpan(i * 4), positions[i]);
        }

        var idxBytes = new byte[indices.Count * 2];
        for (var i = 0; i < indices.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(idxBytes.AsSpan(i * 2), indices[i]);
        }

        var idxPadded = idxBytes.Concat(new byte[(4 - idxBytes.Length % 4) % 4]).ToArray();
        var bin = posBytes.Concat(idxPadded).ToArray();

        var gltf = new
        {
            asset = new { version = "2.0" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { mesh = 0 } },
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 }, indices = 1 } } } },
            buffers = new[] { new { byteLength = bin.Length } },
            bufferViews = new object[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = posBytes.Length },
                new { buffer = 0, byteOffset = posBytes.Length, byteLength = idxBytes.Length },
            },
            accessors = new object[]
            {
                new { bufferView = 0, componentType = 5126, count = positions.Count / 3, type = "VEC3", min = new[] { 0f, 0f, 0f }, max = new[] { size, size, size } },
                new { bufferView = 1, componentType = 5123, count = indices.Count, type = "SCALAR" },
            },
        };
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(gltf));
        var jsonPadded = json.Concat(Enumerable.Repeat((byte)0x20, (4 - json.Length % 4) % 4)).ToArray();

        using var ms = new MemoryStream();
        void U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); ms.Write(b); }
        ms.Write("glTF"u8);
        U32(2);
        U32((uint)(12 + 8 + jsonPadded.Length + 8 + bin.Length));
        U32((uint)jsonPadded.Length);
        ms.Write("JSON"u8);
        ms.Write(jsonPadded);
        U32((uint)bin.Length);
        ms.Write("BIN\0"u8);
        ms.Write(bin);
        return ms.ToArray();
    }
}
