using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using Njulf_Framework.Rendering.Data;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Njulf_Framework.Rendering.Resources;

public class MeshManager : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly BufferManager _bufferManager;
    private readonly Dictionary<string, MeshGpuData> _meshCache = new();

    public struct MeshGpuData
    {
        public Buffer VertexBuffer;
        public Buffer IndexBuffer;
        public DeviceMemory VertexMemory;
        public DeviceMemory IndexMemory;
        public uint IndexCount;
    }

    public MeshManager(Vk vk, Device device, BufferManager bufferManager)
    {
        _vk = vk;
        _device = device;
        _bufferManager = bufferManager;
    }

    /// <summary>
    /// Upload mesh to GPU or return cached version.
    /// </summary>
    public unsafe MeshGpuData GetOrCreateMeshGpu(RenderingData.Mesh mesh)
    {
        if (_meshCache.TryGetValue(mesh.Name, out var cached))
        {
            return cached;
        }

        // Upload vertices
        fixed (RenderingData.Vertex* verticesPtr = mesh.Vertices)
        {
            // Create vertex buffer
            var vertexBufferSize = (ulong)(mesh.Vertices.Length * sizeof(RenderingData.Vertex));
            var vertexBuffer = _bufferManager.CreateVertexBuffer(verticesPtr, vertexBufferSize);

            // Create index buffer
            fixed (uint* indicesPtr = mesh.Indices)
            {
                var indexBuffer = _bufferManager.CreateIndexBuffer(indicesPtr, (uint)mesh.Indices.Length);

                var gpuData = new MeshGpuData
                {
                    VertexBuffer = vertexBuffer,
                    IndexBuffer = indexBuffer,
                    IndexCount = (uint)mesh.Indices.Length,
                    VertexMemory = default,
                    IndexMemory = default
                };

                _meshCache[mesh.Name] = gpuData;
                Console.WriteLine($"Uploaded mesh '{mesh.Name}' to GPU: {mesh.Vertices.Length} vertices, {mesh.Indices.Length} indices");

                return gpuData;
            }
        }
    }

    /// <summary>
    /// Bind mesh for rendering.
    /// </summary>
    public unsafe void BindMesh(CommandBuffer commandBuffer, MeshGpuData meshData)
    {
        var vertexBuffers = stackalloc Buffer[] { meshData.VertexBuffer };
        var offsets = stackalloc ulong[] { 0 };

        _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, vertexBuffers, offsets);
        _vk.CmdBindIndexBuffer(commandBuffer, meshData.IndexBuffer, 0, IndexType.Uint32);
    }

    /// <summary>
    /// Draw mesh with index buffer.
    /// </summary>
    public void DrawMesh(CommandBuffer commandBuffer, MeshGpuData meshData)
    {
        _vk.CmdDrawIndexed(commandBuffer, meshData.IndexCount, 1, 0, 0, 0);
    }

    public unsafe void Dispose()
    {
        foreach (var meshData in _meshCache.Values)
        {
            _vk.DestroyBuffer(_device, meshData.VertexBuffer, null);
            _vk.DestroyBuffer(_device, meshData.IndexBuffer, null);
        }
        _meshCache.Clear();
    }
}

/// <summary>
/// Manages uniform buffers for transformations.
/// One UBO per frame in flight.
/// </summary>
public class UniformBufferManager : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly BufferManager _bufferManager;
    private readonly List<BufferManager.BufferAllocation> _uniformBuffers = new();

    public UniformBufferManager(Vk vk, Device device, BufferManager bufferManager, uint framesInFlight)
    {
        _vk = vk;
        _device = device;
        _bufferManager = bufferManager;

        // Create one UBO per frame
        for (int i = 0; i < framesInFlight; i++)
        {
            var allocation = bufferManager.CreateUniformBuffer(RenderingData.UniformBufferObject.GetSizeInBytes());
            _uniformBuffers.Add(allocation);
        }
    }

    /// <summary>
    /// Update uniform buffer for current frame.
    /// </summary>
    public unsafe void UpdateUniformBuffer(uint frameIndex, RenderingData.UniformBufferObject ubo)
    {
        if (frameIndex >= _uniformBuffers.Count)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        var allocation = _uniformBuffers[(int)frameIndex];

        // Map, update, unmap
        void* data = null;
        _vk.MapMemory(_device, allocation.Memory, 0, RenderingData.UniformBufferObject.GetSizeInBytes(), MemoryMapFlags.None, &data);
            
        // Copy UBO to mapped memory
        System.Runtime.InteropServices.Marshal.StructureToPtr(ubo, (System.IntPtr)data, false);
            
        _vk.UnmapMemory(_device, allocation.Memory);
    }

    public Silk.NET.Vulkan.Buffer GetUniformBuffer(uint frameIndex)
    {
        if (frameIndex >= _uniformBuffers.Count)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        return _uniformBuffers[(int)frameIndex].Buffer;
    }

    public void Dispose()
    {
        // Individual buffers are cleaned up by BufferManager
        _uniformBuffers.Clear();
    }  
}