// SPDX-License-Identifier: MPL-2.0

using NjulfFramework.Core.Interfaces.Scene;
using NjulfFramework.Rendering.Memory;
using NjulfFramework.Rendering.Resources;
using NjulfFramework.Rendering.Resources.Handles;
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
    private readonly List<GPUInstanceData> _instanceData = new();
    private readonly List<GPUMeshletDraw> _meshletDraws = new();

    // Cache to avoid duplicate materials/meshes
    private readonly Dictionary<RenderingData.Material, uint> _materialIndexMap = new();
    private readonly List<GPUMeshData> _meshData = new();
    private readonly Dictionary<RenderingData.Mesh, uint> _meshIndexMap = new();
    private readonly List<GPUObjectData> _objectData = new();
    // Maps texture path to (bindless_index, texture_handle) for proper cleanup
    private readonly Dictionary<string, (uint BindlessIndex, TextureHandle TextureHandle)> _texturePathToIndexMap = new();
    // Track textures that have been loaded but not yet uploaded to GPU
    private readonly Dictionary<TextureHandle, (byte[] Pixels, uint Width, uint Height, Format Format)> _pendingTextureUploads = new();
    private readonly BindlessDescriptorHeap? _bindlessHeap;
    private readonly MeshManager? _meshManager;
    private readonly TextureManager? _textureManager;
    private Sampler _defaultSampler;

    // Track textures that need acquire barriers for QFOT
    private readonly List<(Image Image, uint TransferQueueFamily, uint GraphicsQueueFamily)> _texturesNeedingAcquire = new();
    
    // Store queue families for QFOT
    private uint _lastTransferQueueFamily;
    private uint _lastGraphicsQueueFamily;
    
    private uint _lastMeshletDrawCount;

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
    
    /// <summary> Per-frame instance records (GPU-driven). </summary>
    public IReadOnlyList<GPUInstanceData> InstanceData => _instanceData.AsReadOnly();

    /// <summary> Per-frame flat (instance, meshlet) draw list (GPU-driven). </summary>
    public IReadOnlyList<GPUMeshletDraw> MeshletDraws => _meshletDraws.AsReadOnly();

    /// <summary> Total number of meshlet draws this frame (== task workgroups to dispatch). </summary>
    public uint MeshletDrawCount => (uint)_meshletDraws.Count;

    /// <summary>
    ///     Get the material index for a given material.
    ///     Returns uint.MaxValue if material not found.
    /// </summary>
    public uint GetMaterialIndex(RenderingData.Material material)
    {
        if (_materialIndexMap.TryGetValue(material, out var idx))
            return idx;
        return uint.MaxValue;
    }

    /// <summary>
    ///     Get the mesh index for a given mesh.
    ///     Returns uint.MaxValue if mesh not found.
    /// </summary>
    public uint GetMeshIndex(RenderingData.Mesh mesh)
    {
        if (_meshIndexMap.TryGetValue(mesh, out var idx))
            return idx;
        return uint.MaxValue;
    }

    public void BuildSceneData()
    {
        // No-op: data is accumulated via AddObject and flushed in UploadToGPU.
        // Retained for ISceneDataBuilder compliance.
    }

    public void Dispose()
    {
        // Free all loaded textures and their bindless indices
        foreach (var (_, value) in _texturePathToIndexMap)
        {
            _bindlessHeap?.FreeTextureIndex(value.BindlessIndex);
            _textureManager?.FreeTexture(value.TextureHandle);
        }
        
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
        _instanceData.Clear();
        _meshletDraws.Clear();
        _materialIndexMap.Clear();
        _meshIndexMap.Clear();
        // NOTE: _texturePathToIndexMap is NOT cleared here intentionally.
        // Textures are persistent resources that should be loaded once and reused across frames.
        // Clearing it every frame causes memory leaks as textures are reloaded repeatedly.
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
        
        if (_meshManager != null)
        {
            var entry = _meshManager.GetOrCreateMeshGpu(mesh);
            var instanceIndex = (uint)_instanceData.Count;

            _instanceData.Add(new GPUInstanceData
            {
                Model = transform,
                MaterialIndex = materialIdx,
                MeshletBaseOffset = entry.MeshletOffset,
                MeshletCount = entry.MeshletCount,
                Pad = 0
            });

            // Industry standard: Validate meshlet indices are within bounds
            // Prevents STATUS_HEAP_CORRUPTION (0xC0000374) from out-of-bounds GPU buffer access
            if (entry.MeshletOffset == uint.MaxValue)
                throw new InvalidOperationException($"Mesh '{mesh.Name}' has invalid meshlet offset (uint.MaxValue)");
            if (entry.MeshletCount == uint.MaxValue)
                throw new InvalidOperationException($"Mesh '{mesh.Name}' has invalid meshlet count (uint.MaxValue)");
            
            uint totalMeshletCount = (uint)(_meshManager?.TotalMeshletCount ?? 0);
            for (uint m = 0; m < entry.MeshletCount; m++)
            {
                uint meshletIndex = entry.MeshletOffset + m;
                // Check for overflow and validate against total meshlet count
                if (meshletIndex < entry.MeshletOffset)
                    throw new InvalidOperationException($"Meshlet index overflow for mesh '{mesh.Name}'");
                if (meshletIndex >= totalMeshletCount)
                    throw new InvalidOperationException($"Mesh '{mesh.Name}': meshlet index {meshletIndex} exceeds total meshlet count {totalMeshletCount}");
                
                _meshletDraws.Add(new GPUMeshletDraw
                {
                    InstanceIndex = instanceIndex,
                    MeshletIndex = meshletIndex
                });
            }
        }
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
            material.MetallicFactor,
            material.RoughnessFactor,
            material.NormalScale,
            material.OcclusionStrength,
            material.EmissiveFactor,
            baseColorTexIdx,
            normalTexIdx,
            metallicRoughnessTexIdx,
            occlusionTexIdx,
            emissiveTexIdx
        );

        _materialData.Add(gpuMaterial);
        _materialIndexMap[material] = newIdx;
        return newIdx;
    }

    private uint GetOrAddTextureIndex(string texturePath)
    {
        if (string.IsNullOrEmpty(texturePath))
            return uint.MaxValue;

        if (_texturePathToIndexMap.TryGetValue(texturePath, out var entry))
            return entry.BindlessIndex;

        if (_textureManager != null && _bindlessHeap != null)
            try
            {
                var (pixels, width, height, components) = TextureLoader.LoadTextureFromFile(texturePath);

                // Determine if texture should use sRGB format
                // Base color / albedo / diffuse textures should use sRGB
                // Check both filename and full path for texture type keywords
                var fileName = Path.GetFileNameWithoutExtension(texturePath);
                var shouldUseSRGB = fileName.Contains("baseColor", StringComparison.OrdinalIgnoreCase) ||
                                   fileName.Contains("albedo", StringComparison.OrdinalIgnoreCase) ||
                                   fileName.Contains("diffuse", StringComparison.OrdinalIgnoreCase) ||
                                   texturePath.Contains("baseColor", StringComparison.OrdinalIgnoreCase) ||
                                   texturePath.Contains("albedo", StringComparison.OrdinalIgnoreCase) ||
                                   texturePath.Contains("diffuse", StringComparison.OrdinalIgnoreCase);
                
                var format = TextureLoader.GetVulkanFormat(components, shouldUseSRGB);

                var textureHandle = _textureManager.AllocateTexture(
                    (uint)width,
                    (uint)height,
                    format,
                    ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit);

                // Store pixels for later upload to GPU
                _pendingTextureUploads[textureHandle] = (pixels, (uint)width, (uint)height, format);

                if (_bindlessHeap.TryAllocateTextureIndex(out var textureIndex))
                {
                    var imageView = _textureManager.GetImageView(textureHandle);
                    _bindlessHeap.UpdateTexture(textureIndex, imageView, _defaultSampler);

                    _texturePathToIndexMap[texturePath] = (textureIndex, textureHandle);
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
        UploadToGPU(vk, cmd, Vk.QueueFamilyIgnored, Vk.QueueFamilyIgnored, uploadRing,
            objectDataBuffer, materialDataBuffer, meshDataBuffer);
    }

    /// <summary>
    ///     Record copy commands to upload all scene data to GPU with proper queue family ownership transfer.
    /// </summary>
    /// <param name="vk">Vulkan API</param>
    /// <param name="transferCommandBuffer">Command buffer for transfer queue operations</param>
    /// <param name="transferQueueFamily">Queue family index for transfer operations</param>
    /// <param name="graphicsQueueFamily">Queue family index for graphics operations</param>
    /// <param name="uploadRing">Upload ring buffer</param>
    /// <param name="objectDataBuffer">Destination buffer for object data</param>
    /// <param name="materialDataBuffer">Destination buffer for material data</param>
    /// <param name="meshDataBuffer">Destination buffer for mesh data</param>
    public unsafe void UploadToGPU(Vk vk, CommandBuffer transferCommandBuffer,
        uint transferQueueFamily, uint graphicsQueueFamily, FrameUploadRing uploadRing,
        Buffer objectDataBuffer, Buffer materialDataBuffer, Buffer meshDataBuffer)
    {
        if (_objectData.Count == 0 && _pendingTextureUploads.Count == 0)
            return;

        // Store queue families for later use by RecordAcquireBarriers
        _lastTransferQueueFamily = transferQueueFamily;
        _lastGraphicsQueueFamily = graphicsQueueFamily;

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
            vk.CmdCopyBuffer(transferCommandBuffer, srcBuffer, objectDataBuffer, 1, &objectCopy);
        }

        var materialDataSize = (uint)materialArray.Length * GPUMaterial.GetSizeInBytes();
        if (materialDataSize > 0)
        {
            var materialCopy = new BufferCopy { SrcOffset = materialSrcOffset, DstOffset = 0, Size = materialDataSize };
            vk.CmdCopyBuffer(transferCommandBuffer, srcBuffer, materialDataBuffer, 1, &materialCopy);
        }

        var meshDataSize = (uint)meshArray.Length * GPUMeshData.GetSizeInBytes();
        if (meshDataSize > 0)
        {
            var meshCopy = new BufferCopy { SrcOffset = meshSrcOffset, DstOffset = 0, Size = meshDataSize };
            vk.CmdCopyBuffer(transferCommandBuffer, srcBuffer, meshDataBuffer, 1, &meshCopy);
        }

        // Upload pending texture data to GPU with proper QFOT
        if (_pendingTextureUploads.Count > 0)
            UploadPendingTextures(vk, transferCommandBuffer, transferQueueFamily, graphicsQueueFamily,
                uploadRing, srcBuffer);
    }

    /// <summary>
    /// Upload texture data from staging buffer to GPU images.
    /// </summary>
    private unsafe void UploadPendingTextures(Vk vk, CommandBuffer cmd, FrameUploadRing uploadRing, Buffer srcBuffer)
    {
        UploadPendingTextures(vk, cmd, Vk.QueueFamilyIgnored, Vk.QueueFamilyIgnored, uploadRing, srcBuffer);
    }

    /// <summary>
    /// Upload texture data from staging buffer to GPU images with proper queue family ownership transfer.
    /// 
    /// Industry-standard QFOT pattern:
    /// 1. On transfer queue: copy + release barrier (TRANSFER -> BOTTOM_OF_PIPE, queue release from transfer -> graphics)
    /// 2. On graphics queue: acquire barrier (TOP_OF_PIPE -> FRAGMENT_SHADER, queue acquire from transfer -> graphics)
    ///    (acquire barriers are recorded separately via RecordAcquireBarriers)
    /// </summary>
    private unsafe void UploadPendingTextures(Vk vk, CommandBuffer transferCmd,
        uint transferQueueFamily, uint graphicsQueueFamily, FrameUploadRing uploadRing, Buffer srcBuffer)
    {
        if (_textureManager == null)
            return;

        // Clear previous acquire tracking
        _texturesNeedingAcquire.Clear();

        // Check if we're using separate queues (QFOT needed)
        bool useQfot = transferQueueFamily != graphicsQueueFamily &&
                       transferQueueFamily != Vk.QueueFamilyIgnored &&
                       graphicsQueueFamily != Vk.QueueFamilyIgnored;

        foreach (var (textureHandle, textureData) in _pendingTextureUploads)
        {
            var (pixels, width, height, format) = textureData;

            // Write texture pixels to staging buffer
            uploadRing.WriteData(pixels, out var textureSrcOffset);

            var image = _textureManager.GetImage(textureHandle);

            var region = new BufferImageCopy
            {
                BufferOffset = textureSrcOffset,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                ImageOffset = new Offset3D { X = 0, Y = 0, Z = 0 },
                ImageExtent = new Extent3D { Width = width, Height = height, Depth = 1 }
            };

            var subresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            };

            if (useQfot)
            {
                // === TRANSFER QUEUE OPERATIONS ===
                
                // Transition: UNDEFINED -> TRANSFER_DST_OPTIMAL
                var barrier = new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    SrcAccessMask = 0,
                    DstAccessMask = AccessFlags.TransferWriteBit,
                    OldLayout = ImageLayout.Undefined,
                    NewLayout = ImageLayout.TransferDstOptimal,
                    SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    Image = image,
                    SubresourceRange = subresourceRange
                };

                vk.CmdPipelineBarrier(transferCmd, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0,
                    0, null, 0, null, 1, &barrier);

                // Copy from staging buffer to image
                vk.CmdCopyBufferToImage(transferCmd, srcBuffer, image, ImageLayout.TransferDstOptimal, 1, &region);

                // Release barrier: TRANSFER_DST_OPTIMAL -> TRANSFER_DST_OPTIMAL with queue family release
                // This releases ownership from transfer queue family to graphics queue family
                barrier.OldLayout = ImageLayout.TransferDstOptimal;
                barrier.NewLayout = ImageLayout.TransferDstOptimal;
                barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                barrier.DstAccessMask = 0;
                barrier.SrcQueueFamilyIndex = transferQueueFamily;
                barrier.DstQueueFamilyIndex = graphicsQueueFamily;

                vk.CmdPipelineBarrier(transferCmd, PipelineStageFlags.TransferBit, PipelineStageFlags.BottomOfPipeBit, 0,
                    0, null, 0, null, 1, &barrier);

                // Track texture for acquire barrier on graphics queue (will use _last* queue families)
                _texturesNeedingAcquire.Add((image, transferQueueFamily, graphicsQueueFamily));
            }
            else
            {
                // === SINGLE QUEUE FALLBACK (original behavior) ===
                
                // Transition: UNDEFINED -> TRANSFER_DST_OPTIMAL
                var barrier = new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    SrcAccessMask = 0,
                    DstAccessMask = AccessFlags.TransferWriteBit,
                    OldLayout = ImageLayout.Undefined,
                    NewLayout = ImageLayout.TransferDstOptimal,
                    SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    Image = image,
                    SubresourceRange = subresourceRange
                };

                vk.CmdPipelineBarrier(transferCmd, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0,
                    0, null, 0, null, 1, &barrier);

                // Copy from staging buffer to image
                vk.CmdCopyBufferToImage(transferCmd, srcBuffer, image, ImageLayout.TransferDstOptimal, 1, &region);

                // Transition: TRANSFER_DST_OPTIMAL -> SHADER_READ_ONLY_OPTIMAL
                barrier.OldLayout = ImageLayout.TransferDstOptimal;
                barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
                barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                barrier.DstAccessMask = AccessFlags.ShaderReadBit;

                vk.CmdPipelineBarrier(transferCmd, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0,
                    0, null, 0, null, 1, &barrier);
            }

            Console.WriteLine("Uploaded texture " + textureHandle.Index + " to GPU");
        }

        _pendingTextureUploads.Clear();
    }

    /// <summary>
    /// Record acquire barriers for QFOT on the graphics command buffer.
    /// Must be called while the graphics command buffer is being recorded.
    /// </summary>
    public unsafe void RecordAcquireBarriers(Vk vk, CommandBuffer graphicsCmd)
    {
        if (_texturesNeedingAcquire.Count == 0)
            return;

        var barriers = stackalloc ImageMemoryBarrier[_texturesNeedingAcquire.Count];
        int barrierCount = 0;

        foreach (var (image, transferQueueFamily, graphicsQueueFamily) in _texturesNeedingAcquire)
        {
            barriers[barrierCount] = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.ShaderReadBit,
                OldLayout = ImageLayout.TransferDstOptimal,
                NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                SrcQueueFamilyIndex = transferQueueFamily,
                DstQueueFamilyIndex = graphicsQueueFamily,
                Image = image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };
            barrierCount++;
        }

        if (barrierCount > 0)
        {
            vk.CmdPipelineBarrier(
                graphicsCmd,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.FragmentShaderBit,
                0,
                0, null,
                0, null,
                (uint)barrierCount, barriers);
        }

        _texturesNeedingAcquire.Clear();
    }

    public ulong GetTotalUploadSizeInBytes()
    {
        ulong size = 0;
        size += (ulong)_objectData.Count * GPUObjectData.GetSizeInBytes();
        size += (ulong)_materialData.Count * GPUMaterial.GetSizeInBytes();
        size += (ulong)_meshData.Count * GPUMeshData.GetSizeInBytes();
        return size;
    }

    /// <summary>
    ///     Upload per-frame instance and meshlet-draw lists for GPU-driven rendering.
    /// </summary>
    public unsafe void UploadInstanceAndDrawData(Vk vk, CommandBuffer transferCmd, FrameUploadRing uploadRing,
        Buffer instanceBuffer, Buffer meshletDrawBuffer)
    {
        if (_instanceData.Count == 0 || _meshletDraws.Count == 0)
            return;

        var instanceArray = _instanceData.ToArray();
        var drawArray = _meshletDraws.ToArray();
        var currentDrawCount = (uint)drawArray.Length;

        uploadRing.WriteData<GPUInstanceData>(instanceArray, out var instanceSrc);
        uploadRing.WriteData<GPUMeshletDraw>(drawArray, out var drawSrc);

        var src = uploadRing.CurrentUploadBuffer;

        var instanceSize = (ulong)instanceArray.Length * GPUInstanceData.GetSizeInBytes();
        var instanceCopy = new BufferCopy { SrcOffset = instanceSrc, DstOffset = 0, Size = instanceSize };
        vk.CmdCopyBuffer(transferCmd, src, instanceBuffer, 1, &instanceCopy);

        var drawSize = (ulong)drawArray.Length * GPUMeshletDraw.GetSizeInBytes();
        var drawCopy = new BufferCopy { SrcOffset = drawSrc, DstOffset = 0, Size = drawSize };
        vk.CmdCopyBuffer(transferCmd, src, meshletDrawBuffer, 1, &drawCopy);

        // Clear stale entries if count decreased
        if (currentDrawCount < _lastMeshletDrawCount)
        {
            var clearSize = _lastMeshletDrawCount * GPUMeshletDraw.GetSizeInBytes() - drawSize;
            uploadRing.WriteData(new GPUMeshletDraw[1] { new GPUMeshletDraw() }, out var clearSrc);
            var clearCopy = new BufferCopy { SrcOffset = clearSrc, DstOffset = drawSize, Size = clearSize };
            vk.CmdCopyBuffer(transferCmd, src, meshletDrawBuffer, 1, &clearCopy);
        }

        _lastMeshletDrawCount = currentDrawCount;
    }
    
}