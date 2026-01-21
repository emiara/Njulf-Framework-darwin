// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;

namespace Njulf_Framework.Rendering.Resources;

public class FramebufferManager : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private Framebuffer[] _framebuffers = null!;

    public Framebuffer[] Framebuffers => _framebuffers;

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

    private unsafe void CreateFramebuffers(RenderPass renderPass, ImageView[] imageViews, Extent2D extent)
    {
        _framebuffers = new Framebuffer[imageViews.Length];

        for (int i = 0; i < imageViews.Length; i++)
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

            if (_vk.CreateFramebuffer(_device, &framebufferInfo, null, out _framebuffers[i]) != Result.Success)
            {
                throw new Exception($"Failed to create framebuffer {i}");
            }
        }

        
    }
    
    public unsafe void Dispose()
    {
        foreach (var framebuffer in _framebuffers)
        {
            if (framebuffer.Handle != 0)
            {
                _vk.DestroyFramebuffer(_device, framebuffer, null);
            }
        }
    }
}