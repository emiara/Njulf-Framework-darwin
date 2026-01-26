// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Numerics;

namespace Njulf_Framework.Rendering.RenderingData;

/// <summary>
/// GPU-side mesh metadata for bindless access.
/// Stores buffer indices and mesh geometry info.
/// Matches shader layout for GPU-driven rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GPUMeshData
{
    /// <summary>
    /// Bindless index into vertex buffer array (set=0).
    /// </summary>
    public uint VertexBufferIndex;

    /// <summary>
    /// Bindless index into index buffer array (set=0).
    /// </summary>
    public uint IndexBufferIndex;

    /// <summary>
    /// Number of vertices in this mesh.
    /// </summary>
    public uint VertexCount;

    /// <summary>
    /// Number of indices in this mesh.
    /// </summary>
    public uint IndexCount;

    /// <summary>
    /// Bounding box min corner.
    /// </summary>
    public Vector3 BoundingBoxMin;

    private uint Padding1;

    /// <summary>
    /// Bounding box max corner.
    /// </summary>
    public Vector3 BoundingBoxMax;

    private uint Padding2;

    public uint MeshletOffset;
    public uint MeshletCount;
    private uint Padding3;
    private uint Padding4;
}
