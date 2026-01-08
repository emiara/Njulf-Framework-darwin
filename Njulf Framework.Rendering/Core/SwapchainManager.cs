using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Core.Native;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Njulf_Framework.Rendering.Core;

public class SwapchainManager : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Instance _instance;
    private readonly SurfaceKHR _surface;
    private readonly Queue _presentQueue;
    
    private readonly KhrSurface _khrSurface;
    private readonly KhrSwapchain _khrSwapchain;

    private SwapchainKHR _swapchain;
    private Image[] _swapchainImages = null!;
    private ImageView[] _swapchainImageViews = null!;
    private Extent2D _swapchainExtent;
    private Format _swapchainImageFormat;

    public SwapchainKHR Swapchain => _swapchain;
    public Image[] SwapchainImages => _swapchainImages;
    public ImageView[] SwapchainImageViews => _swapchainImageViews;
    public Extent2D SwapchainExtent => _swapchainExtent;
    public Format SwapchainImageFormat => _swapchainImageFormat;

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

    private unsafe void CreateSwapchain(uint width, uint height)
    {
        var surfaceCapabilities = QuerySurfaceCapabilities();
        var surfaceFormat = ChooseSurfaceFormat();
        var presentMode = ChoosePresentMode();

        _swapchainExtent = ChooseSwapExtent(surfaceCapabilities, width, height);
        _swapchainImageFormat = surfaceFormat.Format;

        uint imageCount = surfaceCapabilities.MinImageCount + 1;
        if (surfaceCapabilities.MaxImageCount > 0 && imageCount > surfaceCapabilities.MaxImageCount)
        {
            imageCount = surfaceCapabilities.MaxImageCount;
        }

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = _swapchainExtent,
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
        {
            throw new Exception("Failed to create swapchain");
        }

        RetrieveSwapchainImages();
    }

    private unsafe void RetrieveSwapchainImages()
    {
        uint imageCount = 0;
        _khrSwapchain!.GetSwapchainImages(_device, _swapchain, &imageCount, null);

        _swapchainImages = new Image[imageCount];
        fixed (Image* imagesPtr = _swapchainImages)
        {
            _khrSwapchain!.GetSwapchainImages(_device, _swapchain, &imageCount, imagesPtr);
        }
    }

    private unsafe void CreateImageViews()
    {
        _swapchainImageViews = new ImageView[_swapchainImages.Length];

        for (int i = 0; i < _swapchainImages.Length; i++)
        {
            var createInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _swapchainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = _swapchainImageFormat,
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

            if (_vk.CreateImageView(_device, &createInfo, null, out _swapchainImageViews[i]) != Result.Success)
            {
                throw new Exception($"Failed to create image view {i}");
            }
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
        {
            if (format.Format == Format.B8G8R8A8Srgb && format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return format;
            }
        }

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
        {
            if (mode == PresentModeKHR.MailboxKhr)
            {
                return mode;
            }
        }

        return PresentModeKHR.FifoKhr;
    }

    private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities, uint width, uint height)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }

        return new Extent2D
        {
            Width = Math.Max(capabilities.MinImageExtent.Width,
                Math.Min(capabilities.MaxImageExtent.Width, width)),
            Height = Math.Max(capabilities.MinImageExtent.Height,
                Math.Min(capabilities.MaxImageExtent.Height, height))
        };
    }

    public unsafe void Dispose()
    {
        foreach (var imageView in _swapchainImageViews)
        {
            _vk.DestroyImageView(_device, imageView, null);
        }

        if (_swapchain.Handle != 0)
        {
            _khrSwapchain!.DestroySwapchain(_device, _swapchain, null);
        }
    }
    
}