// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Njulf_Framework.Rendering.Resources;
using Njulf_Framework.Rendering.Resources.Handles;
using Silk.NET.Vulkan;
using Vma;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Njulf_Framework.Rendering.Memory;

/// <summary>
/// Per-frame CPU-visible upload buffers backed by VMA.
/// Used as a ring so the CPU can write every frame without stalling the GPU.
/// </summary>
public sealed class FrameUploadRing : IDisposable
{
    private readonly BufferManager _bufferManager;
    private readonly BufferHandle[] _uploadBuffers;
    private readonly IntPtr[] _cpuMappings;
    private uint _frameIndex;

    /// <summary>
    /// Number of frames in the ring. 3 is a good default (triple buffering).
    /// </summary>
    private const uint MaxFrames = 3;

    /// <summary>
    /// Size of each per-frame upload buffer in bytes.
    /// Adjust when you know your real scene data size.
    /// </summary>
    private const ulong UploadSize = 256 * 1024 * 1024; // 256 MB

    public FrameUploadRing(BufferManager bufferManager)
    {
        _bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
        _uploadBuffers = new BufferHandle[MaxFrames];
        _cpuMappings = new IntPtr[MaxFrames];

        // Create one big CPU-visible + mapped buffer per frame
        for (int i = 0; i < MaxFrames; i++)
        {
            _uploadBuffers[i] = _bufferManager.AllocateBuffer(
                UploadSize,
                BufferUsageFlags.TransferSrcBit,
                MemoryUsage.Auto,
                AllocationCreateFlags.HostAccessSequentialWriteBit |
                AllocationCreateFlags.MappedBit
            );

            _cpuMappings[i] = _bufferManager.GetMappedPointer(_uploadBuffers[i]);
            if (_cpuMappings[i] == IntPtr.Zero)
                throw new InvalidOperationException($"FrameUploadRing: failed to map upload buffer for frame {i}.");
        }
    }

    /// <summary>
    /// Current frame slot index in the ring.
    /// </summary>
    private uint CurrentFrameIndex => _frameIndex % MaxFrames;

    /// <summary>
    /// Write a contiguous block of POD data into the current frame's upload buffer.
    /// This writes at the beginning of the buffer; higher-level code is responsible
    /// for tracking offsets if multiple writes are needed.
    /// </summary>
    public unsafe void WriteData<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        if (data.IsEmpty)
            return;

        var dstPtr = _cpuMappings[CurrentFrameIndex];
        if (dstPtr == IntPtr.Zero)
            throw new InvalidOperationException("Current upload buffer is not mapped.");

        ulong bytes = (ulong)(data.Length * sizeof(T));
        if (bytes > UploadSize)
            throw new InvalidOperationException(
                $"FrameUploadRing: write size ({bytes} bytes) exceeds UploadSize ({UploadSize} bytes).");

        fixed (T* src = data)
        {
            System.Buffer.MemoryCopy(src, dstPtr.ToPointer(), UploadSize, bytes);
        }
    }

    /// <summary>
    /// Get the VkBuffer handle for the current frame's upload buffer.
    /// Use this as the src buffer in vkCmdCopyBuffer / vkCmdCopyBufferToImage commands.
    /// </summary>
    public Buffer CurrentUploadBuffer
        => _bufferManager.GetBuffer(_uploadBuffers[CurrentFrameIndex]);

    /// <summary>
    /// Advance to the next frame in the ring.
    /// Call this once per frame after all transfers using the current upload buffer are submitted.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NextFrame()
    {
        _frameIndex++;
    }

    public void Dispose()
    {
        for (int i = 0; i < _uploadBuffers.Length; i++)
        {
            if (_uploadBuffers[i].IsValid)
            {
                _bufferManager.DestroyBuffer(_uploadBuffers[i]);
            }

            _cpuMappings[i] = IntPtr.Zero;
        }
    }
}
