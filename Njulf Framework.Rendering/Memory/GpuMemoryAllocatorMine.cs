// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Njulf_Framework.Rendering.Memory;

public class GpuMemoryAllocatorMine : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;

    public GpuMemoryAllocatorMine(Vk vk, Device device, PhysicalDevice physicalDevice)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
    }

    /// <summary>
    /// Allocates a buffer on the GPU.
    /// TODO: Integrate with Vulkan Memory Allocator for better management.
    /// </summary>
    public unsafe (Buffer Buffer, DeviceMemory Memory) AllocateBuffer(
        ulong size,
        BufferUsageFlags usage,
        MemoryPropertyFlags requiredProperties)
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

        _vk.GetBufferMemoryRequirements(_device, buffer, out var memRequirements);

        var memoryIndex = FindMemoryType(memRequirements.MemoryTypeBits, requiredProperties);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = memoryIndex
        };

        if (_vk.AllocateMemory(_device, &allocInfo, null, out var memory) != Result.Success)
        {
            throw new Exception("Failed to allocate memory for buffer");
        }

        _vk.BindBufferMemory(_device, buffer, memory, 0);

        return (buffer, memory);
    }

    /// <summary>
    /// Allocates an image on the GPU.
    /// </summary>
    public unsafe (Image Image, DeviceMemory Memory) AllocateImage(
        uint width,
        uint height,
        Format format,
        ImageUsageFlags usage,
        MemoryPropertyFlags requiredProperties)
    {
        var createInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D { Width = width, Height = height, Depth = 1 },
            MipLevels = 1,
            ArrayLayers = 1,
            Format = format,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            Samples = SampleCountFlags.Count1Bit
        };

        if (_vk.CreateImage(_device, &createInfo, null, out var image) != Result.Success)
        {
            throw new Exception("Failed to create image");
        }

        _vk.GetImageMemoryRequirements(_device, image, out var memRequirements);

        var memoryIndex = FindMemoryType(memRequirements.MemoryTypeBits, requiredProperties);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = memoryIndex
        };

        if (_vk.AllocateMemory(_device, &allocInfo, null, out var memory) != Result.Success)
        {
            throw new Exception("Failed to allocate memory for image");
        }

        _vk.BindImageMemory(_device, image, memory, 0);

        return (image, memory);
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

    public unsafe void FreeBuffer(Buffer buffer, DeviceMemory memory)
    {
        if (buffer.Handle != 0)
            _vk.DestroyBuffer(_device, buffer, null);
        if (memory.Handle != 0)
            _vk.FreeMemory(_device, memory, null);
    }

    public unsafe void FreeImage(Image image, DeviceMemory memory)
    {
        if (image.Handle != 0)
            _vk.DestroyImage(_device, image, null);
        if (memory.Handle != 0)
            _vk.FreeMemory(_device, memory, null);
    }

    public void Dispose()
    {
        // Cleanup code when allocator is destroyed
    }
}