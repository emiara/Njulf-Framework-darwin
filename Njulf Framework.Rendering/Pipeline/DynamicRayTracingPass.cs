// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Njulf_Framework.Rendering.Data;

namespace Njulf_Framework.Rendering.Pipeline;

/// <summary>
/// Render pass that uses Vulkan 1.3 dynamic rendering for ray tracing.
/// Ray tracing operations also do not require VkRenderPass objects.
/// References RenderingData types from the existing data module.
/// </summary>
public class DynamicRayTracingPass : RenderGraphPass
{
    private readonly Vk _vk;
    private readonly Device _device;
    
    private ImageView _outputImage;
    private Extent2D _extent;
    // Note: RayTracingPipeline will be implemented in Task 4.3
    // private RayTracingPipeline _rtPipeline;

    /// <summary>
    /// Initialize a dynamic ray tracing pass.
    /// </summary>
    /// <param name="vk">Vulkan API instance</param>
    /// <param name="device">Vulkan device</param>
    /// <param name="outputImage">Output image view for ray tracing results</param>
    public DynamicRayTracingPass(
        Vk vk,
        Device device,
        ImageView outputImage)
    {
        _vk = vk;
        _device = device;
        _outputImage = outputImage;
    }

    /// <summary>
    /// Set the ray tracing output extent.
    /// </summary>
    public void SetExtent(Extent2D extent) => _extent = extent;

    /// <summary>
    /// Execute the ray tracing pass.
    /// Uses visible objects from RenderingData module.
    /// Note: Full implementation requires RayTracingPipeline (Task 4.3).
    /// </summary>
    public override unsafe void Execute(CommandBuffer cmd, RenderGraphContext ctx)
    {
        _extent = new Extent2D(ctx.Width, ctx.Height);

        // Ray tracing does NOT need dynamic rendering begin/end,
        // but we can still use it for consistency with image transitions
        
        if (!ctx.TLAS.HasValue || ctx.TLAS.Value.Handle == 0)
        {
            Console.WriteLine("Warning: TLAS not available for ray tracing pass");
            return;
        }

        // TODO: Implement ray tracing dispatch when RayTracingPipeline is ready
        // This will use:
        // - ctx.VisibleObjects (RenderingData.RenderObject list)
        // - ctx.TLAS (acceleration structure)
        // - ctx.ViewProjection and ctx.CameraPosition for ray setup
        // Implementation details in Task 4.3
    }
}
