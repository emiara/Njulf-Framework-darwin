// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Njulf_Framework.Rendering.Data;
using Njulf_Framework.Rendering.Memory;
using Njulf_Framework.Rendering.Resources.Handles;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Njulf_Framework.Rendering.Resources;

/// <summary>
/// Refactored MeshManager using GPU-driven consolidated buffers.
/// 
/// Two-phase workflow:
/// 1. Build phase: Register meshes, call Finalize()
/// 2. Upload phase: Upload mesh data to GPU via FrameUploadRing
/// 
/// Replaces per-mesh vertex/index buffer creation with a single consolidated buffer approach.
/// </summary>
public class MeshManager : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly BufferManager _bufferManager;
    
    private MeshBuffer _meshBuffer;
    private bool _finalized = false;
    private HashSet<Data.RenderingData.Mesh> _uploadedMeshes = new();

    public MeshManager(Vk vk, Device device, BufferManager bufferManager)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = device;
        _bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));

        _meshBuffer = new MeshBuffer(bufferManager, vk, device);
    }

    /// <summary>
    /// Register a mesh with the consolidated buffer (before finalization).
    /// </summary>
    public void RegisterMesh(Data.RenderingData.Mesh mesh)
    {
        if (mesh == null)
            throw new ArgumentNullException(nameof(mesh));

        if (_finalized)
            throw new InvalidOperationException("MeshManager already finalized. Cannot register new meshes.");

        _meshBuffer.AddMesh(mesh);
    }

    /// <summary>
    /// Finalize the mesh buffer after all meshes are registered.
    /// Allocates GPU memory for consolidated buffers.
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
    /// Get or create mesh GPU data (returns cached offset info).
    /// Call after Finalize().
    /// </summary>
    public MeshBuffer.MeshEntry GetOrCreateMeshGpu(Data.RenderingData.Mesh mesh)
    {
        if (!_finalized)
            throw new InvalidOperationException("MeshManager not finalized. Call Finalize() first.");

        return _meshBuffer.GetMeshEntry(mesh);
    }

    /// <summary>
    /// Upload a single mesh's data to GPU.
    /// Typically called once per mesh during load phase.
    /// </summary>
    public void UploadMeshToGPU(Data.RenderingData.Mesh mesh, CommandBuffer transferCmd, FrameUploadRing uploadRing)
    {
        if (!_finalized)
            throw new InvalidOperationException("MeshManager not finalized");

        if (_uploadedMeshes.Contains(mesh))
            return;  // Already uploaded

        _meshBuffer.UploadMeshData(mesh, transferCmd, uploadRing);
        _uploadedMeshes.Add(mesh);
    }

    /// <summary>
    /// Bind consolidated vertex and index buffers.
    /// </summary>
    public void BindMeshBuffers(CommandBuffer cmd)
    {
        _meshBuffer.BindBuffers(cmd);
    }

    /// <summary>
    /// Draw a mesh using consolidated buffers.
    /// </summary>
    public void DrawMesh(CommandBuffer cmd, Data.RenderingData.Mesh mesh)
    {
        _meshBuffer.DrawMesh(cmd, mesh);
    }

    /// <summary>
    /// Get GPU mesh data struct for scene buffer population.
    /// </summary>
    public RenderingData.GPUMeshData GetGPUMeshData(Data.RenderingData.Mesh mesh)
    {
        return _meshBuffer.GetGPUMeshData(mesh);
    }

    /// <summary>
    /// Get consolidated buffers for bindless registration.
    /// </summary>
    public (BufferHandle VertexHandle, BufferHandle IndexHandle) GetMeshBufferHandles()
        => (_meshBuffer.VertexBufferHandle, _meshBuffer.IndexBufferHandle);

    /// <summary>
    /// Get consolidated buffer objects.
    /// </summary>
    public (Buffer VertexBuffer, Buffer IndexBuffer) GetMeshBuffers()
        => (_meshBuffer.VertexBuffer, _meshBuffer.IndexBuffer);

    public (Buffer MeshletBuffer, Buffer MeshletVertexIndicesBuffer, Buffer MeshletTriangleIndicesBuffer) GetMeshletBuffers()
        => (_meshBuffer.MeshletBuffer, _meshBuffer.MeshletVertexIndicesBuffer, _meshBuffer.MeshletTriangleIndicesBuffer);

    public void Dispose()
    {
        _meshBuffer?.Dispose();
        _uploadedMeshes.Clear();
    }
}
