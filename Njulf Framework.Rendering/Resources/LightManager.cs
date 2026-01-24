//SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using Silk.NET.Vulkan;
using Vma;
using Njulf_Framework.Rendering.Data;
using Njulf_Framework.Rendering.Core;
using Njulf_Framework.Rendering.Memory;
using Njulf_Framework.Rendering.Resources;
using Njulf_Framework.Rendering.Resources.Descriptors;
using Njulf_Framework.Rendering.Resources.Handles;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Njulf_Framework.Rendering.Resources;

/// <summary>
/// Manages dynamic lights for forward+ rendering.
/// Handles CPU-side light data and GPU uploads.
/// </summary>
public class LightManager : IDisposable
{
    private readonly BufferManager _bufferManager;
    private readonly Vk _vk;
    private readonly Device _device;

    /// <summary>
    /// CPU-side light data, updated per-frame.
    /// </summary>
    private List<GPULight> _lights = new();
    
    public uint LightBufferBindlessIndex { get; private set; }

    /// <summary>
    /// GPU buffer containing all light data.
    /// </summary>
    private BufferHandle _lightBuffer;
    private Buffer _lightBufferVk;
    private const ulong MaxLightCount = 1024;
    private const ulong LightBufferSize = MaxLightCount * 48;  // 48 bytes per light

    /// <summary>
    /// Initialize the light manager.
    /// </summary>
    public LightManager(Vk vk, Device device, BufferManager bufferManager, BindlessDescriptorHeap bindlessHeap)
    {
        _vk = vk;
        _device = device;
        _bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));

        // Create GPU light buffer
        _lightBuffer = _bufferManager.AllocateBuffer(
            LightBufferSize,
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryUsage.AutoPreferDevice);

        _lightBufferVk = _bufferManager.GetBuffer(_lightBuffer);
        
        // Register with bindless heap (two-step pattern)
        if (!bindlessHeap.TryAllocateBufferIndex(out var lightBufferIndex))
            throw new Exception("Failed to allocate bindless index for light buffer");
    
        LightBufferBindlessIndex = lightBufferIndex;
        bindlessHeap.UpdateBuffer(LightBufferBindlessIndex, _lightBufferVk, LightBufferSize);

        Console.WriteLine($"✓ Light manager initialized (max {MaxLightCount} lights)");
    }

    /// <summary>
    /// Get the GPU light buffer for bindless access.
    /// </summary>
    public Buffer GetLightBuffer() => _lightBufferVk;

    /// <summary>
    /// Get total light count.
    /// </summary>
    public uint LightCount => (uint)_lights.Count;

    /// <summary>
    /// Add a point light to the scene.
    /// </summary>
    public void AddPointLight(Vector3 position, float radius, Vector3 color, float intensity)
    {
        if (_lights.Count >= (int)MaxLightCount)
        {
            Console.WriteLine($"⚠ Light limit reached ({MaxLightCount})");
            return;
        }

        _lights.Add(GPULight.CreatePointLight(position, radius, color, intensity));
    }

    /// <summary>
    /// Remove all lights from the scene.
    /// </summary>
    public void ClearLights() => _lights.Clear();

    /// <summary>
    /// Upload all lights to GPU.
    /// Must be called once per frame after CPU updates.
    /// </summary>
    public unsafe void UploadToGPU(CommandBuffer transferCmd, FrameUploadRing uploadRing)
    {
        if (_lights.Count == 0)
            return;

        // Write lights to CPU staging buffer
        unsafe
        {
            var lightArray = _lights.ToArray();
            fixed (GPULight* ptr = lightArray)
            {
                uploadRing.WriteData(new ReadOnlySpan<GPULight>(ptr, _lights.Count));
            }
        }

        // Record copy from staging to GPU buffer
        var srcBuffer = uploadRing.CurrentUploadBuffer;
        var copyRegion = new BufferCopy
        {
            SrcOffset = 0,
            DstOffset = 0,
            Size = (ulong)(_lights.Count * sizeof(GPULight))
        };

        _vk.CmdCopyBuffer(transferCmd, srcBuffer, _lightBufferVk, 1, copyRegion);

        // Barrier: ensure copy is visible to compute shader
        var barrier = new BufferMemoryBarrier
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            Buffer = _lightBufferVk,
            Offset = 0,
            Size = (ulong)(_lights.Count * sizeof(GPULight))
        };

        _vk.CmdPipelineBarrier(
            transferCmd,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.ComputeShaderBit,
            DependencyFlags.None,
            0, null,
            1, &barrier,
            0, null);
    }

    /// <summary>
    /// Get a light by index (for CPU-side access).
    /// </summary>
    public GPULight? GetLight(int index)
    {
        if (index >= 0 && index < _lights.Count)
            return _lights[index];
        return null;
    }

    /// <summary>
    /// Get all lights (for debugging).
    /// </summary>
    public IReadOnlyList<GPULight> GetAllLights() => _lights.AsReadOnly();

    public void Dispose()
    {
        _lights.Clear();
    }
}
