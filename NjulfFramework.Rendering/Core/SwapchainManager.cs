// SPDX-License-Identifier: MPL-2.0


using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace NjulfFramework.Rendering.Core;

public class SwapchainManager : IDisposable
{
    private readonly Device _device;
    private readonly Instance _instance;

    private readonly KhrSurface _khrSurface;
    private readonly KhrSwapchain _khrSwapchain;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Queue _presentQueue;
    private readonly SurfaceKHR _surface;
    private readonly Vk _vk;

    private SwapchainKHR _swapchain;

    public SwapchainManager(
        Vk vk,
        Instance instance,
        PhysicalDevice physicalDevice,
        Device device,
        SurfaceKHR surface,
        Queue presentQueue,
        uint width,
        uint height)
    {
        _vk = vk;
        _instance = instance;
        _physicalDevice = physicalDevice;
        _device = device;
        _surface = surface;
        _presentQueue = presentQueue;

        // In constructor or initialization method:
        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface))
            throw new Exception("KHR_surface extension not available");

        if (!_vk.TryGetDeviceExtension(_instance, _device, out _khrSwapchain))
            throw new Exception("KHR_swapchain extension not available");

        CreateSwapchain(width, height);
        CreateImageViews();
    }

    public SwapchainKHR Swapchain => _swapchain;
    public Image[] SwapchainImages { get; private set; } = null!;

    public ImageView[] SwapchainImageViews { get; private set; } = null!;

    public Extent2D SwapchainExtent { get; private set; }

    public Format SwapchainImageFormat { get; private set; }

    public uint SwapchainImageCount => (uint)SwapchainImages.Length;

    public unsafe void Dispose()
    {
        foreach (var imageView in SwapchainImageViews) _vk.DestroyImageView(_device, imageView, null);

        if (_swapchain.Handle != 0) _khrSwapchain!.DestroySwapchain(_device, _swapchain, null);
    }

    private unsafe void CreateSwapchain(uint width, uint height)
    {
        var surfaceCapabilities = QuerySurfaceCapabilities();
        var surfaceFormat = ChooseSurfaceFormat();
        var presentMode = ChoosePresentMode();

        SwapchainExtent = ChooseSwapExtent(surfaceCapabilities, width, height);
        SwapchainImageFormat = surfaceFormat.Format;

        var imageCount = surfaceCapabilities.MinImageCount + 1;
        if (surfaceCapabilities.MaxImageCount > 0 && imageCount > surfaceCapabilities.MaxImageCount)
            imageCount = surfaceCapabilities.MaxImageCount;

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = SwapchainExtent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            PreTransform = surfaceCapabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,
            OldSwapchain = default,
            // For now, assume graphics queue = present queue
            ImageSharingMode = SharingMode.Exclusive,
            QueueFamilyIndexCount = 0,
            PQueueFamilyIndices = null
        };

        if (_khrSwapchain!.CreateSwapchain(_device, &createInfo, null, out _swapchain) != Result.Success)
            throw new Exception("Failed to create swapchain");

        RetrieveSwapchainImages();
    }

    private unsafe void RetrieveSwapchainImages()
    {
        uint imageCount = 0;
        _khrSwapchain!.GetSwapchainImages(_device, _swapchain, &imageCount, null);

        SwapchainImages = new Image[imageCount];
        fixed (Image* imagesPtr = SwapchainImages)
        {
            _khrSwapchain!.GetSwapchainImages(_device, _swapchain, &imageCount, imagesPtr);
        }
    }

    private unsafe void CreateImageViews()
    {
        SwapchainImageViews = new ImageView[SwapchainImages.Length];

        for (var i = 0; i < SwapchainImages.Length; i++)
        {
            var createInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = SwapchainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = SwapchainImageFormat,
                Components = new ComponentMapping
                {
                    R = ComponentSwizzle.Identity,
                    G = ComponentSwizzle.Identity,
                    B = ComponentSwizzle.Identity,
                    A = ComponentSwizzle.Identity
                },
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            if (_vk.CreateImageView(_device, &createInfo, null, out SwapchainImageViews[i]) != Result.Success)
                throw new Exception($"Failed to create image view {i}");
        }
    }

    private SurfaceCapabilitiesKHR QuerySurfaceCapabilities()
    {
        _khrSurface!.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var capabilities);
        return capabilities;
    }

    private unsafe SurfaceFormatKHR ChooseSurfaceFormat()
    {
        uint formatCount = 0;
        _khrSurface!.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, &formatCount, null);

        var formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* formatsPtr = formats)
        {
            _khrSurface!.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, &formatCount, formatsPtr);
        }

        // Prefer SRGB if available
        foreach (var format in formats)
            if (format.Format == Format.B8G8R8A8Srgb && format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return format;

        return formats[0];
    }

    private unsafe PresentModeKHR ChoosePresentMode()
    {
        uint modeCount = 0;
        _khrSurface!.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, &modeCount, null);

        var modes = new PresentModeKHR[modeCount];
        fixed (PresentModeKHR* modesPtr = modes)
        {
            _khrSurface!.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, &modeCount, modesPtr);
        }

        // Prefer Mailbox (triple buffering), fallback to FIFO
        foreach (var mode in modes)
            if (mode == PresentModeKHR.MailboxKhr)
                return mode;

        return PresentModeKHR.FifoKhr;
    }

    private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities, uint width, uint height)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue) return capabilities.CurrentExtent;

        return new Extent2D
        {
            Width = Math.Max(capabilities.MinImageExtent.Width,
                Math.Min(capabilities.MaxImageExtent.Width, width)),
            Height = Math.Max(capabilities.MinImageExtent.Height,
                Math.Min(capabilities.MaxImageExtent.Height, height))
        };
    }
}