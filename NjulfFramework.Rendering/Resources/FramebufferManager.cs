// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;

namespace NjulfFramework.Rendering.Resources;

public class FramebufferManager : IDisposable
{
    private readonly Device _device;
    private readonly Vk _vk;

    public FramebufferManager(
        Vk vk,
        Device device,
        RenderPass renderPass,
        ImageView[] imageViews,
        Extent2D extent)
    {
        _vk = vk;
        _device = device;

        CreateFramebuffers(renderPass, imageViews, extent);
    }

    public Framebuffer[] Framebuffers { get; private set; } = null!;

    public unsafe void Dispose()
    {
        foreach (var framebuffer in Framebuffers)
            if (framebuffer.Handle != 0)
                _vk.DestroyFramebuffer(_device, framebuffer, null);
    }

    private unsafe void CreateFramebuffers(RenderPass renderPass, ImageView[] imageViews, Extent2D extent)
    {
        Framebuffers = new Framebuffer[imageViews.Length];

        for (var i = 0; i < imageViews.Length; i++)
        {
            var attachments = stackalloc ImageView[] { imageViews[i] };

            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = renderPass,
                AttachmentCount = 1,
                PAttachments = attachments,
                Width = extent.Width,
                Height = extent.Height,
                Layers = 1
            };

            if (_vk.CreateFramebuffer(_device, &framebufferInfo, null, out Framebuffers[i]) != Result.Success)
                throw new Exception($"Failed to create framebuffer {i}");
        }
    }
}