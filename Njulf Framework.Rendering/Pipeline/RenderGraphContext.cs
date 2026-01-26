// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Njulf_Framework.Rendering.Data;
using Njulf_Framework.Rendering.Resources;
using Njulf_Framework.Rendering.Resources.Descriptors;

namespace Njulf_Framework.Rendering.Pipeline;

/// <summary>
/// Context data passed to render graph passes, containing frame-specific
/// information like dimensions, visible objects, and acceleration structures.
/// </summary>
public class RenderGraphContext(uint width, uint height, BindlessDescriptorHeap bindlessHeap)
{
    /// <summary>
    /// Render target width in pixels.
    /// </summary>
    public uint Width { get; set; } = width;

    /// <summary>
    /// Render target height in pixels.
    /// </summary>
    public uint Height { get; set; } = height;
    
    public BindlessDescriptorHeap BindlessHeap { get; set; } = bindlessHeap;

    /// <summary>
    /// Current frame index for ring buffer management.
    /// </summary>
    public uint FrameIndex { get; set; }

    /// <summary>
    /// List of visible render objects for this frame.
    /// Populated after frustum/occlusion culling.
    /// </summary>
    public List<Data.RenderingData.RenderObject> VisibleObjects { get; set; } = new();
    
    /// <summary>
    /// Color attachment image view for the current frame.
    /// </summary>
    public ImageView ColorAttachmentView { get; set; }

    /// <summary>
    /// Depth attachment image view (optional).
    /// </summary>
    public ImageView DepthAttachmentView { get; set; }

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
    /// View matrix for the current frame.
    /// </summary>
    public System.Numerics.Matrix4x4 View { get; set; }

    /// <summary>
    /// Projection matrix for the current frame.
    /// </summary>
    public System.Numerics.Matrix4x4 Projection { get; set; }

    /// <summary>
    /// Camera position in world space.
    /// </summary>
    public System.Numerics.Vector3 CameraPosition { get; set; }

    /// <summary>
    /// Mesh manager for binding and drawing consolidated buffers.
    /// </summary>
    public MeshManager? MeshManager { get; set; }

    /// <summary>
    /// Descriptor set for mesh vertex/index buffers.
    /// </summary>
    public DescriptorSet MeshBuffersSet { get; set; }

    /// <summary>
    /// Total light count for this frame.
    /// </summary>
    public uint LightCount { get; set; }

    /// <summary>
    /// Bindless buffer index for the light buffer.
    /// </summary>
    public uint LightBufferIndex { get; set; }

    /// <summary>
    /// Bindless buffer index for tiled light headers.
    /// </summary>
    public uint TiledLightHeaderBufferIndex { get; set; }

    /// <summary>
    /// Bindless buffer index for tiled light indices.
    /// </summary>
    public uint TiledLightIndicesBufferIndex { get; set; }
}
