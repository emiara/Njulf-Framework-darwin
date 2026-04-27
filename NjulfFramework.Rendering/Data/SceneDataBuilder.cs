// SPDX-License-Identifier: MPL-2.0

using NjulfFramework.Core.Interfaces.Scene;
using NjulfFramework.Rendering.Memory;
using NjulfFramework.Rendering.Resources;
using NjulfFramework.Rendering.Resources.Descriptors;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace NjulfFramework.Rendering.Data;

/// <summary>
///     Builds GPU scene data from CPU-side RenderObjects.
///     Collects object transforms, materials, and mesh references into arrays
///     ready for GPU upload via FrameUploadRing.
/// </summary>
public class SceneDataBuilder : ISceneDataBuilder
{
    private readonly List<GPUMaterial> _materialData = new();

    // Cache to avoid duplicate materials/meshes
    private readonly Dictionary<RenderingData.Material, uint> _materialIndexMap = new();
    private readonly List<GPUMeshData> _meshData = new();
    private readonly Dictionary<RenderingData.Mesh, uint> _meshIndexMap = new();
    private readonly List<GPUObjectData> _objectData = new();
    private readonly Dictionary<string, uint> _texturePathToIndexMap = new();
    private readonly BindlessDescriptorHeap? _bindlessHeap;
    private readonly MeshManager? _meshManager;
    private readonly TextureManager? _textureManager;
    private Sampler _defaultSampler;

    public SceneDataBuilder(MeshManager meshManager, TextureManager textureManager, BindlessDescriptorHeap bindlessHeap)
    {
        _meshManager = meshManager ?? throw new ArgumentNullException(nameof(meshManager));
        _textureManager = textureManager ?? throw new ArgumentNullException(nameof(textureManager));
        _bindlessHeap = bindlessHeap ?? throw new ArgumentNullException(nameof(bindlessHeap));
    }

    /// <summary>
    ///     Get read-only arrays for inspection/debugging.
    /// </summary>
    public IReadOnlyList<GPUObjectData> ObjectData => _objectData.AsReadOnly();

    public IReadOnlyList<GPUMaterial> MaterialData => _materialData.AsReadOnly();
    public IReadOnlyList<GPUMeshData> MeshData => _meshData.AsReadOnly();

    public void BuildSceneData()
    {
        // No-op: data is accumulated via AddObject and flushed in UploadToGPU.
        // Retained for ISceneDataBuilder compliance.
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

    /// <summary>
    ///     Clear all data at frame start.
    ///     Call once per frame before adding objects.
    /// </summary>
    public void Clear()
    {
        _objectData.Clear();
        _materialData.Clear();
        _meshData.Clear();
        _materialIndexMap.Clear();
        _meshIndexMap.Clear();
        _texturePathToIndexMap.Clear();
    }

    /// <summary>
    ///     Called by VulkanRenderer at the start of each frame. Delegates to Clear().
    /// </summary>
    public void BeginFrame() => Clear();

    /// <summary>
    ///     Add a renderable object to the scene data using the backend-agnostic descriptor.
    ///     Looks up the concrete RenderObject by mesh name for GPU data population.
    /// </summary>
    public void AddObject(RenderObjectDescriptor descriptor)
    {
        // Resolve the mesh from the mesh manager by name
        if (_meshManager == null)
            return;

        var mesh = _meshManager.TryGetMeshByName(descriptor.MeshName);
        if (mesh == null)
            return;

        // Use a default material when no material name is provided
        var material = new RenderingData.Material(
            descriptor.MaterialName ?? "default",
            "Shaders/test_vert.spv");

        AddObjectInternal(mesh, material, descriptor.Transform);
    }

    /// <summary>
    ///     Add a concrete RenderObject directly (used internally by VulkanRenderer).
    /// </summary>
    public void AddObject(Data.RenderingData.RenderObject obj)
    {
        if (obj?.Mesh == null || obj.Material == null)
            return;

        AddObjectInternal(obj.Mesh, obj.Material, obj.Transform);
    }

    private void AddObjectInternal(RenderingData.Mesh mesh, RenderingData.Material material, System.Numerics.Matrix4x4 transform)
    {
        var materialIdx = GetOrAddMaterial(material);
        var meshIdx = GetOrAddMesh(mesh);

        var gpuObjectData = new GPUObjectData(
            transform,
            materialIdx,
            meshIdx,
            (uint)_objectData.Count
        );

        _objectData.Add(gpuObjectData);
    }

    // ... existing code ...

    public void SetDefaultSampler(Sampler sampler)
    {
        _defaultSampler = sampler;
    }

    /// <summary>
    ///     Add or retrieve a material's GPU index.
    /// </summary>
    private uint GetOrAddMaterial(RenderingData.Material material)
    {
        if (_materialIndexMap.TryGetValue(material, out var idx))
            return idx;

        var newIdx = (uint)_materialData.Count;

        var baseColorTexIdx = GetOrAddTextureIndex(material.BaseColorTexturePath);
        var normalTexIdx = GetOrAddTextureIndex(material.NormalTexturePath);
        var metallicRoughnessTexIdx = GetOrAddTextureIndex(material.MetallicRoughnessTexturePath);
        var occlusionTexIdx = GetOrAddTextureIndex(material.OcclusionTexturePath);
        var emissiveTexIdx = GetOrAddTextureIndex(material.EmissiveTexturePath);

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

    private uint GetOrAddTextureIndex(string texturePath)
    {
        if (string.IsNullOrEmpty(texturePath))
            return uint.MaxValue;

        if (_texturePathToIndexMap.TryGetValue(texturePath, out var idx))
            return idx;

        if (_textureManager != null && _bindlessHeap != null)
            try
            {
                var (pixels, width, height, components) = TextureLoader.LoadTextureFromFile(texturePath);

                var format = TextureLoader.GetVulkanFormat(components,
                    texturePath.EndsWith("baseColor", StringComparison.OrdinalIgnoreCase) ||
                    texturePath.EndsWith("albedo", StringComparison.OrdinalIgnoreCase) ||
                    texturePath.EndsWith("diffuse", StringComparison.OrdinalIgnoreCase));

                var textureHandle = _textureManager.AllocateTextureWithData(
                    (uint)width,
                    (uint)height,
                    format,
                    ImageUsageFlags.SampledBit,
                    pixels);

                if (_bindlessHeap.TryAllocateTextureIndex(out var textureIndex))
                {
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

        return uint.MaxValue;
    }

    private uint GetOrAddMesh(RenderingData.Mesh mesh)
    {
        if (_meshIndexMap.TryGetValue(mesh, out var idx))
            return idx;

        var newIdx = (uint)_meshData.Count;

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
            uint.MaxValue,
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
    ///     Record copy commands to upload all scene data to GPU.
    /// </summary>
    public unsafe void UploadToGPU(Vk vk, CommandBuffer cmd, FrameUploadRing uploadRing,
        Buffer objectDataBuffer, Buffer materialDataBuffer, Buffer meshDataBuffer)
    {
        if (_objectData.Count == 0)
            return;

        var objectArray = _objectData.ToArray();
        var materialArray = _materialData.ToArray();
        var meshArray = _meshData.ToArray();

        uploadRing.WriteData<GPUObjectData>(objectArray, out var objectSrcOffset);
        uploadRing.WriteData<GPUMaterial>(materialArray, out var materialSrcOffset);
        uploadRing.WriteData<GPUMeshData>(meshArray, out var meshSrcOffset);

        var srcBuffer = uploadRing.CurrentUploadBuffer;

        var objectDataSize = (uint)objectArray.Length * GPUObjectData.GetSizeInBytes();
        if (objectDataSize > 0)
        {
            var objectCopy = new BufferCopy { SrcOffset = objectSrcOffset, DstOffset = 0, Size = objectDataSize };
            vk.CmdCopyBuffer(cmd, srcBuffer, objectDataBuffer, 1, &objectCopy);
        }

        var materialDataSize = (uint)materialArray.Length * GPUMaterial.GetSizeInBytes();
        if (materialDataSize > 0)
        {
            var materialCopy = new BufferCopy { SrcOffset = materialSrcOffset, DstOffset = 0, Size = materialDataSize };
            vk.CmdCopyBuffer(cmd, srcBuffer, materialDataBuffer, 1, &materialCopy);
        }

        var meshDataSize = (uint)meshArray.Length * GPUMeshData.GetSizeInBytes();
        if (meshDataSize > 0)
        {
            var meshCopy = new BufferCopy { SrcOffset = meshSrcOffset, DstOffset = 0, Size = meshDataSize };
            vk.CmdCopyBuffer(cmd, srcBuffer, meshDataBuffer, 1, &meshCopy);
        }
    }

    public ulong GetTotalUploadSizeInBytes()
    {
        ulong size = 0;
        size += (ulong)_objectData.Count * GPUObjectData.GetSizeInBytes();
        size += (ulong)_materialData.Count * GPUMaterial.GetSizeInBytes();
        size += (ulong)_meshData.Count * GPUMeshData.GetSizeInBytes();
        return size;
    }
}