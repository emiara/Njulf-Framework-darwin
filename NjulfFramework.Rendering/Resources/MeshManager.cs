// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using NjulfFramework.Core.Interfaces.Rendering;
using NjulfFramework.Rendering.Memory;
using NjulfFramework.Rendering.RenderingData;
using NjulfFramework.Rendering.Resources.Handles;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace NjulfFramework.Rendering.Resources;

/// <summary>
///     Refactored MeshManager using GPU-driven consolidated buffers.
///     Two-phase workflow:
///     1. Build phase: Register meshes, call Finalize()
///     2. Upload phase: Upload mesh data to GPU via FrameUploadRing
///     Replaces per-mesh vertex/index buffer creation with a single consolidated buffer approach.
/// </summary>
public class MeshManager : IMeshManager
{
    private readonly BufferManager _bufferManager;
    private readonly Device _device;

    private readonly MeshBuffer _meshBuffer;
    private readonly HashSet<Data.RenderingData.Mesh> _uploadedMeshes = new();
    private readonly Dictionary<string, Data.RenderingData.Mesh> _meshByName = new();
    private readonly Vk _vk;
    private bool _finalized;
    private readonly uint _graphicsQueueFamily;
    private readonly uint _transferQueueFamily;

    public MeshManager(Vk vk, Device device, BufferManager bufferManager, uint graphicsQueueFamily, uint transferQueueFamily)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = device;
        _bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
        _graphicsQueueFamily = graphicsQueueFamily;
        _transferQueueFamily = transferQueueFamily;

        _meshBuffer = new MeshBuffer(bufferManager, vk, device, graphicsQueueFamily, transferQueueFamily);
    }
    

    public void Dispose()
    {
        _meshBuffer?.Dispose();
        _uploadedMeshes.Clear();
    }

    public void RegisterMesh(string name, ReadOnlySpan<byte> vertices, ReadOnlySpan<uint> indices)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));

        var typedVertices = MemoryMarshal.Cast<byte, Data.RenderingData.Vertex>(vertices);

        var vertexArray = typedVertices.ToArray();
        var indexArray  = indices.ToArray();

        // Compute bounding box from vertex positions
        var boundsMin = new System.Numerics.Vector3(float.MaxValue);
        var boundsMax = new System.Numerics.Vector3(float.MinValue);
        foreach (var v in typedVertices)
        {
            boundsMin = System.Numerics.Vector3.Min(boundsMin, v.Position);
            boundsMax = System.Numerics.Vector3.Max(boundsMax, v.Position);
        }

        if (vertexArray.Length == 0)
        {
            boundsMin = System.Numerics.Vector3.Zero;
            boundsMax = System.Numerics.Vector3.Zero;
        }

        var mesh = new Data.RenderingData.Mesh(name, vertexArray, indexArray, boundsMin, boundsMax);
        RegisterMesh(mesh);
    }
    
    /// <summary>
    ///     Register a mesh with the consolidated buffer (before finalization).
    /// </summary>
    public void RegisterMesh(Data.RenderingData.Mesh mesh)
    {
        if (mesh == null)
            throw new ArgumentNullException(nameof(mesh));
        
        // No-op if already registered (same instance)
        if (_meshByName.TryGetValue(mesh.Name, out var existing) && ReferenceEquals(existing, mesh))
            return;
    
        _meshBuffer.AddMesh(mesh);
        _meshByName[mesh.Name] = mesh;
    }
    
    public void FinalizeOrReFinalize()
    {
        if (!_finalized)
        {
            Finalize();
        }
        else
        {
            ResetAndFinalize();
        }
    }

    /// <summary>
    ///     Finalize the mesh buffer after all meshes are registered.
    ///     Allocates GPU memory for consolidated buffers.
    /// </summary>
    public void Finalize()
    {
        if (_finalized)
            return;

        _meshBuffer.Finalize();
        _finalized = true;
        Console.WriteLine("✓ MeshManager finalized");
    }

    /// <summary>
    /// Old buffer handles from previous finalization (for deferred deletion).
    /// </summary>
    public (BufferHandle? Vertex, BufferHandle? Index, BufferHandle? Meshlet,
            BufferHandle? MeshletVertexIndices, BufferHandle? MeshletTriangleIndices) OldBufferHandles
        => _meshBuffer.OldBufferHandles;

    /// <summary>
    /// Whether the mesh manager has been finalized.
    /// </summary>
    public bool IsFinalized => _meshBuffer.IsFinalized;

    /// <summary>
    /// Clear old buffer handles after they've been consumed for deferred deletion.
    /// </summary>
    public void ClearOldBufferHandles() => _meshBuffer.ClearOldBufferHandles();

    /// <summary>
    /// Set the fence associated with old buffers for tracking when they can be safely deleted.
    /// </summary>
    public void SetOldBufferFence(Fence fence) => _meshBuffer.SetOldBufferFence(fence);

    /// <summary>
    /// Whether there are old buffers pending deletion.
    /// </summary>
    public bool HasOldBuffersPendingDeletion => _meshBuffer.HasOldBuffersPendingDeletion;

    /// <summary>
    /// Check if all old buffer fences have signaled (safe to update bindless heap).
    /// </summary>
    public bool OldBufferFencesAllSignaled(Vk vk, Device device) => _meshBuffer.OldBufferFencesAllSignaled(vk, device);

    /// <summary>
    /// Reset the finalization state and perform dynamic finalization.
    /// Allows adding new meshes after initial finalization.
    /// </summary>
    public void ResetAndFinalize()
    {
        if (!_finalized)
            throw new InvalidOperationException("MeshManager not initially finalized");
        
        // Reset finalization state
        _finalized = false;
        
        // Clear uploaded mesh tracking so all meshes are re-uploaded with new GPU buffers
        _uploadedMeshes.Clear();
        
        // Perform finalization again with current mesh set
        _meshBuffer.Finalize();
        _finalized = true;
        
        Console.WriteLine("✓ MeshManager re-finalized for dynamic loading");
    }

    public bool TryGetMeshDescriptor(string name, out IMeshDescriptor? descriptor)
    {
        // Try to get mesh descriptor by name
        descriptor = null;
        return false;
    }

    /// <summary>
    ///     Get or create mesh GPU data (returns cached offset info).
    ///     Call after Finalize().
    /// </summary>
    public MeshBuffer.MeshEntry GetOrCreateMeshGpu(Data.RenderingData.Mesh mesh)
    {
        if (!_finalized)
            throw new InvalidOperationException("MeshManager not finalized. Call Finalize() first.");

        return _meshBuffer.GetMeshEntry(mesh);
    }

    /// <summary>
    ///     Upload a single mesh's data to GPU.
    ///     Typically called once per mesh during load phase.
    /// </summary>
    public void UploadMeshToGPU(Data.RenderingData.Mesh mesh, CommandBuffer transferCmd, FrameUploadRing uploadRing)
    {
        if (!_finalized)
            throw new InvalidOperationException("MeshManager not finalized");

        if (_uploadedMeshes.Contains(mesh))
            return; // Already uploaded

        _meshBuffer.UploadMeshData(mesh, transferCmd, uploadRing);
        _uploadedMeshes.Add(mesh);
    }

    /// <summary>
    ///     Bind consolidated vertex and index buffers.
    /// </summary>
    public void BindMeshBuffers(CommandBuffer cmd)
    {
        _meshBuffer.BindBuffers(cmd);
    }

    /// <summary>
    ///     Draw a mesh using consolidated buffers.
    /// </summary>
    public void DrawMesh(CommandBuffer cmd, Data.RenderingData.Mesh mesh)
    {
        _meshBuffer.DrawMesh(cmd, mesh);
    }

    /// <summary>
    ///     Get GPU mesh data struct for scene buffer population.
    /// </summary>
    public GPUMeshData GetGPUMeshData(Data.RenderingData.Mesh mesh)
    {
        return _meshBuffer.GetGPUMeshData(mesh);
    }

    /// <summary>
    ///     Get consolidated buffers for bindless registration.
    /// </summary>
    public (BufferHandle VertexHandle, BufferHandle IndexHandle) GetMeshBufferHandles()
    {
        return (VertexHandle: _meshBuffer.VertexBufferHandle,
                IndexHandle: _meshBuffer.IndexBufferHandle);
    }

    /// <summary>
    ///     Get all mesh buffer handles for bindless registration.
    /// </summary>
    public (BufferHandle VertexHandle, BufferHandle IndexHandle, BufferHandle MeshletHandle,
            BufferHandle MeshletVertexIndicesHandle, BufferHandle MeshletTriangleIndicesHandle)
        GetAllMeshBufferHandles()
    {
        return (VertexHandle: _meshBuffer.VertexBufferHandle,
                IndexHandle: _meshBuffer.IndexBufferHandle,
                MeshletHandle: _meshBuffer.MeshletBufferHandle,
                MeshletVertexIndicesHandle: _meshBuffer.MeshletVertexIndicesBufferHandle,
                MeshletTriangleIndicesHandle: _meshBuffer.MeshletTriangleIndicesBufferHandle);
    }

    /// <summary>
    ///     Get consolidated buffer objects.
    /// </summary>
    public (Buffer VertexBuffer, Buffer IndexBuffer) GetMeshBuffers()
    {
        return (VertexBuffer: _meshBuffer.VertexBuffer,
                IndexBuffer: _meshBuffer.IndexBuffer);
    }

    public (Buffer MeshletBuffer, Buffer MeshletVertexIndicesBuffer, Buffer MeshletTriangleIndicesBuffer)
        GetMeshletBuffers()
    {
        return (MeshletBuffer: _meshBuffer.MeshletBuffer,
                MeshletVertexIndicesBuffer: _meshBuffer.MeshletVertexIndicesBuffer,
                MeshletTriangleIndicesBuffer: _meshBuffer.MeshletTriangleIndicesBuffer);
    }

    /// <summary>
    ///     Look up a registered mesh by name. Returns null if not found.
    /// </summary>
    public Data.RenderingData.Mesh? TryGetMeshByName(string name)
    {
        _meshByName.TryGetValue(name, out var mesh);
        return mesh;
    }

    /// <summary>
    ///     Check if all mesh buffer handles are valid (for diagnostics).
    /// </summary>
    public bool AreAllBufferHandlesValid()
    {
        var handles = GetAllMeshBufferHandles();
        return handles.VertexHandle.IsValid && 
               handles.IndexHandle.IsValid && 
               handles.MeshletHandle.IsValid && 
               handles.MeshletVertexIndicesHandle.IsValid && 
               handles.MeshletTriangleIndicesHandle.IsValid;
    }
}