// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;

namespace NjulfFramework.Rendering.Core;

public class CommandBufferManager : IDisposable
{
    private readonly Device _device;
    private readonly uint _queueFamilyIndex;
    private readonly Vk _vk;
    private readonly VulkanContext _vulkanContext;

    // Graphics command pool + buffers
    private CommandPool _commandPool;

    // Transfer command pool + buffers
    private CommandPool _transferCommandPool;

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

    public CommandPool CommandPool => _commandPool;
    public CommandBuffer[] CommandBuffers { get; private set; } = null!;

    public CommandBuffer[] TransferCommandBuffers { get; private set; }


    public unsafe void Dispose()
    {
        // Destroy graphics command pool (also frees graphics command buffers)
        if (_commandPool.Handle != 0) _vk.DestroyCommandPool(_device, _commandPool, null);

        // Destroy transfer command pool (also frees transfer command buffers)
        if (_transferCommandPool.Handle != 0) _vk.DestroyCommandPool(_device, _transferCommandPool, null);
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
            throw new Exception("Failed to create graphics command pool");
    }

    private unsafe void AllocateCommandBuffers(uint count)
    {
        CommandBuffers = new CommandBuffer[count];

        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = count
        };

        fixed (CommandBuffer* buffersPtr = CommandBuffers)
        {
            if (_vk.AllocateCommandBuffers(_device, &allocInfo, buffersPtr) != Result.Success)
                throw new Exception("Failed to allocate graphics command buffers");
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
    
    /// <summary>
    /// Begins recording a single-time command buffer for immediate submission.
    /// Use this for operations like pipeline barriers that need to complete before rendering.
    /// </summary>
    public unsafe CommandBuffer BeginSingleTimeCommands()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,  // Uses your existing graphics pool
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        if (_vk.AllocateCommandBuffers(_device, &allocInfo, out var cmd) != Result.Success)
            throw new Exception("Failed to allocate single-time command buffer");

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _vk.BeginCommandBuffer(cmd, &beginInfo);
        return cmd;
    }

    /// <summary>
    /// Ends recording, submits, and frees a single-time command buffer.
    /// Blocks until completion, ensuring synchronization.
    /// </summary>
    public unsafe void EndSingleTimeCommands(CommandBuffer cmd)
    {
        _vk.EndCommandBuffer(cmd);

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd
        };

        // Submit to graphics queue and wait for completion
        _vk.QueueSubmit(_vulkanContext.GraphicsQueue, 1, &submitInfo, default);
        _vk.QueueWaitIdle(_vulkanContext.GraphicsQueue);

        // Free the temporary command buffer
        _vk.FreeCommandBuffers(_device, _commandPool, 1, &cmd);
    }
}