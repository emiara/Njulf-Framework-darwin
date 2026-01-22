// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;

namespace Njulf_Framework.Rendering.Core;

public class CommandBufferManager : IDisposable
{
    private readonly VulkanContext _vulkanContext;
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly uint _queueFamilyIndex;

    // Graphics command pool + buffers
    private CommandPool _commandPool;
    private CommandBuffer[] _commandBuffers = null!;

    public CommandPool CommandPool => _commandPool;
    public CommandBuffer[] CommandBuffers => _commandBuffers;

    // Transfer command pool + buffers
    private CommandPool _transferCommandPool;
    public CommandBuffer[] TransferCommandBuffers { get; private set; }

    public CommandBufferManager(VulkanContext vulkanContext, uint queueFamilyIndex, uint bufferCount = 2)
    {
        _vulkanContext = vulkanContext;
        _vk = _vulkanContext.VulkanApi;
        _device = _vulkanContext.Device;
        _queueFamilyIndex = queueFamilyIndex;

        // Create graphics command pool + buffers
        CreateCommandPool();
        AllocateCommandBuffers(bufferCount);

        // Create transfer command pool + buffers
        CreateTransferCommandPool(bufferCount);
    }


    private unsafe void CreateCommandPool()
    {
        var createInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _queueFamilyIndex,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        if (_vk.CreateCommandPool(_device, &createInfo, null, out _commandPool) != Result.Success)
        {
            throw new Exception("Failed to create graphics command pool");
        }
    }

    private unsafe void AllocateCommandBuffers(uint count)
    {
        _commandBuffers = new CommandBuffer[count];

        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = count
        };

        fixed (CommandBuffer* buffersPtr = _commandBuffers)
        {
            if (_vk.AllocateCommandBuffers(_device, &allocInfo, buffersPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate graphics command buffers");
            }
        }
    }


    private unsafe void CreateTransferCommandPool(uint bufferCount)
    {
        var transferPoolCreateInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _vulkanContext.TransferQueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        var result = _vk.CreateCommandPool(_device, &transferPoolCreateInfo, null, out _transferCommandPool);
        if (result != Result.Success)
            throw new Exception($"Failed to create transfer command pool: {result}");

        AllocateTransferCommandBuffers(bufferCount);
    }

    private unsafe void AllocateTransferCommandBuffers(uint count)
    {
        var transferCommandBuffers = new CommandBuffer[count];

        var transferAllocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _transferCommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = count
        };

        fixed (CommandBuffer* buffersPtr = transferCommandBuffers)
        {
            var commandResult = _vk.AllocateCommandBuffers(_device, &transferAllocInfo, buffersPtr);
            if (commandResult != Result.Success)
                throw new Exception($"Failed to allocate transfer command buffers: {commandResult}");
        }

        TransferCommandBuffers = transferCommandBuffers;
    }
    

    public unsafe void BeginRecording(CommandBuffer commandBuffer)
    {
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _vk.BeginCommandBuffer(commandBuffer, &beginInfo);
    }

    public void EndRecording(CommandBuffer commandBuffer)
    {
        _vk.EndCommandBuffer(commandBuffer);
    }

    public void ResetCommandBuffer(CommandBuffer commandBuffer)
    {
        _vk.ResetCommandBuffer(commandBuffer, CommandBufferResetFlags.ReleaseResourcesBit);
    }
    

    public unsafe void Dispose()
    {
        // Destroy graphics command pool (also frees graphics command buffers)
        if (_commandPool.Handle != 0)
        {
            _vk.DestroyCommandPool(_device, _commandPool, null);
        }

        // Destroy transfer command pool (also frees transfer command buffers)
        if (_transferCommandPool.Handle != 0)
        {
            _vk.DestroyCommandPool(_device, _transferCommandPool, null);
        }
    }
}
