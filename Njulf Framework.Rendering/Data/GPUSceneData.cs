// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using System.Runtime.InteropServices;

namespace Njulf_Framework.Rendering.Data;

/// <summary>
/// GPU-side data for a single renderable object.
/// This is uploaded to a storage buffer and indexed by shaders.
/// Highly optimized for GPU access patterns.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GPUObjectData
{
    /// <summary>
    /// Model-to-world transformation matrix.
    /// 64 bytes.
    /// </summary>
    public Matrix4x4 Transform;

    /// <summary>
    /// Index into the material array in the GPU bindless heap.
    /// </summary>
    public uint MaterialIndex;

    /// <summary>
    /// Index into the mesh data array (contains vertex/index buffer info).
    /// </summary>
    public uint MeshIndex;

    /// <summary>
    /// Unique instance identifier for this object.
    /// Used for hit shader identification in ray tracing.
    /// </summary>
    public uint InstanceIndex;

    /// <summary>
    /// Padding to align to 16-byte boundary (required for std430 layout in GLSL).
    /// </summary>
    public uint Padding;

    public GPUObjectData(Matrix4x4 transform, uint materialIndex, uint meshIndex, uint instanceIndex)
    {
        Transform = transform;
        MaterialIndex = materialIndex;
        MeshIndex = meshIndex;
        InstanceIndex = instanceIndex;
        Padding = 0;
    }

    /// <summary>
    /// Get the size of this struct in bytes.
    /// Must be verified to match shader layout.
    /// </summary>
    public static uint GetSizeInBytes()
    {
        return (uint)Marshal.SizeOf<GPUObjectData>();
    }
}

/// <summary>
/// GPU-side material data.
/// Contains color and texture indices for bindless access.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GPUMaterial
{
    /// <summary>
    /// Base color (sRGB).
    /// 16 bytes.
    /// </summary>
    public Vector4 BaseColor;

    /// <summary>
    /// Index into the texture bindless array for base color.
    /// Set to uint.MaxValue to disable.
    /// </summary>
    public uint BaseColorTextureIndex;

    /// <summary>
    /// Index into the texture bindless array for normal map.
    /// Set to uint.MaxValue to disable.
    /// </summary>
    public uint NormalTextureIndex;

    /// <summary>
    /// Index into the texture bindless array for metallic/roughness.
    /// Set to uint.MaxValue to disable.
    /// </summary>
    public uint MetallicRoughnessTextureIndex;

    /// <summary>
    /// Padding to align to 16-byte boundary.
    /// </summary>
    public uint Padding;

    public GPUMaterial(Vector4 baseColor, uint baseColorTextureIndex = uint.MaxValue,
        uint normalTextureIndex = uint.MaxValue, uint metallicRoughnessTextureIndex = uint.MaxValue)
    {
        BaseColor = baseColor;
        BaseColorTextureIndex = baseColorTextureIndex;
        NormalTextureIndex = normalTextureIndex;
        MetallicRoughnessTextureIndex = metallicRoughnessTextureIndex;
        Padding = 0;
    }

    /// <summary>
    /// Get the size of this struct in bytes.
    /// </summary>
    public static uint GetSizeInBytes()
    {
        return (uint)Marshal.SizeOf<GPUMaterial>();
    }
}

/// <summary>
/// GPU-side mesh data.
/// Contains references to GPU buffers and draw parameters.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GPUMeshData
{
    /// <summary>
    /// Index into the buffer bindless array for vertex data.
    /// </summary>
    public uint VertexBufferIndex;

    /// <summary>
    /// Index into the buffer bindless array for index data.
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
    /// Minimum corner of bounding box (for frustum culling / ray tracing).
    /// 12 bytes.
    /// </summary>
    public Vector3 BoundingBoxMin;

    /// <summary>
    /// Padding to align Vector3 to 16 bytes.
    /// </summary>
    public uint Padding1;

    /// <summary>
    /// Maximum corner of bounding box.
    /// 12 bytes.
    /// </summary>
    public Vector3 BoundingBoxMax;

    /// <summary>
    /// Padding to align to 16-byte boundary.
    /// </summary>
    public uint Padding2;

    public GPUMeshData(uint vertexBufferIndex, uint indexBufferIndex,
        uint vertexCount, uint indexCount, Vector3 boundingBoxMin, Vector3 boundingBoxMax)
    {
        VertexBufferIndex = vertexBufferIndex;
        IndexBufferIndex = indexBufferIndex;
        VertexCount = vertexCount;
        IndexCount = indexCount;
        BoundingBoxMin = boundingBoxMin;
        BoundingBoxMax = boundingBoxMax;
        Padding1 = 0;
        Padding2 = 0;
    }

    /// <summary>
    /// Get the size of this struct in bytes.
    /// </summary>
    public static uint GetSizeInBytes()
    {
        return (uint)Marshal.SizeOf<GPUMeshData>();
    }

    /// <summary>
    /// Calculate bounding box from vertices.
    /// </summary>
    public static (Vector3 min, Vector3 max) CalculateBoundingBox(RenderingData.Vertex[] vertices)
    {
        if (vertices.Length == 0)
            return (Vector3.Zero, Vector3.Zero);

        Vector3 min = vertices[0].Position;
        Vector3 max = vertices[0].Position;

        for (int i = 1; i < vertices.Length; i++)
        {
            min = Vector3.Min(min, vertices[i].Position);
            max = Vector3.Max(max, vertices[i].Position);
        }

        return (min, max);
    }
}

/// <summary>
/// Container for all per-frame GPU scene data.
/// Represents the current frame's state on the GPU.
/// </summary>
public class GPUSceneFrame
{
    /// <summary>
    /// Array of object data to upload to GPU.
    /// Index corresponds to instance ID in ray tracing.
    /// </summary>
    public List<GPUObjectData> ObjectData { get; } = new();

    /// <summary>
    /// Array of material data to upload to GPU.
    /// Referenced by MaterialIndex in GPUObjectData.
    /// </summary>
    public List<GPUMaterial> MaterialData { get; } = new();

    /// <summary>
    /// Array of mesh data to upload to GPU.
    /// Referenced by MeshIndex in GPUObjectData.
    /// </summary>
    public List<GPUMeshData> MeshData { get; } = new();

    /// <summary>
    /// Clear all frame data (called at beginning of each frame).
    /// </summary>
    public void Clear()
    {
        ObjectData.Clear();
        MaterialData.Clear();
        MeshData.Clear();
    }

    /// <summary>
    /// Get the total upload size in bytes for this frame.
    /// </summary>
    public ulong GetTotalUploadSizeInBytes()
    {
        ulong size = 0;
        size += (ulong)ObjectData.Count * GPUObjectData.GetSizeInBytes();
        size += (ulong)MaterialData.Count * GPUMaterial.GetSizeInBytes();
        size += (ulong)MeshData.Count * GPUMeshData.GetSizeInBytes();
        return size;
    }

    /// <summary>
    /// Verify all structures are POD-safe and correctly sized.
    /// Call this during initialization to catch errors early.
    /// </summary>
    public static void ValidateStructureSizes()
    {
        // All GPU structures must be multiples of 16 bytes (std430 layout requirement)
        uint objectDataSize = GPUObjectData.GetSizeInBytes();
        uint materialSize = GPUMaterial.GetSizeInBytes();
        uint meshDataSize = GPUMeshData.GetSizeInBytes();

        if (objectDataSize % 16 != 0)
            throw new InvalidOperationException(
                $"GPUObjectData size ({objectDataSize}) must be multiple of 16 bytes");

        if (materialSize % 16 != 0)
            throw new InvalidOperationException(
                $"GPUMaterial size ({materialSize}) must be multiple of 16 bytes");

        if (meshDataSize % 16 != 0)
            throw new InvalidOperationException(
                $"GPUMeshData size ({meshDataSize}) must be multiple of 16 bytes");

        System.Diagnostics.Debug.WriteLine($"✓ GPUObjectData size: {objectDataSize} bytes");
        System.Diagnostics.Debug.WriteLine($"✓ GPUMaterial size: {materialSize} bytes");
        System.Diagnostics.Debug.WriteLine($"✓ GPUMeshData size: {meshDataSize} bytes");
    }
}
