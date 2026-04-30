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

    public MeshManager(Vk vk, Device device, BufferManager bufferManager)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = device;
        _bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));

        _meshBuffer = new MeshBuffer(bufferManager, vk, device);
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
        return (_meshBuffer.VertexBufferHandle, _meshBuffer.IndexBufferHandle);
    }

    /// <summary>
    ///     Get consolidated buffer objects.
    /// </summary>
    public (Buffer VertexBuffer, Buffer IndexBuffer) GetMeshBuffers()
    {
        return (_meshBuffer.VertexBuffer, _meshBuffer.IndexBuffer);
    }

    public (Buffer MeshletBuffer, Buffer MeshletVertexIndicesBuffer, Buffer MeshletTriangleIndicesBuffer)
        GetMeshletBuffers()
    {
        return (_meshBuffer.MeshletBuffer, _meshBuffer.MeshletVertexIndicesBuffer,
            _meshBuffer.MeshletTriangleIndicesBuffer);
    }

    /// <summary>
    ///     Look up a registered mesh by name. Returns null if not found.
    /// </summary>
    public Data.RenderingData.Mesh? TryGetMeshByName(string name)
    {
        _meshByName.TryGetValue(name, out var mesh);
        return mesh;
    }
}