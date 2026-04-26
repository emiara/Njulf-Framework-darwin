// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using NjulfFramework.Rendering.Memory;
using NjulfFramework.Rendering.Resources;
using Silk.NET.Vulkan;
using NjulfFramework.Rendering.Resources.Descriptors;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace NjulfFramework.Rendering.Data;

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
    private MeshManager? _meshManager;
    private TextureManager? _textureManager;
    private BindlessDescriptorHeap? _bindlessHeap;
    private Sampler _defaultSampler;

    // Cache to avoid duplicate materials/meshes
    private readonly Dictionary<RenderingData.Material, uint> _materialIndexMap = new();
    private readonly Dictionary<RenderingData.Mesh, uint> _meshIndexMap = new();
    private readonly Dictionary<string, uint> _texturePathToIndexMap = new();

    public SceneDataBuilder()
    {
    }

    public void SetMeshManager(MeshManager? meshManager)
    {
        _meshManager = meshManager;
    }

    public void SetTextureManager(TextureManager? textureManager)
    {
        _textureManager = textureManager;
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
        _texturePathToIndexMap.Clear();
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
        var materialIdx = GetOrAddMaterial(obj.Material);

        // Get or add mesh
        var meshIdx = GetOrAddMesh(obj.Mesh);

        // Add object data
        var gpuObjectData = new GPUObjectData(
            obj.Transform,
            materialIdx,
            meshIdx,
            (uint)_objectData.Count
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

        var newIdx = (uint)_materialData.Count;
        
        // Get texture indices for PBR materials
        uint baseColorTexIdx = GetOrAddTextureIndex(material.BaseColorTexturePath);
        uint normalTexIdx = GetOrAddTextureIndex(material.NormalTexturePath);
        uint metallicRoughnessTexIdx = GetOrAddTextureIndex(material.MetallicRoughnessTexturePath);
        uint occlusionTexIdx = GetOrAddTextureIndex(material.OcclusionTexturePath);
        uint emissiveTexIdx = GetOrAddTextureIndex(material.EmissiveTexturePath);

        var gpuMaterial = new GPUMaterial(
            material.BaseColorFactor,
            baseColorTexIdx,
            normalTexIdx,
            metallicRoughnessTexIdx
        );

        _materialData.Add(gpuMaterial);
        _materialIndexMap[material] = newIdx;
        return newIdx;
    }

    /// <summary>
    /// Get or add a texture index for a texture path
    /// </summary>
    /// <summary>
    /// Get or add a texture index for a texture path
    /// </summary>
    private uint GetOrAddTextureIndex(string texturePath)
    {
        if (string.IsNullOrEmpty(texturePath))
            return uint.MaxValue;

        if (_texturePathToIndexMap.TryGetValue(texturePath, out var idx))
            return idx;

        // Load texture using texture manager
        if (_textureManager != null && _bindlessHeap != null)
        {
            try
            {
                // Load texture data from file
                var (pixels, width, height, components) = TextureLoader.LoadTextureFromFile(texturePath);

                // Determine format (use sRGB for base color textures, linear for others)
                var format = TextureLoader.GetVulkanFormat(components,
                    texturePath.EndsWith("baseColor", StringComparison.OrdinalIgnoreCase) ||
                    texturePath.EndsWith("albedo", StringComparison.OrdinalIgnoreCase) ||
                    texturePath.EndsWith("diffuse", StringComparison.OrdinalIgnoreCase));

                // Allocate texture in texture manager
                var textureHandle = _textureManager.AllocateTextureWithData(
                    (uint)width,
                    (uint)height,
                    format,
                    ImageUsageFlags.SampledBit,
                    pixels);

                // Get bindless texture index
                if (_bindlessHeap.TryAllocateTextureIndex(out var textureIndex))
                {
                    // Update the bindless descriptor heap with the new texture
                    var imageView = _textureManager.GetImageView(textureHandle);
                    _bindlessHeap.UpdateTexture(textureIndex, imageView, _defaultSampler);

                    _texturePathToIndexMap[texturePath] = textureIndex;
                    return textureIndex;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to load texture: " + texturePath + ": " + ex.Message);
            }
        } 
 
       // Fallback: return uint.MaxValue (no texture) 
       return uint.MaxValue; 
   }

    /// <summary>
    /// Add or retrieve a mesh's GPU index.
    /// </summary>
    private uint GetOrAddMesh(RenderingData.Mesh mesh)
    {
        if (_meshIndexMap.TryGetValue(mesh, out var idx))
            return idx;

        var newIdx = (uint)_meshData.Count;

        // Calculate bounding box
        var (bbMin, bbMax) = GPUMeshData.CalculateBoundingBox(mesh.Vertices);

        uint meshletOffset = 0;
        uint meshletCount = 0;
        if (_meshManager != null)
        {
            var entry = _meshManager.GetOrCreateMeshGpu(mesh);
            meshletOffset = entry.MeshletOffset;
            meshletCount = entry.MeshletCount;
        }

        var gpuMeshData = new GPUMeshData(
            uint.MaxValue, // TODO: get from buffer manager
            uint.MaxValue,
            (uint)mesh.Vertices.Length,
            (uint)mesh.Indices.Length,
            bbMin,
            bbMax,
            meshletOffset,
            meshletCount
        );

        _meshData.Add(gpuMeshData);
        _meshIndexMap[mesh] = newIdx;
        return newIdx;
    }

    /// <summary>
    /// Record copy commands to upload all scene data to GPU.
    /// The FrameUploadRing provides the staging buffers; you supply the destination buffers.
    /// </summary>
    public unsafe void UploadToGPU(Vk vk, CommandBuffer cmd, FrameUploadRing uploadRing,
        Buffer objectDataBuffer, Buffer materialDataBuffer, Buffer meshDataBuffer)
    {
        if (_objectData.Count == 0)
            return;

        // Convert arrays
        var objectArray = _objectData.ToArray();
        var materialArray = _materialData.ToArray();
        var meshArray = _meshData.ToArray();

        // Write to staging buffers
        uploadRing.WriteData(objectArray, out var objectSrcOffset);
        uploadRing.WriteData(materialArray, out var materialSrcOffset);
        uploadRing.WriteData(meshArray, out var meshSrcOffset);

        // Record copy commands
        var srcBuffer = uploadRing.CurrentUploadBuffer;

        // Copy object data
        var objectDataSize = (uint)objectArray.Length * GPUObjectData.GetSizeInBytes();
        if (objectDataSize > 0)
        {
            var objectCopy = new BufferCopy
            {
                SrcOffset = objectSrcOffset,
                DstOffset = 0,
                Size = objectDataSize
            };
            vk.CmdCopyBuffer(cmd, srcBuffer, objectDataBuffer, 1, &objectCopy);
        }

        // Copy material data (offset after object data)
        var materialDataSize = (uint)materialArray.Length * GPUMaterial.GetSizeInBytes();
        if (materialDataSize > 0)
        {
            var materialCopy = new BufferCopy
            {
                SrcOffset = materialSrcOffset,
                DstOffset = 0,
                Size = materialDataSize
            };
            vk.CmdCopyBuffer(cmd, srcBuffer, materialDataBuffer, 1, &materialCopy);
        }

        // Copy mesh data (offset after materials)
        var meshDataSize = (uint)meshArray.Length * GPUMeshData.GetSizeInBytes();
        if (meshDataSize > 0)
        {
            var meshCopy = new BufferCopy
            {
                SrcOffset = meshSrcOffset,
                DstOffset = 0,
                Size = meshDataSize
            };
            vk.CmdCopyBuffer(cmd, srcBuffer, meshDataBuffer, 1, &meshCopy);
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

    public void SetBindlessHeap(BindlessDescriptorHeap? bindlessHeap)
    {
        _bindlessHeap = bindlessHeap;
    }

    public void SetDefaultSampler(Sampler sampler)
    {
        _defaultSampler = sampler;
    }

    public void Dispose()
    {
        _objectData.Clear();
        _materialData.Clear();
        _meshData.Clear();
        _materialIndexMap.Clear();
        _meshIndexMap.Clear();
        _texturePathToIndexMap.Clear();
    }
}