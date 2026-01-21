// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;

namespace Njulf_Framework.Rendering.Resources;

public class RenderPassManager : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private RenderPass _renderPass;

    public RenderPass RenderPass => _renderPass;

    public RenderPassManager(Vk vk, Device device, Format colorFormat)
    {
        _vk = vk;
        _device = device;

        CreateRenderPass(colorFormat);
    }

    private unsafe void CreateRenderPass(Format colorFormat)
    {
        // Color attachment description
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        // Color attachment reference
        var colorAttachmentRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        // Subpass
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef
        };

        // Subpass dependency (synchronization)
        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DependencyFlags = 0
        };

        // Render pass create info
        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        if (_vk.CreateRenderPass(_device, &renderPassInfo, null, out _renderPass) != Result.Success)
        {
            throw new Exception("Failed to create render pass");
        }
    }

    public unsafe void Dispose()
    {
        if (_renderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _renderPass, null);
        }
    }
}