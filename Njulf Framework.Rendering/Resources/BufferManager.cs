using Silk.NET.Vulkan;
using System;
using Njulf_Framework.Rendering.Data;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Njulf_Framework.Rendering.Resources;

public class BufferManager : IDisposable
{
        private readonly Vk _vk;
        private readonly Device _device;
        private readonly PhysicalDevice _physicalDevice;
        private readonly Queue _transferQueue;
        private readonly uint _transferQueueFamily;

        // Track buffer-memory pairs for cleanup
        private readonly Dictionary<Buffer, DeviceMemory> _bufferMemories = new();
        
        private CommandPool _transferCommandPool;

        public struct BufferAllocation
        {
        public Buffer Buffer;
        public DeviceMemory Memory;
    }

    public BufferManager(Vk vk, Device device, PhysicalDevice physicalDevice, Queue transferQueue, uint transferQueueFamily)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        _transferQueue = transferQueue;
        _transferQueueFamily = transferQueueFamily;
    }

    /// <summary>
    /// Create a GPU buffer with data and return both buffer and memory handles.
    /// </summary>
    public unsafe BufferAllocation CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, void* data = null)
    {
        var createInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        if (_vk.CreateBuffer(_device, &createInfo, null, out var buffer) != Result.Success)
        {
            throw new Exception("Failed to create buffer");
        }

        // Allocate memory
        _vk.GetBufferMemoryRequirements(_device, buffer, out var memRequirements);
        var memType = FindMemoryType(memRequirements.MemoryTypeBits, properties);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = memType
        };

        if (_vk.AllocateMemory(_device, &allocInfo, null, out var bufferMemory) != Result.Success)
        {
            throw new Exception("Failed to allocate buffer memory");
        }

        _vk.BindBufferMemory(_device, buffer, bufferMemory, 0);

        // Copy data if provided
        if (data != null)
        {
            CopyDataToBuffer(bufferMemory, size, data);
        }

        // Track for cleanup
        _bufferMemories[buffer] = bufferMemory;

        return new BufferAllocation { Buffer = buffer, Memory = bufferMemory };
    }

    /// <summary>
    /// Create a vertex buffer and upload data.
    /// </summary>
    public unsafe Buffer CreateVertexBuffer(void* vertices, ulong sizeInBytes)
    {
        // Create staging buffer
        var staging = CreateBuffer(
            sizeInBytes,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            vertices);
    
        // Create device local buffer
        var vertexBuffer = CreateBuffer(
            sizeInBytes,
            BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
            MemoryPropertyFlags.DeviceLocalBit);
    
        // Copy data
        CopyBuffer(staging.Buffer, vertexBuffer.Buffer, sizeInBytes);
    
        // Cleanup staging buffer
        _vk.DestroyBuffer(_device, staging.Buffer, null);
        _vk.FreeMemory(_device, staging.Memory, null);
        _bufferMemories.Remove(staging.Buffer);
    
        return vertexBuffer.Buffer;
    }


    /// <summary>
    /// Create an index buffer and upload data.
    /// </summary>
    public unsafe Buffer CreateIndexBuffer(uint* indices, uint indexCount)
    {
        var sizeInBytes = (ulong)(indexCount * sizeof(uint));

        var staging = CreateBuffer(
            sizeInBytes,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            indices);

        var indexBuffer = CreateBuffer(
            sizeInBytes,
            BufferUsageFlags.TransferDstBit | BufferUsageFlags.IndexBufferBit,
            MemoryPropertyFlags.DeviceLocalBit);

        CopyBuffer(staging.Buffer, indexBuffer.Buffer, sizeInBytes);

        _vk.DestroyBuffer(_device, staging.Buffer, null);
        _vk.FreeMemory(_device, staging.Memory, null);
        _bufferMemories.Remove(staging.Buffer);

        return indexBuffer.Buffer;
    }

    /// <summary>
    /// Create a uniform buffer (dynamic per frame).
    /// </summary>
    public unsafe BufferAllocation CreateUniformBuffer(ulong sizeInBytes)
    {
        return CreateBuffer(
            sizeInBytes,
            BufferUsageFlags.UniformBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
    }

    /// <summary>
    /// Get the memory handle for a buffer (for mapping operations).
    /// </summary>
    public DeviceMemory GetBufferMemory(Buffer buffer)
    {
        if (_bufferMemories.TryGetValue(buffer, out var memory))
        {
            return memory;
        }
        throw new ArgumentException($"Buffer not found in manager");
    }

    private unsafe void CopyDataToBuffer(DeviceMemory memory, ulong size, void* data)
    {
        void* memPtr = null;
        _vk.MapMemory(_device, memory, 0, size, MemoryMapFlags.None, &memPtr);
        System.Buffer.MemoryCopy(data, memPtr, (long)size, (long)size);
        _vk.UnmapMemory(_device, memory);
    }

    private unsafe void CopyBuffer(Buffer srcBuffer, Buffer dstBuffer, ulong size)
    {
        // Create command pool if it doesn't exist
        if (_transferCommandPool.Handle == 0)
        {
            var poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = _transferQueueFamily,
                Flags = CommandPoolCreateFlags.TransientBit
            };

            if (_vk.CreateCommandPool(_device, &poolInfo, null, out _transferCommandPool) != Result.Success)
            {
                throw new Exception("Failed to create transfer command pool");
            }
        }

        // Allocate command buffer
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = _transferCommandPool,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        _vk.AllocateCommandBuffers(_device, &allocInfo, &commandBuffer);

        // Begin recording
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _vk.BeginCommandBuffer(commandBuffer, &beginInfo);

        // Record copy command
        var copyRegion = new BufferCopy
        {
            SrcOffset = 0,
            DstOffset = 0,
            Size = size
        };

        _vk.CmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, 1, &copyRegion);

        // End recording
        _vk.EndCommandBuffer(commandBuffer);

        // Submit and wait
        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        _vk.QueueSubmit(_transferQueue, 1, &submitInfo, default);
        _vk.QueueWaitIdle(_transferQueue);

        // Cleanup command buffer
        _vk.FreeCommandBuffers(_device, _transferCommandPool, 1, &commandBuffer);
        
        Console.WriteLine($"✓ Copied {size} bytes between buffers");
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var memProperties);

        for (uint i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << (int)i)) != 0 && 
                (memProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new Exception("Failed to find suitable memory type");
    }

    public unsafe void Dispose()
    {
        // Destroy command pool
        if (_transferCommandPool.Handle != 0)
        {
            _vk.DestroyCommandPool(_device, _transferCommandPool, null);
        }

        foreach (var kvp in _bufferMemories)
        {
            _vk.DestroyBuffer(_device, kvp.Key, null);
            _vk.FreeMemory(_device, kvp.Value, null);
        }
        _bufferMemories.Clear();
    }
}