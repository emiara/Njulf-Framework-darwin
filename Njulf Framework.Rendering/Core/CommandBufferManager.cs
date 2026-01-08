using Silk.NET.Vulkan;

using System;

namespace Njulf_Framework.Rendering.Core;

public class CommandBufferManager : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly uint _queueFamilyIndex;

    private CommandPool _commandPool;
    private CommandBuffer[] _commandBuffers = null!;

    public CommandPool CommandPool => _commandPool;
    public CommandBuffer[] CommandBuffers => _commandBuffers;

    public CommandBufferManager(Vk vk, Device device, uint queueFamilyIndex, uint bufferCount = 2)
    {
        _vk = vk;
        _device = device;
        _queueFamilyIndex = queueFamilyIndex;

        CreateCommandPool();
        AllocateCommandBuffers(bufferCount);
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
            throw new Exception("Failed to create command pool");
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
                throw new Exception("Failed to allocate command buffers");
            }
        }
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
        if (_commandPool.Handle != 0)
        {
            _vk.DestroyCommandPool(_device, _commandPool, null);
        }
    }
}