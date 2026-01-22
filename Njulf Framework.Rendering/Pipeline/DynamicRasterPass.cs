// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using System.Numerics;
using Njulf_Framework.Rendering.Data;

namespace Njulf_Framework.Rendering.Pipeline;

/// <summary>
/// Render pass that uses Vulkan 1.3 dynamic rendering for rasterization.
/// Attachments are specified inline in vkCmdBeginRendering instead of 
/// using VkRenderPass and VkFramebuffer objects.
/// </summary>
public class DynamicRasterPass : RenderGraphPass
{
    private readonly Vk _vk;
    private readonly Device _device;
    
    private ImageView _colorAttachment;
    private ImageView _depthAttachment;
    private Extent2D _extent;
    private GraphicsPipeline _pipeline;
    private Vector4 _clearColor;

    /// <summary>
    /// Initialize a dynamic raster pass with color and depth attachments.
    /// </summary>
    /// <param name="vk">Vulkan API instance</param>
    /// <param name="device">Vulkan device</param>
    /// <param name="colorAttachment">Color attachment image view</param>
    /// <param name="depthAttachment">Depth attachment image view (optional)</param>
    /// <param name="pipeline">Graphics pipeline for rendering</param>
    /// <param name="clearColor">Color to clear attachment with</param>
    public DynamicRasterPass(
        Vk vk,
        Device device,
        ImageView colorAttachment,
        ImageView depthAttachment,
        GraphicsPipeline pipeline,
        Vector4 clearColor)
    {
        _vk = vk;
        _device = device;
        _colorAttachment = colorAttachment;
        _depthAttachment = depthAttachment;
        _pipeline = pipeline;
        _clearColor = clearColor;
    }

    /// <summary>
    /// Set the render target extent (width, height).
    /// </summary>
    public void SetExtent(Extent2D extent) => _extent = extent;

    /// <summary>
    /// Execute the raster pass using dynamic rendering.
    /// </summary>
    public override unsafe void Execute(CommandBuffer cmd, RenderGraphContext ctx)
    {
        _extent = new Extent2D(ctx.Width, ctx.Height);

        // Configure color attachment for dynamic rendering
        var colorAttachmentInfo = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = _colorAttachment,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = new ClearValue 
            { 
                Color = new ClearColorValue(_clearColor.X, _clearColor.Y, _clearColor.Z, _clearColor.W) 
            }
        };

        // Configure depth attachment for dynamic rendering
        RenderingAttachmentInfo depthAttachmentInfo = default;
        RenderingAttachmentInfo* pDepthAttachment = null;

        if (_depthAttachment.Handle != 0)
        {
            depthAttachmentInfo = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = _depthAttachment,
                ImageLayout = ImageLayout.DepthAttachmentOptimal,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.DontCare,
                ClearValue = new ClearValue 
                { 
                    DepthStencil = new ClearDepthStencilValue(1.0f, 0) 
                }
            };
            pDepthAttachment = &depthAttachmentInfo;
        }

        // Create rendering info with inline attachments (NO VkRenderPass)
        var renderingInfo = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D 
            { 
                Offset = new Offset2D { X = 0, Y = 0 },
                Extent = _extent 
            },
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentInfo,
            PDepthAttachment = pDepthAttachment
        };

        // Begin dynamic rendering
        _vk.CmdBeginRendering(cmd, &renderingInfo);

        try
        {
            // Bind pipeline
            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline.Pipeline);

            // Set viewport and scissor (if needed for dynamic state)
            var viewport = new Viewport
            {
                X = 0,
                Y = 0,
                Width = _extent.Width,
                Height = _extent.Height,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };
            _vk.CmdSetViewport(cmd, 0, 1, &viewport);

            var scissor = new Rect2D
            {
                Offset = new Offset2D { X = 0, Y = 0 },
                Extent = _extent
            };
            _vk.CmdSetScissor(cmd, 0, 1, &scissor);

            // Draw all visible objects from RenderingData
            foreach (var obj in ctx.VisibleObjects)
            {
                DrawObject(cmd, obj);
            }
        }
        finally
        {
            // End dynamic rendering
            _vk.CmdEndRendering(cmd);
        }
    }

    /// <summary>
    /// Draw a single render object from RenderingData module.
    /// Implement bindless indexing and push constants here.
    /// </summary>
    private unsafe void DrawObject(CommandBuffer cmd, RenderingData.RenderObject obj)
    {
        // This is a placeholder implementation.
        // In a full implementation, this would:
        // - Bind descriptor sets for bindless buffers/textures
        // - Push object-specific data via push constants
        // - Issue draw calls with vertex/index buffers from obj.Mesh
        
        if (obj?.Mesh?.Indices != null && obj.Mesh.Indices.Length > 0)
        {
            _vk.CmdDrawIndexed(cmd, (uint)obj.Mesh.Indices.Length, 1, 0, 0, 0);
        }
    }
}
