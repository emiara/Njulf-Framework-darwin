// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;

namespace Njulf_Framework.Rendering.Pipeline;

/// <summary>
/// Simple render graph that executes a sequence of render passes.
/// Each pass can read/write images in a dependency-free manner.
/// For a production system, this would include explicit dependency tracking.
/// </summary>
public class RenderGraph
{
    private readonly List<RenderGraphPass> _passes = new();
    private readonly string _name;

    public RenderGraph(string name = "RenderGraph")
    {
        _name = name;
    }

    /// <summary>
    /// Add a render pass to the graph.
    /// Passes execute in the order they are added.
    /// </summary>
    public void AddPass(RenderGraphPass pass)
    {
        if (pass == null)
            throw new ArgumentNullException(nameof(pass));
        _passes.Add(pass);
    }

    /// <summary>
    /// Remove all passes from the graph.
    /// </summary>
    public void Clear() => _passes.Clear();

    /// <summary>
    /// Execute all render passes in order.
    /// </summary>
    /// <param name="cmd">Command buffer to record passes into</param>
    /// <param name="ctx">Render context for frame data</param>
    public void Execute(CommandBuffer cmd, RenderGraphContext ctx)
    {
        foreach (var pass in _passes)
        {
            try
            {
                pass.Execute(cmd, ctx);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing render pass '{pass.Name}': {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Get pass count for debugging.
    /// </summary>
    public int PassCount => _passes.Count;
}