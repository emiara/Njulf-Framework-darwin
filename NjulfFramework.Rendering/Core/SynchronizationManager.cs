// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace NjulfFramework.Rendering.Core;

public class SynchronizationManager : IDisposable
{
    private readonly Device _device;
    private readonly Vk _vk;

    // Per-frame semaphores (for acquiring images)

    // Per-frame fences

    // Per-image semaphores (for presenting images)

    public SynchronizationManager(Vk vk, Device device, uint swapchainImageCount, uint maxFramesInFlight = 2)
    {
        _vk = vk;
        _device = device;

        CreateSemaphoresAndFences(swapchainImageCount, maxFramesInFlight);
    }

    public Semaphore[] ImageAvailableSemaphores { get; private set; } = null!;

    public Semaphore[] TransferFinishedSemaphores { get; private set; } = null!;

    public Semaphore[] RenderFinishedSemaphores { get; private set; } = null!;

    public Fence[] InFlightFences { get; private set; } = null!;

    public unsafe void Dispose()
    {
        foreach (var semaphore in ImageAvailableSemaphores)
            if (semaphore.Handle != 0)
                _vk.DestroySemaphore(_device, semaphore, null);

        foreach (var semaphore in TransferFinishedSemaphores)
            if (semaphore.Handle != 0)
                _vk.DestroySemaphore(_device, semaphore, null);

        foreach (var semaphore in RenderFinishedSemaphores)
            if (semaphore.Handle != 0)
                _vk.DestroySemaphore(_device, semaphore, null);

        foreach (var fence in InFlightFences)
            if (fence.Handle != 0)
                _vk.DestroyFence(_device, fence, null);
    }

    private unsafe void CreateSemaphoresAndFences(uint swapchainImageCount, uint maxFramesInFlight)
    {
        // ImageAvailableSemaphores: per-frame (acquired in Acquire, consumed in Submit)
        ImageAvailableSemaphores = new Semaphore[maxFramesInFlight];

        TransferFinishedSemaphores = new Semaphore[maxFramesInFlight];

        // RenderFinishedSemaphores: per-image (signaled in Submit, consumed in Present)
        RenderFinishedSemaphores = new Semaphore[swapchainImageCount];

        // InFlightFences: per-frame
        InFlightFences = new Fence[maxFramesInFlight];

        var semaphoreCreateInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        var fenceCreateInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit // Start in signaled state
        };

        // Create acquire semaphores (per-frame)
        for (uint i = 0; i < maxFramesInFlight; i++)
            if (_vk.CreateSemaphore(_device, &semaphoreCreateInfo, null, out ImageAvailableSemaphores[i]) !=
                Result.Success)
                throw new Exception($"Failed to create image available semaphore {i}");

        for (uint i = 0; i < maxFramesInFlight; i++)
            if (_vk.CreateSemaphore(_device, &semaphoreCreateInfo, null, out TransferFinishedSemaphores[i]) !=
                Result.Success)
                throw new Exception($"Failed to create transfer finished semaphore {i}");

        // Create render finished semaphores (per-image)
        for (uint i = 0; i < swapchainImageCount; i++)
            if (_vk.CreateSemaphore(_device, &semaphoreCreateInfo, null, out RenderFinishedSemaphores[i]) !=
                Result.Success)
                throw new Exception($"Failed to create render finished semaphore {i}");

        // Create frame fences
        for (uint i = 0; i < maxFramesInFlight; i++)
            if (_vk.CreateFence(_device, &fenceCreateInfo, null, out InFlightFences[i]) != Result.Success)
                throw new Exception($"Failed to create in-flight fence {i}");

        Console.WriteLine(
            $"✓ Created {maxFramesInFlight} acquire semaphores (per frame) + {maxFramesInFlight} transfer finished semaphores (per frame) + {swapchainImageCount} render finished semaphores (per image) + {maxFramesInFlight} fences (per frame)");
    }

    public unsafe void WaitForFence(Fence fence, ulong timeout = ulong.MaxValue)
    {
        _vk.WaitForFences(_device, 1, &fence, true, timeout);
    }

    public unsafe void ResetFence(Fence fence)
    {
        _vk.ResetFences(_device, 1, &fence);
    }
}