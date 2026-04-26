//SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using NjulfFramework.Rendering.Data;
using NjulfFramework.Rendering.Memory;
using NjulfFramework.Rendering.Resources.Descriptors;
using NjulfFramework.Rendering.Resources.Handles;
using Silk.NET.Vulkan;
using Vma;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace NjulfFramework.Rendering.Resources;

/// <summary>
///     Manages dynamic lights for forward+ rendering.
///     Handles CPU-side light data and GPU uploads.
/// </summary>
public class LightManager : IDisposable
{
    private const ulong MaxLightCount = 1024;
    private const ulong LightBufferSize = MaxLightCount * 48; // 48 bytes per light
    private readonly BufferManager _bufferManager;
    private readonly Device _device;

    /// <summary>
    ///     GPU buffer containing all light data.
    /// </summary>
    private readonly BufferHandle _lightBuffer;

    private readonly Buffer _lightBufferVk;

    /// <summary>
    ///     CPU-side light data, updated per-frame.
    /// </summary>
    private readonly List<GPULight> _lights = new();

    private readonly Vk _vk;

    /// <summary>
    ///     Initialize the light manager.
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

    public uint LightBufferBindlessIndex { get; }

    /// <summary>
    ///     Get total light count.
    /// </summary>
    public uint LightCount => (uint)_lights.Count;

    public void Dispose()
    {
        _lights.Clear();
    }

    /// <summary>
    ///     Get the GPU light buffer for bindless access.
    /// </summary>
    public Buffer GetLightBuffer()
    {
        return _lightBufferVk;
    }

    /// <summary>
    ///     Add a point light to the scene.
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
    ///     Remove all lights from the scene.
    /// </summary>
    public void ClearLights()
    {
        _lights.Clear();
    }

    /// <summary>
    ///     Upload all lights to GPU.
    ///     Must be called once per frame after CPU updates.
    /// </summary>
    public unsafe void UploadToGPU(CommandBuffer transferCmd, FrameUploadRing uploadRing)
    {
        if (_lights.Count == 0)
            return;

        // Write lights to CPU staging buffer
        ulong srcOffset = 0;
        var lightArray = _lights.ToArray();
        fixed (GPULight* ptr = lightArray)
        {
            uploadRing.WriteData(new ReadOnlySpan<GPULight>(ptr, _lights.Count), out srcOffset);
        }

        // Record copy from staging to GPU buffer
        var srcBuffer = uploadRing.CurrentUploadBuffer;
        var copyRegion = new BufferCopy
        {
            SrcOffset = srcOffset,
            DstOffset = 0,
            Size = (ulong)(_lights.Count * sizeof(GPULight))
        };

        _vk.CmdCopyBuffer(transferCmd, srcBuffer, _lightBufferVk, 1, copyRegion);
    }

    /// <summary>
    ///     Get a light by index (for CPU-side access).
    /// </summary>
    public GPULight? GetLight(int index)
    {
        if (index >= 0 && index < _lights.Count)
            return _lights[index];
        return null;
    }

    /// <summary>
    ///     Get all lights (for debugging).
    /// </summary>
    public IReadOnlyList<GPULight> GetAllLights()
    {
        return _lights.AsReadOnly();
    }
}