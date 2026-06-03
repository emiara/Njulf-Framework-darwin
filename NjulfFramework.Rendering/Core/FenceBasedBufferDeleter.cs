// SPDX-License-Identifier: MPL-2.0

using NjulfFramework.Rendering.Resources;
using NjulfFramework.Rendering.Resources.Handles;
using Silk.NET.Vulkan;

namespace NjulfFramework.Rendering.Core;

/// <summary>
/// Deletes buffers when their associated fence signals.
/// Industry-standard fence-based resource lifecycle management for Vulkan.
/// </summary>
public class FenceBasedBufferDeleter : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly BufferManager _bufferManager;
    
    private readonly List<(BufferHandle Handle, Fence Fence)> _pending = new();

    public FenceBasedBufferDeleter(Vk vk, Device device, BufferManager bufferManager)
    {
        _vk = vk;
        _device = device;
        _bufferManager = bufferManager;
    }

    /// <summary>
    /// Track a buffer for deletion when the fence signals.
    /// </summary>
    public void Track(BufferHandle handle, Fence fence)
    {
        if (!handle.IsValid) return;
        _pending.Add((handle, fence));
    }

    /// <summary>
    /// Track all mesh buffers for deletion with the same fence.
    /// </summary>
    public void TrackMeshBuffers(
        BufferHandle? vertex,
        BufferHandle? index,
        BufferHandle? meshlet,
        BufferHandle? meshletVertexIndices,
        BufferHandle? meshletTriangleIndices,
        Fence fence)
    {
        if (vertex.HasValue && vertex.Value.IsValid) Track(vertex.Value, fence);
        if (index.HasValue && index.Value.IsValid) Track(index.Value, fence);
        if (meshlet.HasValue && meshlet.Value.IsValid) Track(meshlet.Value, fence);
        if (meshletVertexIndices.HasValue && meshletVertexIndices.Value.IsValid) Track(meshletVertexIndices.Value, fence);
        if (meshletTriangleIndices.HasValue && meshletTriangleIndices.Value.IsValid) Track(meshletTriangleIndices.Value, fence);
    }

    /// <summary>
    /// Clean up all buffers whose fences have signaled.
    /// Call this once per frame.
    /// </summary>
    public unsafe void Cleanup()
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var (handle, fence) = _pending[i];
            if (_vk.GetFenceStatus(_device, fence) == Result.Success)
            {
                _bufferManager.FreeBuffer(handle);
                _pending.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        Cleanup();
    }
}
