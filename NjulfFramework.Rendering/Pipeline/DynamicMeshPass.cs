// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace NjulfFramework.Rendering.Pipeline;

public class DynamicMeshPass : RenderGraphPass
{
    private readonly Vector4 _clearColor;
    private readonly ExtMeshShader _meshShader;
    private readonly MeshPipeline _pipeline;
    private readonly Vk _vk;

    public DynamicMeshPass(
        Vk vk,
        ExtMeshShader meshShader,
        MeshPipeline pipeline,
        Vector4 clearColor)
    {
        _vk = vk;
        _meshShader = meshShader;
        _pipeline = pipeline;
        _clearColor = clearColor;
    }

    public override unsafe void Execute(CommandBuffer cmd, RenderGraphContext ctx)
    {
        var extent = new Extent2D(ctx.Width, ctx.Height);

        var colorAttachmentInfo = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = ctx.ColorAttachmentView,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = new ClearValue
            {
                Color = new ClearColorValue(_clearColor.X, _clearColor.Y, _clearColor.Z, _clearColor.W)
            }
        };

        RenderingAttachmentInfo depthAttachmentInfo = default;
        RenderingAttachmentInfo* pDepthAttachment = null;
        if (ctx.DepthAttachmentView.Handle != 0)
        {
            depthAttachmentInfo = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = ctx.DepthAttachmentView,
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

        var renderingInfo = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D
            {
                Offset = new Offset2D { X = 0, Y = 0 },
                Extent = extent
            },
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentInfo,
            PDepthAttachment = pDepthAttachment
        };

        _vk.CmdBeginRendering(cmd, &renderingInfo);

        try
        {
            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline.Pipeline);

            var descriptorSets = stackalloc DescriptorSet[]
            {
                ctx.BindlessHeap.BufferSet,
                ctx.BindlessHeap.TextureSet
            };
            _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _pipeline.PipelineLayout,
                0, 2, descriptorSets, 0, null);

            var viewport = new Viewport
            {
                X = 0,
                Y = 0,
                Width = extent.Width,
                Height = extent.Height,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };
            _vk.CmdSetViewport(cmd, 0, 1, &viewport);

            var scissor = new Rect2D
            {
                Offset = new Offset2D { X = 0, Y = 0 },
                Extent = extent
            };
            _vk.CmdSetScissor(cmd, 0, 1, &scissor);

            if (ctx.MeshManager == null || ctx.SceneDataBuilder == null || ctx.MeshletDrawCount == 0)
                return;

            var pushConstants = new Data.RenderingData.PushConstants
            {
                View = ctx.View,
                Projection = ctx.Projection,
                ScreenWidth = ctx.Width,
                ScreenHeight = ctx.Height,
                DebugMeshlets = 0,
                LightCount = ctx.LightCount,
                LightBufferIndex = ctx.LightBufferIndex,
                TiledLightHeaderBufferIndex = ctx.TiledLightHeaderBufferIndex,
                TiledLightIndicesBufferIndex = ctx.TiledLightIndicesBufferIndex,
                InstanceBufferIndex = ctx.InstanceBufferIndex,
                MeshletDrawBufferIndex = ctx.MeshletDrawBufferIndex,
                MeshletDrawCount = ctx.MeshletDrawCount,
                FrameIndex = ctx.FrameIndex,
                MaxFramesInFlight = 2
            };

            _vk.CmdPushConstants(cmd, _pipeline.PipelineLayout,
                ShaderStageFlags.MeshBitExt | ShaderStageFlags.FragmentBit | ShaderStageFlags.TaskBitExt,
                0, (uint)Marshal.SizeOf<Data.RenderingData.PushConstants>(), &pushConstants);

            // One task workgroup per (instance, meshlet) draw entry.
            // (Switch to CmdDrawMeshTasksIndirectCount later when culling moves fully GPU-side.)
            _meshShader.CmdDrawMeshTask(cmd, ctx.MeshletDrawCount, 1, 1);
        }
        finally
        {
            _vk.CmdEndRendering(cmd);
        }
    }
}