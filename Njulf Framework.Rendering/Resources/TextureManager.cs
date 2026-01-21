using Vma;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;

namespace Njulf_Framework.Rendering.Resources;

public sealed unsafe class TextureManager : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly Allocator* _allocator;

    private sealed class ImageEntry
    {
        public Image Handle;
        public ImageView View;
        public Allocation* Allocation; 
        public uint Width;
        public uint Height;
        public Format Format;
    }

    private readonly Dictionary<uint, ImageEntry> _images = new();
    private uint _nextId = 1;

    public TextureManager(Vk vk, Device device, Allocator* allocator)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
    }

    /// <summary>
    /// Allocate a new GPU image/texture.
    /// Do NOT use Mapped flag for images — that's only for buffers.
    /// </summary>
    public Handles.TextureHandle AllocateTexture(
    uint width,
    uint height,
    Format format,
    ImageUsageFlags usage,
    ImageTiling tiling = ImageTiling.Optimal,
    MemoryUsage memUsage = MemoryUsage.AutoPreferDevice)
{
    if (width == 0 || height == 0)
        throw new ArgumentException("Image dimensions must be > 0");

    var imageInfo = new ImageCreateInfo
    {
        SType = StructureType.ImageCreateInfo,
        ImageType = ImageType.Type2D,
        Format = format,
        Extent = new Extent3D { Width = width, Height = height, Depth = 1 },
        MipLevels = 1,
        ArrayLayers = 1,
        Samples = SampleCountFlags.Count1Bit,
        Tiling = tiling,
        Usage = usage,
        SharingMode = SharingMode.Exclusive,
        InitialLayout = ImageLayout.Undefined
    };

    var allocInfo = new AllocationCreateInfo
    {
        Usage = memUsage
    };

    ImageCreateInfo* pImageInfo = &imageInfo;
    AllocationCreateInfo* pAllocInfo = &allocInfo;

    // Declare output variables
    Image image;
    Vma.Allocation* allocation;
    AllocationInfo allocationInfo;

    // Create image
    var result = Apis.CreateImage(
        _allocator,
        pImageInfo,
        pAllocInfo,
        &image,
        &allocation,
        &allocationInfo);

    if (result != Result.Success)
    {
        throw new InvalidOperationException(
            $"Failed to allocate image {width}x{height} (format={format}): {result}");
    }

    // Create image view
    var aspectMask = GetAspectMask(format);

    var viewInfo = new ImageViewCreateInfo
    {
        SType = StructureType.ImageViewCreateInfo,
        Image = image,
        ViewType = ImageViewType.Type2D,
        Format = format,
        Components = new ComponentMapping
        {
            R = ComponentSwizzle.Identity,
            G = ComponentSwizzle.Identity,
            B = ComponentSwizzle.Identity,
            A = ComponentSwizzle.Identity
        },
        SubresourceRange = new ImageSubresourceRange
        {
            AspectMask = aspectMask,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1
        }
    };

    ImageViewCreateInfo* pViewInfo = &viewInfo;
    result = _vk.CreateImageView(_device, pViewInfo, null, out var view);
    
    if (result != Result.Success)
    {
        Apis.DestroyImage(_allocator, image, allocation);
        throw new InvalidOperationException(
            $"Failed to create image view: {result}");
    }

    var id = _nextId++;
    _images[id] = new ImageEntry
    {
        Handle = image,
        View = view,
        Allocation = allocation,  // Store pointer, not struct
        Width = width,
        Height = height,
        Format = format
    };

    return new Handles.TextureHandle(id, 1);
}


    /// <summary>
    /// Allocate a texture with initial data from a staging buffer.
    /// The caller is responsible for the staging buffer and copy commands.
    /// </summary>
    public Handles.TextureHandle AllocateTextureWithData(
        uint width,
        uint height,
        Format format,
        ImageUsageFlags usage,
        ReadOnlySpan<byte> initialData,
        ImageTiling tiling = ImageTiling.Optimal)
    {
        // Allocate the image itself
        var handle = AllocateTexture(width, height, format, usage | ImageUsageFlags.TransferDstBit, tiling);

        // Note: Actual data upload happens via staging buffer + copy commands
        // This method just allocates; the caller handles staging and copying

        return handle;
    }

    public Image GetImage(Handles.TextureHandle handle)
    {
        if (!_images.TryGetValue(handle.Index, out var entry))
            throw new InvalidOperationException($"Texture handle {handle} not found");
        return entry.Handle;
    }

    public ImageView GetImageView(Handles.TextureHandle handle)
    {
        if (!_images.TryGetValue(handle.Index, out var entry))
            throw new InvalidOperationException($"Texture handle {handle} not found");
        return entry.View;
    }

    public (uint Width, uint Height) GetTextureSize(Handles.TextureHandle handle)
    {
        if (!_images.TryGetValue(handle.Index, out var entry))
            throw new InvalidOperationException($"Texture handle {handle} not found");
        return (entry.Width, entry.Height);
    }

    public Format GetTextureFormat(Handles.TextureHandle handle)
    {
        if (!_images.TryGetValue(handle.Index, out var entry))
            throw new InvalidOperationException($"Texture handle {handle} not found");
        return entry.Format;
    }

    /// <summary>
    /// Free a texture handle. The handle becomes invalid after this.
    /// </summary>
    public void FreeTexture(Handles.TextureHandle handle)
    {
        if (!_images.Remove(handle.Index, out var entry))
            return;

        _vk.DestroyImageView(_device, entry.View, null);
        Apis.DestroyImage(_allocator, entry.Handle, entry.Allocation);
    }

    /// <summary>
    /// Determine the correct aspect mask for a given image format.
    /// </summary>
    private static ImageAspectFlags GetAspectMask(Format format)
    {
        return format switch
        {
            // Depth-only formats
            Format.D16Unorm or
            Format.D32Sfloat or
            Format.X8D24UnormPack32 =>
                ImageAspectFlags.DepthBit,

            // Stencil-only formats
            Format.S8Uint =>
                ImageAspectFlags.StencilBit,

            // Depth+Stencil formats
            Format.D16UnormS8Uint or
            Format.D24UnormS8Uint or
            Format.D32SfloatS8Uint =>
                ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,

            // Color formats (default)
            _ =>
                ImageAspectFlags.ColorBit
        };
    }

    public void Dispose()
    {
        foreach (var (_, entry) in _images)
        {
            _vk.DestroyImageView(_device, entry.View, null);
            Apis.DestroyImage(_allocator, entry.Handle, entry.Allocation);
        }
        _images.Clear();
    }
}