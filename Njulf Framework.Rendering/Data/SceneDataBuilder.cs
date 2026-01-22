// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using Njulf_Framework.Rendering.Memory;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Njulf_Framework.Rendering.Data;

/// <summary>
/// Builds GPU scene data from CPU-side RenderObjects.
/// Collects object transforms, materials, and mesh references into arrays
/// ready for GPU upload via FrameUploadRing.
/// </summary>
public class SceneDataBuilder : IDisposable
{
    private readonly List<GPUObjectData> _objectData = new();
    private readonly List<GPUMaterial> _materialData = new();
    private readonly List<GPUMeshData> _meshData = new();

    // Cache to avoid duplicate materials/meshes
    private readonly Dictionary<RenderingData.Material, uint> _materialIndexMap = new();
    private readonly Dictionary<RenderingData.Mesh, uint> _meshIndexMap = new();

    public SceneDataBuilder()
    {
    }

    /// <summary>
    /// Clear all data at frame start.
    /// Call once per frame before adding objects.
    /// </summary>
    public void BeginFrame()
    {
        _objectData.Clear();
        _materialData.Clear();
        _meshData.Clear();
        _materialIndexMap.Clear();
        _meshIndexMap.Clear();
    }

    /// <summary>
    /// Add a render object to the scene data.
    /// Automatically deduplicates materials and meshes.
    /// </summary>
    public void AddObject(RenderingData.RenderObject obj)
    {
        if (obj == null || obj.Mesh == null || obj.Material == null)
            return;

        // Get or add material
        uint materialIdx = GetOrAddMaterial(obj.Material);

        // Get or add mesh
        uint meshIdx = GetOrAddMesh(obj.Mesh);

        // Add object data
        var gpuObjectData = new GPUObjectData(
            transform: obj.Transform,
            materialIndex: materialIdx,
            meshIndex: meshIdx,
            instanceIndex: (uint)_objectData.Count
        );

        _objectData.Add(gpuObjectData);
    }

    /// <summary>
    /// Add or retrieve a material's GPU index.
    /// </summary>
    private uint GetOrAddMaterial(RenderingData.Material material)
    {
        if (_materialIndexMap.TryGetValue(material, out var idx))
            return idx;

        uint newIdx = (uint)_materialData.Count;
        var gpuMaterial = new GPUMaterial(
            baseColor: material.Color,
            baseColorTextureIndex: uint.MaxValue,    // TODO: get from texture manager
            normalTextureIndex: uint.MaxValue,
            metallicRoughnessTextureIndex: uint.MaxValue
        );

        _materialData.Add(gpuMaterial);
        _materialIndexMap[material] = newIdx;
        return newIdx;
    }

    /// <summary>
    /// Add or retrieve a mesh's GPU index.
    /// </summary>
    private uint GetOrAddMesh(RenderingData.Mesh mesh)
    {
        if (_meshIndexMap.TryGetValue(mesh, out var idx))
            return idx;

        uint newIdx = (uint)_meshData.Count;

        // Calculate bounding box
        var (bbMin, bbMax) = GPUMeshData.CalculateBoundingBox(mesh.Vertices);

        var gpuMeshData = new GPUMeshData(
            vertexBufferIndex: uint.MaxValue,    // TODO: get from buffer manager
            indexBufferIndex: uint.MaxValue,
            vertexCount: (uint)mesh.Vertices.Length,
            indexCount: (uint)mesh.Indices.Length,
            boundingBoxMin: bbMin,
            boundingBoxMax: bbMax
        );

        _meshData.Add(gpuMeshData);
        _meshIndexMap[mesh] = newIdx;
        return newIdx;
    }

    /// <summary>
    /// Record copy commands to upload all scene data to GPU.
    /// The FrameUploadRing provides the staging buffers; you supply the destination buffers.
    /// </summary>
    public void UploadToGPU(CommandBuffer cmd, FrameUploadRing uploadRing, 
        Buffer objectDataBuffer, Buffer materialDataBuffer, Buffer meshDataBuffer)
    {
        if (_objectData.Count == 0)
            return;

        // Convert arrays
        var objectArray = _objectData.ToArray();
        var materialArray = _materialData.ToArray();
        var meshArray = _meshData.ToArray();

        // Write to staging buffers
        uploadRing.WriteData(objectArray);
        uploadRing.WriteData(materialArray);
        uploadRing.WriteData(meshArray);

        // Record copy commands
        var srcBuffer = uploadRing.CurrentUploadBuffer;

        // Copy object data
        uint objectDataSize = (uint)objectArray.Length * GPUObjectData.GetSizeInBytes();
        if (objectDataSize > 0)
        {
            var objectCopy = new BufferCopy
            {
                SrcOffset = 0,
                DstOffset = 0,
                Size = objectDataSize
            };
            // TODO: Vk!.CmdCopyBuffer(cmd, srcBuffer, objectDataBuffer, 1, &objectCopy);
        }

        // Copy material data (offset after object data)
        uint materialDataSize = (uint)materialArray.Length * GPUMaterial.GetSizeInBytes();
        if (materialDataSize > 0)
        {
            var materialCopy = new BufferCopy
            {
                SrcOffset = objectDataSize,
                DstOffset = 0,
                Size = materialDataSize
            };
            // TODO: Vk!.CmdCopyBuffer(cmd, srcBuffer, materialDataBuffer, 1, &materialCopy);
        }

        // Copy mesh data (offset after materials)
        uint meshDataSize = (uint)meshArray.Length * GPUMeshData.GetSizeInBytes();
        if (meshDataSize > 0)
        {
            var meshCopy = new BufferCopy
            {
                SrcOffset = objectDataSize + materialDataSize,
                DstOffset = 0,
                Size = meshDataSize
            };
            // TODO: Vk!.CmdCopyBuffer(cmd, srcBuffer, meshDataBuffer, 1, &meshCopy);
        }
    }

    /// <summary>
    /// Get total GPU upload size in bytes.
    /// Useful for validation before calling UploadToGPU.
    /// </summary>
    public ulong GetTotalUploadSizeInBytes()
    {
        ulong size = 0;
        size += (ulong)_objectData.Count * GPUObjectData.GetSizeInBytes();
        size += (ulong)_materialData.Count * GPUMaterial.GetSizeInBytes();
        size += (ulong)_meshData.Count * GPUMeshData.GetSizeInBytes();
        return size;
    }

    /// <summary>
    /// Get read-only arrays for inspection/debugging.
    /// </summary>
    public IReadOnlyList<GPUObjectData> ObjectData => _objectData.AsReadOnly();
    public IReadOnlyList<GPUMaterial> MaterialData => _materialData.AsReadOnly();
    public IReadOnlyList<GPUMeshData> MeshData => _meshData.AsReadOnly();

    public void Dispose()
    {
        _objectData.Clear();
        _materialData.Clear();
        _meshData.Clear();
        _materialIndexMap.Clear();
        _meshIndexMap.Clear();
    }
}
