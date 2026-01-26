// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using System.Numerics;
using Njulf_Framework.Rendering.Data;

namespace Njulf_Framework.Rendering.Pipeline;

public class DynamicMeshPass : RenderGraphPass
{
    private readonly Vk _vk;
    private readonly ExtMeshShader _meshShader;
    private readonly MeshPipeline _pipeline;
    private readonly Vector4 _clearColor;

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
                ctx.BindlessHeap.TextureSet,
                ctx.MeshBuffersSet
            };
            _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _pipeline.PipelineLayout,
                0, 3, descriptorSets, 0, null);

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

            foreach (var obj in ctx.VisibleObjects)
            {
                if (obj?.Mesh == null || ctx.MeshManager == null)
                    continue;

                var meshEntry = ctx.MeshManager.GetOrCreateMeshGpu(obj.Mesh);

                var pushConstants = new Data.RenderingData.PushConstants
                {
                    Model = obj.Transform,
                    View = ctx.View,
                    Projection = ctx.Projection,
                    MaterialIndex = 0,
                    VertexOffset = meshEntry.VertexOffset,
                    IndexOffset = meshEntry.IndexOffset,
                    IndexCount = meshEntry.IndexCount,
                    VertexCount = meshEntry.VertexCount,
                    MeshletOffset = meshEntry.MeshletOffset,
                    MeshletCount = meshEntry.MeshletCount,
                    MeshBoundsRadius = meshEntry.BoundsRadius,
                    ScreenWidth = ctx.Width,
                    ScreenHeight = ctx.Height,
                    DebugMeshlets = 0,
                    LightCount = ctx.LightCount,
                    LightBufferIndex = ctx.LightBufferIndex,
                    TiledLightHeaderBufferIndex = ctx.TiledLightHeaderBufferIndex,
                    TiledLightIndicesBufferIndex = ctx.TiledLightIndicesBufferIndex,
                    Padding = 0
                };

                _vk.CmdPushConstants(cmd, _pipeline.PipelineLayout,
                    ShaderStageFlags.MeshBitExt | ShaderStageFlags.FragmentBit | ShaderStageFlags.TaskBitExt,
                    0, (uint)sizeof(Data.RenderingData.PushConstants), &pushConstants);

                if (meshEntry.MeshletCount == 0)
                    continue;

                _meshShader.CmdDrawMeshTask(cmd, 1, 1, 1);
            }
        }
        finally
        {
            _vk.CmdEndRendering(cmd);
        }
    }
}
