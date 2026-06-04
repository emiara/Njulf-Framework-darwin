//SPDX-License-Identifier: MPL-2.0

using NjulfFramework.Core.Interfaces.Rendering;
using NjulfFramework.Rendering.Resources;
using NjulfFramework.Rendering.Resources.Descriptors;
using NjulfFramework.Rendering.Resources.Handles;

using static NjulfFramework.Rendering.Resources.Descriptors.BindlessBufferIndices;
using Silk.NET.Vulkan;
using Vma;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace NjulfFramework.Rendering.Pipeline;

/// <summary>
///     Tiled light culling compute pass for forward+ rendering.
///     Divides screen into tiles (16x16 pixels) and computes
///     which lights affect each tile. Output: TiledLightBuffer containing
///     per-tile light lists used by fragment shader.
///     Pattern:
///     1. Initialize with compute shader path
///     2. ComputePipeline handles GLSL compilation → SPIR-V → Pipeline
///     3. TiledLightCullingPass handles culling algorithm and buffer management
/// </summary>
public class TiledLightCullingPass : RenderGraphPass
{
    // Tile configuration
    private const uint TileSize = 16; // 16x16 pixel tiles
    private const uint MaxLightsPerTile = 256;
    private const uint MaxTiles = 1920 / TileSize * (1080 / TileSize) + 1; // ~6400 tiles @ 1080p

    private readonly ComputePipeline _computePipeline;
    private readonly Device _device;
    private readonly ILightManager _lightManager;

    private readonly BufferHandle _tiledLightHeaderBuffer;
    private readonly uint _tiledLightHeaderBufferIndex;
    private readonly Buffer _tiledLightHeaderBufferVk;

    private readonly BufferHandle _tiledLightIndicesBuffer;
    private readonly uint _tiledLightIndicesBufferIndex;
    private readonly Buffer _tiledLightIndicesBufferVk;
    private readonly Vk _vk;

    /// <summary>
    ///     Initialize tiled light culling pass.
    ///     Automatically:
    ///     1. Compiles light_cull.comp GLSL → SPIR-V using ShaderCompiler
    ///     2. Creates compute pipeline from SPIR-V
    ///     3. Allocates GPU buffers for tiled light data
    /// </summary>
    public TiledLightCullingPass(
        string name,
        Vk vk,
        Device device,
        ILightManager lightManager,
        BufferManager bufferManager,
        DescriptorSetLayouts descriptorLayouts,
        BindlessDescriptorHeap bindlessHeap,
        string computeShaderPath = "Shaders/light_cull.comp")
    {
        Name = name ?? "Tiled Light Culling";
        _vk = vk;
        _device = device;
        _lightManager = lightManager;

        // Step 1: Compile and create compute pipeline (same pattern as GraphicsPipeline)
        Console.WriteLine("🔧 Initializing tiled light culling pass...");
        _computePipeline = new ComputePipeline(vk, device, computeShaderPath, descriptorLayouts.BufferHeapLayout);

        // Step 2: Allocate tiled light header buffer (one header per tile)
        _tiledLightHeaderBuffer = bufferManager.AllocateBuffer(
            MaxTiles * 8, // 8 bytes per header (offset + count)
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryUsage.AutoPreferDevice);
        _tiledLightHeaderBufferVk = bufferManager.GetBuffer(_tiledLightHeaderBuffer);

        // Register with bindless heap at fixed indices
        // Scene buffers: 0=object, 1=material, 2=mesh
        // Mesh buffers: 3=vertex, 4=index, 5=meshlet, 6=meshletVertexIndex, 7=meshletTriangleIndex
        // Instance buffers: 8,9
        // Meshlet draw buffers: 10,11
        // Light buffers: 12=light, 13=header, 14=indices
        _tiledLightHeaderBufferIndex = TiledLightHeaderBuffer;
        _tiledLightIndicesBufferIndex = TiledLightIndicesBuffer;
        bindlessHeap.UpdateBuffer(TiledLightHeaderBuffer, _tiledLightHeaderBufferVk, MaxTiles * 8);

        // Step 3: Allocate tiled light indices buffer (all light indices for all tiles)
        _tiledLightIndicesBuffer = bufferManager.AllocateBuffer(
            MaxTiles * MaxLightsPerTile * 4, // 4 bytes per index
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryUsage.AutoPreferDevice);
        _tiledLightIndicesBufferVk = bufferManager.GetBuffer(_tiledLightIndicesBuffer);

        // Register with bindless heap at fixed index
        bindlessHeap.UpdateBuffer(TiledLightIndicesBuffer, _tiledLightIndicesBufferVk,
            MaxTiles * MaxLightsPerTile * 4);

        Console.WriteLine("✓ Tiled light culling pass initialized");
        Console.WriteLine($"  Tile size: {TileSize}x{TileSize} pixels");
        Console.WriteLine($"  Max tiles: {MaxTiles}");
        Console.WriteLine($"  Max lights per tile: {MaxLightsPerTile}");
    }

    /// <summary>
    ///     Bindless index for tiled light header buffer.
    /// </summary>
    public uint TiledLightHeaderBufferIndex => _tiledLightHeaderBufferIndex;

    /// <summary>
    ///     Bindless index for tiled light indices buffer.
    /// </summary>
    public uint TiledLightIndicesBufferIndex => _tiledLightIndicesBufferIndex;

    /// <summary>
    ///     Execute tiled light culling compute shader.
    /// </summary>
    public override unsafe void Execute(CommandBuffer cmd, RenderGraphContext ctx)
    {
        if (_lightManager.LightCount == 0)
            return;

        // Bind compute pipeline
        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, _computePipeline.Pipeline);

        // Bind descriptor sets (lights buffer via bindless)
        var descriptorSets = stackalloc DescriptorSet[1]
        {
            ctx.BindlessHeap.BufferSet // set=0: Light buffer + tiled buffers
        };
        _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute,
            _computePipeline.PipelineLayout, 0, 1, descriptorSets, 0, null);

        // Push constants: screen dimensions, light count, tile size
        var pushConstants = stackalloc uint[7]
        {
            ctx.Width,
            ctx.Height,
            _lightManager.LightCount,
            TileSize,
            _lightManager.LightBufferBindlessIndex,
            _tiledLightHeaderBufferIndex,
            _tiledLightIndicesBufferIndex
        };

        _vk.CmdPushConstants(cmd, _computePipeline.PipelineLayout,
            ShaderStageFlags.ComputeBit, 0, 28, pushConstants);


        // Dispatch compute shader
        // Each workgroup = 16x16 threads, one per tile
        var tilesX = (ctx.Width + TileSize - 1) / TileSize;
        var tilesY = (ctx.Height + TileSize - 1) / TileSize;

        _vk.CmdDispatch(cmd, tilesX, tilesY, 1);

        // Barrier: ensure compute shader finished writing before fragment shader reads
        var barrier = new BufferMemoryBarrier
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            Buffer = _tiledLightHeaderBufferVk,
            Offset = 0,
            Size = MaxTiles * 8
        };

        var barrier2 = new BufferMemoryBarrier
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            Buffer = _tiledLightIndicesBufferVk,
            Offset = 0,
            Size = MaxTiles * MaxLightsPerTile * 4
        };

        var barriers = stackalloc BufferMemoryBarrier[2] { barrier, barrier2 };
        _vk.CmdPipelineBarrier(
            cmd,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.FragmentShaderBit,
            DependencyFlags.None,
            0, null,
            2, barriers,
            0, null);
    }

    public void Dispose()
    {
        _computePipeline?.Dispose();
    }

    /// <summary>
    ///     Get tiled light header buffer for fragment shader access.
    /// </summary>
    public Buffer GetTiledLightHeaderBuffer()
    {
        return _tiledLightHeaderBufferVk;
    }

    /// <summary>
    ///     Get tiled light indices buffer for fragment shader access.
    /// </summary>
    public Buffer GetTiledLightIndicesBuffer()
    {
        return _tiledLightIndicesBufferVk;
    }
}