using System;
using NjulfFramework.Rendering.Resources;
using Silk.NET.Vulkan;
using NjulfFramework.Rendering.Resources.Handles;
using Vma;

namespace NjulfFramework.Rendering.Memory
{
    /// <summary>
    /// Utility for dynamically resizing GPU buffers based on usage.
    /// Thread-safe and optimized for minimal overhead during render loop.
    /// </summary>
    public class BufferResizer : IDisposable
    {
        private readonly BufferManager _bufferManager;
        private readonly float _growthFactor = 1.5f; // Grow by 50% when resizing
        private readonly ulong _minBufferSize; // Minimum buffer size (e.g., 1 MB)
        private readonly object _resizeLock = new object(); // Thread safety lock

        public BufferResizer(BufferManager bufferManager, ulong minBufferSize = 1024 * 1024)
        {
            _bufferManager = bufferManager;
            _minBufferSize = minBufferSize;
        }

        /// <summary>
        /// Ensures the buffer is large enough to hold the required size.
        /// Resizes the buffer if necessary and returns the new buffer handle.
        /// </summary>
        public BufferHandle EnsureBufferCapacity(
            BufferHandle currentHandle,
            ulong requiredSize,
            BufferUsageFlags usage,
            MemoryUsage memoryUsage)
        {
            lock (_resizeLock)
            {
                if (currentHandle.IsValid)
                {
                    ulong currentSize = _bufferManager.GetBufferSize(currentHandle);
                    if (currentSize >= requiredSize)
                        return currentHandle; // No resize needed
                }

                // Calculate new size (grow by _growthFactor)
                ulong newSize = Math.Max(_minBufferSize, (ulong)(requiredSize * _growthFactor));
                if (currentHandle.IsValid)
                    _bufferManager.FreeBuffer(currentHandle);

                // Allocate new buffer
                return _bufferManager.AllocateBuffer(newSize, usage, memoryUsage);
            }
        }

        public void Dispose()
        {
            // No resources to dispose
        }
    }
}