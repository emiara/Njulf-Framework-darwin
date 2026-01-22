// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Njulf_Framework.Rendering.Data;

namespace Njulf_Framework.Rendering.Pipeline;

/// <summary>
/// Context data passed to render graph passes, containing frame-specific
/// information like dimensions, visible objects, and acceleration structures.
/// </summary>
public class RenderGraphContext
{
    /// <summary>
    /// Render target width in pixels.
    /// </summary>
    public uint Width { get; set; }

    /// <summary>
    /// Render target height in pixels.
    /// </summary>
    public uint Height { get; set; }

    /// <summary>
    /// Current frame index for ring buffer management.
    /// </summary>
    public uint FrameIndex { get; set; }

    /// <summary>
    /// List of visible render objects for this frame.
    /// Populated after frustum/occlusion culling.
    /// </summary>
    public List<RenderingData.RenderObject> VisibleObjects { get; set; } = new();

    /// <summary>
    /// Top-level acceleration structure for ray tracing passes.
    /// May be null if ray tracing is not active.
    /// </summary>
    public AccelerationStructureKHR? TLAS { get; set; }

    /// <summary>
    /// View-projection matrix for the current frame.
    /// </summary>
    public System.Numerics.Matrix4x4 ViewProjection { get; set; }

    /// <summary>
    /// Camera position in world space.
    /// </summary>
    public System.Numerics.Vector3 CameraPosition { get; set; }
}