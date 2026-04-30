using System.Numerics;
using NjulfFramework.Core.Enums;
using NjulfFramework.Core.Math;

namespace NjulfFramework.Core.Interfaces.Rendering;

/// <summary>
///     Renderer-agnostic vertex. Matches the GPU vertex layout (32 bytes):
///     Position (12) + Normal (12) + TexCoord (8) = 32 bytes.
/// </summary>
public struct MeshVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TexCoord;
}

/// <summary>
///     Read-only view of a mesh as seen by the renderer.
/// </summary>
public interface IMesh
{
    string Name { get; }
    BoundingBox Bounds { get; }
    string MaterialName { get; }
    PrimitiveMode PrimitiveMode { get; }

    MeshVertex[] Vertices { get; }
    uint[]       Indices  { get; }
    Vector3 BoundingBoxMin { get; }
    Vector3 BoundingBoxMax { get; }
}