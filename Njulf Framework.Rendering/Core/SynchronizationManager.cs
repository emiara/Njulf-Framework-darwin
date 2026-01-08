using Silk.NET.Vulkan;
using System;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Njulf_Framework.Rendering.Core;

public class SynchronizationManager : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;

    private Semaphore[] _imageAvailableSemaphores = null!;
    private Semaphore[] _renderFinishedSemaphores = null!;
    private Fence[] _inFlightFences = null!;

    public Semaphore[] ImageAvailableSemaphores => _imageAvailableSemaphores;
    public Semaphore[] RenderFinishedSemaphores => _renderFinishedSemaphores;
    public Fence[] InFlightFences => _inFlightFences;

    public SynchronizationManager(Vk vk, Device device, uint maxFramesInFlight = 2)
    {
        _vk = vk;
        _device = device;

        CreateSemaphoresAndFences(maxFramesInFlight);
    }

    private unsafe void CreateSemaphoresAndFences(uint maxFramesInFlight)
    {
        _imageAvailableSemaphores = new Semaphore[maxFramesInFlight];
        _renderFinishedSemaphores = new Semaphore[maxFramesInFlight];
        _inFlightFences = new Fence[maxFramesInFlight];

        var semaphoreCreateInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        var fenceCreateInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit // Start in signaled state
        };

        for (uint i = 0; i < maxFramesInFlight; i++)
        {
            if (_vk.CreateSemaphore(_device, &semaphoreCreateInfo, null, out _imageAvailableSemaphores[i]) != Result.Success)
            {
                throw new Exception($"Failed to create image available semaphore {i}");
            }

            if (_vk.CreateSemaphore(_device, &semaphoreCreateInfo, null, out _renderFinishedSemaphores[i]) != Result.Success)
            {
                throw new Exception($"Failed to create render finished semaphore {i}");
            }

            if (_vk.CreateFence(_device, &fenceCreateInfo, null, out _inFlightFences[i]) != Result.Success)
            {
                throw new Exception($"Failed to create in-flight fence {i}");
            }
        }
    }

    public unsafe void WaitForFence(Fence fence, ulong timeout = ulong.MaxValue)
    {
        _vk.WaitForFences(_device, 1, &fence, true, timeout);
    }

    public unsafe void ResetFence(Fence fence)
    {
        _vk.ResetFences(_device, 1, &fence);
    }

    public unsafe void Dispose()
    {
        foreach (var semaphore in _imageAvailableSemaphores)
        {
            if (semaphore.Handle != 0)
            {
                _vk.DestroySemaphore(_device, semaphore, null);
            }
        }

        foreach (var semaphore in _renderFinishedSemaphores)
        {
            if (semaphore.Handle != 0)
            {
                _vk.DestroySemaphore(_device, semaphore, null);
            }
        }

        foreach (var fence in _inFlightFences)
        {
            if (fence.Handle != 0)
            {
                _vk.DestroyFence(_device, fence, null);
            }
        }
    }
}