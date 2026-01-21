// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using System;
using Njulf_Framework.Rendering.Data;
using Vma;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Njulf_Framework.Rendering.Resources;

public sealed unsafe class BufferManager : IDisposable
{
    private readonly Vk _vk;
    private readonly Allocator* _allocator;

    private sealed class BufferEntry
    {
        public Buffer Handle;
        public Allocation* Allocation;  
        public ulong Size;
        public IntPtr MappedData;
    }

    private readonly Dictionary<uint, BufferEntry> _buffers = new();
    private uint _nextId = 1;

    public BufferManager(Vk vk, Allocator* allocator)
    {
        _vk = vk;
        _allocator = allocator;
    }

    /// <summary>
    /// Allocate a new GPU buffer.
    /// </summary>
    public Handles.BufferHandle AllocateBuffer(
        ulong size,
        BufferUsageFlags usage,
        MemoryUsage memUsage,
        AllocationCreateFlags flags = 0)
    {
        if (size == 0)
            throw new ArgumentException("Buffer size must be > 0", nameof(size));

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        var allocInfo = new AllocationCreateInfo
        {
            Usage = memUsage,
            Flags = flags
        };

        // Buffer buffer;
        // Allocation* alloc;
        // AllocationInfo allocInfo2;
        // var result = Apis.CreateBuffer(_allocator, &bufferInfo, &allocInfo,
        //     &buffer, &alloc, &allocInfo2);
        //
        // if (result != Result.Success)
        // {
        //     throw new InvalidOperationException(
        //         $"Failed to allocate buffer: {result}");
        // }
        BufferCreateInfo* pBufferInfo = &bufferInfo;
        AllocationCreateInfo* pAllocInfo = &allocInfo;

        Buffer buffer;
        Allocation* allocation;
        AllocationInfo allocationInfo;

        var result = Apis.CreateBuffer(
            _allocator,
            pBufferInfo,
            pAllocInfo,
            &buffer,
            &allocation,
            &allocationInfo);

        if (result != Result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to allocate buffer: {result}");
        }

        IntPtr mappedPtr = (IntPtr)allocationInfo.PMappedData;

        var id = _nextId++;
        _buffers[id] = new BufferEntry
        {
            Handle = buffer,
            Allocation = allocation,
            Size = size,
            MappedData = mappedPtr
        };

        return new Handles.BufferHandle(id, 1);
    }

    public Buffer GetBuffer(Handles.BufferHandle handle)
    {
        if (!_buffers.TryGetValue(handle.Index, out var entry))
            throw new InvalidOperationException($"Buffer handle {handle} not found");
        return entry.Handle;
    }

    public Allocation* GetAllocation(Handles.BufferHandle handle)
    {
        if (!_buffers.TryGetValue(handle.Index, out var entry))
            throw new InvalidOperationException($"Buffer handle {handle} not found");
        return entry.Allocation;
    }

    public ulong GetBufferSize(Handles.BufferHandle handle)
    {
        if (!_buffers.TryGetValue(handle.Index, out var entry))
            throw new InvalidOperationException($"Buffer handle {handle} not found");
        return entry.Size;
    }

    /// <summary>
    /// Get device address for ray tracing or GPU-driven submission.
    /// Requires BufferUsageFlags.ShaderDeviceAddress set at creation time.
    /// </summary>
    public ulong GetBufferDeviceAddress(Handles.BufferHandle handle)
    {
        var buffer = GetBuffer(handle);
        var addrInfo = new BufferDeviceAddressInfo
        {
            SType = StructureType.BufferDeviceAddressInfo,
            Buffer = buffer
        };
        return _vk.GetBufferDeviceAddress(_vk.CurrentDevice!.Value, addrInfo);
    }

    /// <summary>
    /// Get mapped CPU pointer for writing (buffer must be allocated with Mapped flag).
    /// </summary>
    public IntPtr GetMappedPointer(Handles.BufferHandle handle)
    {
        if (!_buffers.TryGetValue(handle.Index, out var entry))
            throw new InvalidOperationException($"Buffer handle {handle} not found");

        if (entry.MappedData == default)
            throw new InvalidOperationException(
                $"Buffer was not allocated with Mapped flag");

        return entry.MappedData;
    }

    /// <summary>
    /// Write data to a mapped buffer (CPU-side).
    /// </summary>
    public void WriteData<T>(Handles.BufferHandle handle, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        if (data.IsEmpty)
            return;

        var ptr = GetMappedPointer(handle);
        var size = GetBufferSize(handle);
        var bytesNeeded = (ulong)(data.Length * sizeof(T));

        if (bytesNeeded > size)
            throw new InvalidOperationException(
                $"Data size {bytesNeeded} exceeds buffer size {size}");

        fixed (T* src = data)
        {
            System.Buffer.MemoryCopy(src, ptr.ToPointer(), (long)size, (long)bytesNeeded);
        }
    }

    /// <summary>
    /// Flush a mapped buffer range to GPU (needed for non-coherent memory).
    /// </summary>
    public void FlushBuffer(Handles.BufferHandle handle, ulong offset = 0, ulong size = ulong.MaxValue)
    {
        var alloc = GetAllocation(handle);
        var actualSize = size == ulong.MaxValue ? GetBufferSize(handle) : size;
        Apis.FlushAllocation(_allocator, alloc, offset, actualSize);
    }

    /// <summary>
    /// Invalidate a mapped buffer range (reading from GPU).
    /// </summary>
    public void InvalidateBuffer(Handles.BufferHandle handle, ulong offset = 0, ulong size = ulong.MaxValue)
    {
        var alloc = GetAllocation(handle);
        var actualSize = size == ulong.MaxValue ? GetBufferSize(handle) : size;
        Apis.InvalidateAllocation(_allocator, alloc, offset, actualSize);
    }

    /// <summary>
    /// Free a buffer handle. The handle becomes invalid after this.
    /// </summary>
    public void FreeBuffer(Handles.BufferHandle handle)
    {
        if (!_buffers.Remove(handle.Index, out var entry))
            return;
        
        Apis.DestroyBuffer(_allocator, entry.Handle, entry.Allocation);
        
    }

    public void Dispose()
    {
        foreach (var (_, entry) in _buffers)
        {
            Apis.DestroyBuffer(_allocator, entry.Handle, entry.Allocation);
        }
        _buffers.Clear();
    }
}