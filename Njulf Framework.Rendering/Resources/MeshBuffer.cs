// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Njulf_Framework.Rendering.Data;
using Njulf_Framework.Rendering.Memory;
using Njulf_Framework.Rendering.Resources.Handles;
using Vma;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Njulf_Framework.Rendering.Resources;

/// <summary>
/// GPU-driven mesh buffer system.
/// Consolidates all mesh vertex/index data into two large buffers (vertex + index).
/// Mesh offsets stored in CPU-side registry for efficient bindless access.
/// 
/// This replaces per-mesh vertex/index buffer creation.
/// Shaders access meshes via GPUMeshData structs with offset lookups.
/// </summary>
public class MeshBuffer : IDisposable
{
    private readonly BufferManager _bufferManager;
    private readonly Vk _vk;
    private readonly Device _device;

    // GPU-side consolidated buffers
    private BufferHandle _vertexBufferHandle;
    private BufferHandle _indexBufferHandle;
    private Buffer _vertexBuffer;
    private Buffer _indexBuffer;

    // CPU-side mesh registry
    public class MeshEntry
    {
        public uint VertexOffset { get; set; }      // Offset in vertex buffer (in vertices)
        public uint IndexOffset { get; set; }       // Offset in index buffer (in indices)
        public uint VertexCount { get; set; }
        public uint IndexCount { get; set; }
    }

    private readonly Dictionary<Data.RenderingData.Mesh, MeshEntry> _meshes = new();
    private uint _totalVertices = 0;
    private uint _totalIndices = 0;
    private bool _finalized = false;

    public MeshBuffer(BufferManager bufferManager, Vk vk, Device device)
    {
        _bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = device;
    }

    /// <summary>
    /// Add a mesh to the consolidated buffer (pre-finalization).
    /// Called during initialization; tracks offsets but doesn't upload yet.
    /// </summary>
    public void AddMesh(Data.RenderingData.Mesh mesh)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot add meshes after MeshBuffer is finalized");

        if (mesh == null)
            throw new ArgumentNullException(nameof(mesh));

        if (_meshes.ContainsKey(mesh))
            return;  // Already added

        var entry = new MeshEntry
        {
            VertexOffset = _totalVertices,
            IndexOffset = _totalIndices,
            VertexCount = (uint)(mesh.Vertices?.Length ?? 0),
            IndexCount = (uint)(mesh.Indices?.Length ?? 0)
        };

        _meshes[mesh] = entry;
        _totalVertices += entry.VertexCount;
        _totalIndices += entry.IndexCount;

        Console.WriteLine($"Added mesh '{mesh.Name}': {entry.VertexCount} vertices, {entry.IndexCount} indices");
    }

    /// <summary>
    /// Finalize the mesh buffer: allocate GPU memory and prepare for uploads.
    /// Called once after all meshes are registered.
    /// </summary>
    public void Finalize()
    {
        if (_finalized)
            throw new InvalidOperationException("MeshBuffer already finalized");

        if (_meshes.Count == 0)
            throw new InvalidOperationException("No meshes registered");

        Console.WriteLine($"Finalizing MeshBuffer: {_totalVertices} total vertices, {_totalIndices} total indices");

        // Allocate vertex buffer
        if (_totalVertices > 0)
        {
            var vertexSize = _totalVertices * Marshal.SizeOf<Data.RenderingData.Vertex>();

            _vertexBufferHandle = _bufferManager.AllocateBuffer(
                (ulong)vertexSize,
                BufferUsageFlags.VertexBufferBit 
                    | BufferUsageFlags.StorageBufferBit  // For bindless access
                    | BufferUsageFlags.TransferDstBit,    // For uploading
                MemoryUsage.AutoPreferDevice);

            _vertexBuffer = _bufferManager.GetBuffer(_vertexBufferHandle);
            Console.WriteLine($"✓ Vertex buffer allocated: {vertexSize / (1024 * 1024)} MB");
        }

        // Allocate index buffer
        if (_totalIndices > 0)
        {
            var indexSize = _totalIndices * sizeof(uint);

            _indexBufferHandle = _bufferManager.AllocateBuffer(
                (ulong)indexSize,
                BufferUsageFlags.IndexBufferBit 
                    | BufferUsageFlags.StorageBufferBit   // For bindless access
                    | BufferUsageFlags.TransferDstBit,    // For uploading
                MemoryUsage.AutoPreferDevice);

            _indexBuffer = _bufferManager.GetBuffer(_indexBufferHandle);
            Console.WriteLine($"✓ Index buffer allocated: {indexSize / (1024 * 1024)} MB");
        }

        _finalized = true;
    }

    /// <summary>
    /// Get mesh entry (offset + counts) for a given mesh.
    /// </summary>
    public MeshEntry GetMeshEntry(Data.RenderingData.Mesh mesh)
    {
        if (!_meshes.TryGetValue(mesh, out var entry))
            throw new KeyNotFoundException($"Mesh '{mesh?.Name}' not found in MeshBuffer");
        return entry;
    }

    /// <summary>
    /// Get mesh offsets as GPUMeshData struct (for scene buffer upload).
    /// </summary>
    public RenderingData.GPUMeshData GetGPUMeshData(Data.RenderingData.Mesh mesh)
    {
        var entry = GetMeshEntry(mesh);
        return new RenderingData.GPUMeshData
        {
            VertexBufferIndex = 0,      // Bindless index (will be set by BindlessDescriptorHeap)
            IndexBufferIndex = 1,       // Bindless index
            VertexCount = entry.VertexCount,
            IndexCount = entry.IndexCount,
            BoundingBoxMin = new System.Numerics.Vector3(-1),
            BoundingBoxMax = new System.Numerics.Vector3(1)
        };
    }

    /// <summary>
    /// Bind consolidated vertex and index buffers to command buffer.
    /// </summary>
    public void BindBuffers(CommandBuffer cmd)
    {
        if (!_finalized)
            throw new InvalidOperationException("MeshBuffer not finalized (Bind)");

        if (_vertexBuffer.Handle != 0)
            _vk.CmdBindVertexBuffers(cmd, 0, 1, _vertexBuffer, 0);

        if (_indexBuffer.Handle != 0)
            _vk.CmdBindIndexBuffer(cmd, _indexBuffer, 0, IndexType.Uint32);
    }

    /// <summary>
    /// Draw a mesh using consolidated buffers with offset tracking.
    /// </summary>
    public unsafe void DrawMesh(CommandBuffer cmd, Data.RenderingData.Mesh mesh)
    {
        if (!_finalized)
            throw new InvalidOperationException("MeshBuffer not finalized (Draw)");

        var entry = GetMeshEntry(mesh);

        if (entry.IndexCount > 0)
        {
            _vk.CmdDrawIndexed(cmd, entry.IndexCount, 1, entry.IndexOffset, (int)entry.VertexOffset, 0);
        }
    }

    /// <summary>
    /// Upload mesh data from CPU to GPU via staging buffer.
    /// Typically called once per mesh after finalization.
    /// </summary>
    public void UploadMeshData(Data.RenderingData.Mesh mesh, CommandBuffer transferCmd, FrameUploadRing uploadRing)
    {
        if (!_finalized)
            throw new InvalidOperationException("MeshBuffer not finalized (Upload)");

        if (mesh == null || mesh.Vertices == null || mesh.Indices == null)
            return;

        var entry = GetMeshEntry(mesh);

        // Write vertex data to staging buffer
        if (mesh.Vertices.Length > 0)
        {
            uploadRing.WriteData(mesh.Vertices, out var vertexSrcOffset);

            var srcBuffer = uploadRing.CurrentUploadBuffer;
            var dstBuffer = _vertexBuffer;
            var vertexSize = (ulong)(entry.VertexCount * Marshal.SizeOf<Data.RenderingData.Vertex>());
            var dstOffset = entry.VertexOffset * (ulong)Marshal.SizeOf<Data.RenderingData.Vertex>();

            RecordCopyCommand(transferCmd, srcBuffer, dstBuffer, vertexSrcOffset, dstOffset, vertexSize);
        }

        // Write index data to staging buffer
        if (mesh.Indices.Length > 0)
        {
            uploadRing.WriteData(mesh.Indices, out var indexSrcOffset);

            var srcBuffer = uploadRing.CurrentUploadBuffer;
            var dstBuffer = _indexBuffer;
            var indexSize = (ulong)(entry.IndexCount * sizeof(uint));
            var dstOffset = entry.IndexOffset * sizeof(uint);

            RecordCopyCommand(transferCmd, srcBuffer, dstBuffer, indexSrcOffset, dstOffset, indexSize);
        }

        Console.WriteLine($"✓ Uploaded mesh '{mesh.Name}' to GPU buffers");
    }

    /// <summary>
    /// Helper: Record a buffer copy command.
    /// </summary>
    private unsafe void RecordCopyCommand(CommandBuffer cmd, Buffer srcBuffer, Buffer dstBuffer,
        ulong srcOffset, ulong dstOffset, ulong size)
    {
        var region = new BufferCopy
        {
            SrcOffset = srcOffset,
            DstOffset = dstOffset,
            Size = size
        };

        _vk.CmdCopyBuffer(cmd, srcBuffer, dstBuffer, 1, &region);
    }

    /// <summary>
    /// Get vertex buffer handle (for bindless registration).
    /// </summary>
    public BufferHandle VertexBufferHandle => _vertexBufferHandle;

    /// <summary>
    /// Get index buffer handle (for bindless registration).
    /// </summary>
    public BufferHandle IndexBufferHandle => _indexBufferHandle;

    /// <summary>
    /// Get actual vertex buffer.
    /// </summary>
    public Buffer VertexBuffer => _vertexBuffer;

    /// <summary>
    /// Get actual index buffer.
    /// </summary>
    public Buffer IndexBuffer => _indexBuffer;

    /// <summary>
    /// Total vertex count across all meshes.
    /// </summary>
    public uint TotalVertices => _totalVertices;

    /// <summary>
    /// Total index count across all meshes.
    /// </summary>
    public uint TotalIndices => _totalIndices;

    /// <summary>
    /// Mesh count.
    /// </summary>
    public int MeshCount => _meshes.Count;

    public void Dispose()
    {
        _meshes.Clear();
        // BufferManager handles actual VMA deallocation
    }
}
