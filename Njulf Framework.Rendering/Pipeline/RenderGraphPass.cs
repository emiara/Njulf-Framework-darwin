// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;

namespace Njulf_Framework.Rendering.Pipeline;

/// <summary>
/// Base class for render graph passes using dynamic rendering (Vulkan 1.3).
/// Each pass represents a rendering operation that operates on images inline
/// without requiring VkRenderPass or VkFramebuffer objects.
/// </summary>
public abstract class RenderGraphPass
{
    /// <summary>
    /// Name of the render pass for debugging and profiling.
    /// </summary>
    public string Name { get; set; } = "RenderPass";

    /// <summary>
    /// Execute the render pass with the given command buffer and context.
    /// </summary>
    /// <param name="cmd">The command buffer to record commands into</param>
    /// <param name="ctx">The render graph context containing frame data</param>
    public abstract void Execute(CommandBuffer cmd, RenderGraphContext ctx);
}