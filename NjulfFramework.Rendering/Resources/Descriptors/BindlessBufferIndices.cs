// SPDX-License-Identifier: MPL-2.0

namespace NjulfFramework.Rendering.Resources.Descriptors;

/// <summary>
/// Centralized constants for bindless descriptor heap buffer indices.
/// All indices must be unique and match shader expectations.
/// </summary>
public static class BindlessBufferIndices
{
    /// <summary>Object data buffer (per-object transforms, etc.)</summary>
    public const uint ObjectBuffer = 0;

    /// <summary>Material data buffer (PBR material parameters)</summary>
    public const uint MaterialBuffer = 1;

    /// <summary>Scene mesh metadata buffer (mesh bounds, LOD info)</summary>
    public const uint SceneMeshBuffer = 2;

    /// <summary>Vertex buffer for mesh shading pipeline</summary>
    public const uint VertexBuffer = 3;

    /// <summary>Index buffer for traditional indexed drawing</summary>
    public const uint IndexBuffer = 4;

    /// <summary>Meshlet buffer (meshlet definitions)</summary>
    public const uint MeshletBuffer = 5;

    /// <summary>Meshlet vertex index buffer (vertex remapping for meshlets)</summary>
    public const uint MeshletVertexIndexBuffer = 6;

    /// <summary>Meshlet triangle index buffer (triangle remapping for meshlets)</summary>
    public const uint MeshletTriangleIndexBuffer = 7;

    /// <summary>Base index for per-frame instance buffers (frame 0 at 8, frame 1 at 9)</summary>
    public const uint InstanceBufferBase = 8;

    /// <summary>Base index for per-frame meshlet draw buffers (frame 0 at 10, frame 1 at 11)</summary>
    public const uint MeshletDrawBufferBase = 10;

    // Light culling buffers (must be after mesh buffers, before per-frame)
    /// <summary>Light data buffer for forward+ rendering</summary>
    public const uint LightBuffer = 12;

    /// <summary>Tiled light culling header buffer (per-tile light offsets/counts)</summary>
    public const uint TiledLightHeaderBuffer = 13;

    /// <summary>Tiled light culling indices buffer (all light indices for all tiles)</summary>
    public const uint TiledLightIndicesBuffer = 14;
}
