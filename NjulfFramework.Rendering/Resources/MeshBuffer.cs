// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using System.Runtime.InteropServices;
using NjulfFramework.Rendering.Memory;
using NjulfFramework.Rendering.RenderingData;
using NjulfFramework.Rendering.Resources.Handles;
using Silk.NET.Vulkan;
using Vma;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace NjulfFramework.Rendering.Resources;

/// <summary>
///     GPU-driven mesh buffer system.
///     Consolidates all mesh vertex/index data into two large buffers (vertex + index).
///     Mesh offsets stored in CPU-side registry for efficient bindless access.
///     This replaces per-mesh vertex/index buffer creation.
///     Shaders access meshes via GPUMeshData structs with offset lookups.
/// </summary>
public class MeshBuffer : IDisposable
{
    private const uint MaxPrimsPerMeshlet = 64;
    private const uint MaxVertsPerMeshlet = 128;

    private readonly BufferManager _bufferManager;
    private readonly Device _device;

    private readonly Dictionary<Data.RenderingData.Mesh, MeshEntry> _meshes = new();

    private readonly List<GPUMeshlet> _meshlets = new();
    private readonly List<uint> _meshletTriangleIndices = new();
    private readonly List<uint> _meshletVertexIndices = new();
    private readonly Vk _vk;
    private bool _finalized;
    private Buffer _indexBuffer;

    private bool _meshletDataUploaded;
    private Buffer _vertexBuffer;

    // GPU-side consolidated buffers

    public MeshBuffer(BufferManager bufferManager, Vk vk, Device device)
    {
        _bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = device;
    }

    /// <summary>
    ///     Get vertex buffer handle (for bindless registration).
    /// </summary>
    public BufferHandle VertexBufferHandle { get; private set; }

    /// <summary>
    ///     Get index buffer handle (for bindless registration).
    /// </summary>
    public BufferHandle IndexBufferHandle { get; private set; }

    /// <summary>
    ///     Get actual vertex buffer.
    /// </summary>
    public Buffer VertexBuffer => _vertexBuffer;

    /// <summary>
    ///     Get actual index buffer.
    /// </summary>
    public Buffer IndexBuffer => _indexBuffer;

    public Buffer MeshletBuffer { get; private set; }

    public Buffer MeshletVertexIndicesBuffer { get; private set; }

    public Buffer MeshletTriangleIndicesBuffer { get; private set; }

    public BufferHandle MeshletBufferHandle { get; private set; }

    public BufferHandle MeshletVertexIndicesBufferHandle { get; private set; }

    public BufferHandle MeshletTriangleIndicesBufferHandle { get; private set; }

    /// <summary>
    ///     Total vertex count across all meshes.
    /// </summary>
    public uint TotalVertices { get; private set; }

    /// <summary>
    ///     Total index count across all meshes.
    /// </summary>
    public uint TotalIndices { get; private set; }

    /// <summary>
    ///     Mesh count.
    /// </summary>
    public int MeshCount => _meshes.Count;

    public void Dispose()
    {
        _meshes.Clear();
        _meshlets.Clear();
        _meshletVertexIndices.Clear();
        _meshletTriangleIndices.Clear();
        // BufferManager handles actual VMA deallocation
    }

    /// <summary>
    ///     Add a mesh to the consolidated buffer (pre-finalization).
    ///     Called during initialization; tracks offsets but doesn't upload yet.
    /// </summary>
    public void AddMesh(Data.RenderingData.Mesh mesh)
    {
        if (mesh == null)
            throw new ArgumentNullException(nameof(mesh));

        if (_meshes.ContainsKey(mesh))
            return; // Already added

        var entry = new MeshEntry
        {
            VertexOffset = TotalVertices,
            IndexOffset = TotalIndices,
            VertexCount = (uint)(mesh.Vertices?.Length ?? 0),
            IndexCount = (uint)(mesh.Indices?.Length ?? 0),
            BoundsRadius = ComputeBoundsRadius(mesh)
        };

        _meshes[mesh] = entry;
        TotalVertices += entry.VertexCount;
        TotalIndices += entry.IndexCount;

        Console.WriteLine($"Added mesh '{mesh.Name}': {entry.VertexCount} vertices, {entry.IndexCount} indices");
    }

    /// <summary>
    ///     Finalize the mesh buffer: allocate GPU memory and prepare for uploads.
    ///     Called once after all meshes are registered.
    /// </summary>
    public void Finalize()
    {
        if (_meshes.Count == 0)
            throw new InvalidOperationException("No meshes registered");

        if (_finalized)
        {
            // Re-finalization: Clean up existing buffers first
            Console.WriteLine("Re-finalizing MeshBuffer...");
        
        if (VertexBufferHandle.IsValid)
            _bufferManager.FreeBuffer(VertexBufferHandle);
        if (IndexBufferHandle.IsValid)
            _bufferManager.FreeBuffer(IndexBufferHandle);
        if (MeshletBufferHandle.IsValid)
            _bufferManager.FreeBuffer(MeshletBufferHandle);
        if (MeshletVertexIndicesBufferHandle.IsValid)
            _bufferManager.FreeBuffer(MeshletVertexIndicesBufferHandle);
        if (MeshletTriangleIndicesBufferHandle.IsValid)
            _bufferManager.FreeBuffer(MeshletTriangleIndicesBufferHandle);

            // Reset meshlet data
            _meshlets.Clear();
            _meshletVertexIndices.Clear();
            _meshletTriangleIndices.Clear();
            _meshletDataUploaded = false;
            
            // Reset totals so offsets are recomputed correctly
            TotalVertices = 0;
            TotalIndices = 0;

            // Recompute offsets for all registered meshes
            var meshKeys = _meshes.Keys.ToList();
            _meshes.Clear();
            foreach (var mesh in meshKeys)
                AddMesh(mesh);
        }
        else
        {
            Console.WriteLine($"Finalizing MeshBuffer: {TotalVertices} total vertices, {TotalIndices} total indices");
        }

        BuildMeshlets();

        // Allocate vertex buffer
        if (TotalVertices > 0)
        {
            var vertexSize = TotalVertices * Marshal.SizeOf<Data.RenderingData.Vertex>();

            VertexBufferHandle = _bufferManager.AllocateBuffer(
                (ulong)vertexSize,
                BufferUsageFlags.VertexBufferBit
                | BufferUsageFlags.StorageBufferBit // For bindless access
                | BufferUsageFlags.TransferDstBit, // For uploading
                MemoryUsage.AutoPreferDevice);

            _vertexBuffer = _bufferManager.GetBuffer(VertexBufferHandle);
            Console.WriteLine($"✓ Vertex buffer allocated: {vertexSize / (1024 * 1024)} MB");
        }

        // Allocate index buffer
        if (TotalIndices > 0)
        {
            var indexSize = TotalIndices * sizeof(uint);

            IndexBufferHandle = _bufferManager.AllocateBuffer(
                indexSize,
                BufferUsageFlags.IndexBufferBit
                | BufferUsageFlags.StorageBufferBit // For bindless access
                | BufferUsageFlags.TransferDstBit, // For uploading
                MemoryUsage.AutoPreferDevice);

            _indexBuffer = _bufferManager.GetBuffer(IndexBufferHandle);
            Console.WriteLine($"✓ Index buffer allocated: {indexSize / (1024 * 1024)} MB");
        }

        if (_meshlets.Count > 0)
        {
            var meshletSize = (ulong)(_meshlets.Count * Marshal.SizeOf<GPUMeshlet>());
            MeshletBufferHandle = _bufferManager.AllocateBuffer(
                meshletSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.AutoPreferDevice);
            MeshletBuffer = _bufferManager.GetBuffer(MeshletBufferHandle);

            var meshletVertexIndexSize = (ulong)(_meshletVertexIndices.Count * sizeof(uint));
            MeshletVertexIndicesBufferHandle = _bufferManager.AllocateBuffer(
                meshletVertexIndexSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.AutoPreferDevice);
            MeshletVertexIndicesBuffer = _bufferManager.GetBuffer(MeshletVertexIndicesBufferHandle);

            var meshletTriangleIndexSize = (ulong)(_meshletTriangleIndices.Count * sizeof(uint));
            MeshletTriangleIndicesBufferHandle = _bufferManager.AllocateBuffer(
                meshletTriangleIndexSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.AutoPreferDevice);
            MeshletTriangleIndicesBuffer = _bufferManager.GetBuffer(MeshletTriangleIndicesBufferHandle);
        }

        _finalized = true;
    }

    /// <summary>
    ///     Get mesh entry (offset + counts) for a given mesh.
    /// </summary>
    public MeshEntry GetMeshEntry(Data.RenderingData.Mesh mesh)
    {
        if (!_meshes.TryGetValue(mesh, out var entry))
            throw new KeyNotFoundException($"Mesh '{mesh?.Name}' not found in MeshBuffer");
        return entry;
    }

    /// <summary>
    ///     Get mesh offsets as GPUMeshData struct (for scene buffer upload).
    /// </summary>
    public GPUMeshData GetGPUMeshData(Data.RenderingData.Mesh mesh)
    {
        var entry = GetMeshEntry(mesh);
        return new GPUMeshData
        {
            VertexBufferIndex = 0, // Bindless index (will be set by BindlessDescriptorHeap)
            IndexBufferIndex = 1, // Bindless index
            VertexCount = entry.VertexCount,
            IndexCount = entry.IndexCount,
            BoundingBoxMin = new Vector3(-1),
            BoundingBoxMax = new Vector3(1),
            MeshletOffset = entry.MeshletOffset,
            MeshletCount = entry.MeshletCount
        };
    }

    /// <summary>
    ///     Bind consolidated vertex and index buffers to command buffer.
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
    ///     Draw a mesh using consolidated buffers with offset tracking.
    /// </summary>
    public void DrawMesh(CommandBuffer cmd, Data.RenderingData.Mesh mesh)
    {
        if (!_finalized)
            throw new InvalidOperationException("MeshBuffer not finalized (Draw)");

        var entry = GetMeshEntry(mesh);

        if (entry.IndexCount > 0)
            _vk.CmdDrawIndexed(cmd, entry.IndexCount, 1, entry.IndexOffset, (int)entry.VertexOffset, 0);
    }

    /// <summary>
    ///     Upload mesh data from CPU to GPU via staging buffer.
    ///     Typically called once per mesh after finalization.
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
            uploadRing.WriteData<Data.RenderingData.Vertex>(mesh.Vertices, out var vertexSrcOffset);

            var srcBuffer = uploadRing.CurrentUploadBuffer;
            var dstBuffer = _vertexBuffer;
            var vertexSize = (ulong)(entry.VertexCount * Marshal.SizeOf<Data.RenderingData.Vertex>());
            var dstOffset = entry.VertexOffset * (ulong)Marshal.SizeOf<Data.RenderingData.Vertex>();

            RecordCopyCommand(transferCmd, srcBuffer, dstBuffer, vertexSrcOffset, dstOffset, vertexSize);
        }

        // Write index data to staging buffer
        if (mesh.Indices.Length > 0)
        {
            uploadRing.WriteData<uint>(mesh.Indices, out var indexSrcOffset);

            var srcBuffer = uploadRing.CurrentUploadBuffer;
            var dstBuffer = _indexBuffer;
            var indexSize = (ulong)(entry.IndexCount * sizeof(uint));
            var dstOffset = entry.IndexOffset * sizeof(uint);

            RecordCopyCommand(transferCmd, srcBuffer, dstBuffer, indexSrcOffset, dstOffset, indexSize);
        }

        UploadMeshletData(transferCmd, uploadRing);

        Console.WriteLine($"✓ Uploaded mesh '{mesh.Name}' to GPU buffers");
    }

    /// <summary>
    ///     Helper: Record a buffer copy command.
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

    private void BuildMeshlets()
    {
        _meshlets.Clear();
        _meshletVertexIndices.Clear();
        _meshletTriangleIndices.Clear();

        foreach (var kvp in _meshes)
        {
            var mesh = kvp.Key;
            var entry = kvp.Value;
            var indices = mesh.Indices;
            var vertices = mesh.Vertices;

            var meshletOffset = (uint)_meshlets.Count;
            var meshletCount = 0u;

            var triCount = indices.Length / 3;
            var triIndex = 0;
            while (triIndex < triCount)
            {
                var localMap = new Dictionary<uint, uint>();
                var localVerts = new List<uint>();
                var localTris = new List<uint>();

                var boundsMin = new Vector3(float.MaxValue);
                var boundsMax = new Vector3(float.MinValue);
                var normalSum = new Vector3(0);
                var triNormals = new List<Vector3>();

                while (triIndex < triCount && localTris.Count / 3 < MaxPrimsPerMeshlet)
                {
                    var baseIndex = triIndex * 3;
                    var i0 = indices[baseIndex + 0];
                    var i1 = indices[baseIndex + 1];
                    var i2 = indices[baseIndex + 2];

                    var neededNew = 0;
                    if (!localMap.ContainsKey(i0)) neededNew++;
                    if (!localMap.ContainsKey(i1)) neededNew++;
                    if (!localMap.ContainsKey(i2)) neededNew++;
                    if (localVerts.Count + neededNew > MaxVertsPerMeshlet)
                        break;

                    var l0 = GetOrAddLocalVertex(i0, localMap, localVerts, vertices, ref boundsMin, ref boundsMax);
                    var l1 = GetOrAddLocalVertex(i1, localMap, localVerts, vertices, ref boundsMin, ref boundsMax);
                    var l2 = GetOrAddLocalVertex(i2, localMap, localVerts, vertices, ref boundsMin, ref boundsMax);

                    localTris.Add(l0);
                    localTris.Add(l1);
                    localTris.Add(l2);

                    var p0 = vertices[i0].Position;
                    var p1 = vertices[i1].Position;
                    var p2 = vertices[i2].Position;
                    var n = Vector3.Cross(p1 - p0, p2 - p0);
                    if (n.LengthSquared() > 0.0f)
                    {
                        n = Vector3.Normalize(n);
                        triNormals.Add(n);
                        normalSum += n;
                    }

                    triIndex++;
                }

                if (localTris.Count == 0)
                    break;

                var vertexOffset = (uint)_meshletVertexIndices.Count;
                var triangleOffset = (uint)_meshletTriangleIndices.Count;

                _meshletVertexIndices.AddRange(localVerts);
                _meshletTriangleIndices.AddRange(localTris);

                var coneAxis = normalSum.LengthSquared() > 0.0f
                    ? Vector3.Normalize(normalSum)
                    : new Vector3(0, 0, 1);
                var coneCutoff = -1.0f;
                if (triNormals.Count > 0)
                {
                    var maxAngle = 0.0f;
                    foreach (var n in triNormals)
                    {
                        var dot = Math.Clamp(Vector3.Dot(coneAxis, n), -1.0f, 1.0f);
                        var angle = MathF.Acos(dot);
                        if (angle > maxAngle)
                            maxAngle = angle;
                    }

                    coneCutoff = MathF.Cos(maxAngle);
                }

                _meshlets.Add(new GPUMeshlet
                {
                    VertexOffset = vertexOffset,
                    VertexCount = (uint)localVerts.Count,
                    TriangleOffset = triangleOffset,
                    TriangleCount = (uint)(localTris.Count / 3),
                    BoundsMin = new Vector4(boundsMin, 0),
                    BoundsMax = new Vector4(boundsMax, 0),
                    ConeAxis = new Vector4(coneAxis, 0),
                    ConeData = new Vector4(coneCutoff, 0, 0, 0)
                });

                meshletCount++;
            }

            entry.MeshletOffset = meshletOffset;
            entry.MeshletCount = meshletCount;
        }
    }

    private unsafe void UploadMeshletData(CommandBuffer transferCmd, FrameUploadRing uploadRing)
    {
        if (_meshletDataUploaded || _meshlets.Count == 0)
            return;

        uploadRing.WriteData<GPUMeshlet>(_meshlets.ToArray(), out var meshletSrcOffset);
        uploadRing.WriteData<uint>(_meshletVertexIndices.ToArray(), out var meshletVertexIndexSrcOffset);
        uploadRing.WriteData<uint>(_meshletTriangleIndices.ToArray(), out var meshletTriangleIndexSrcOffset);

        var srcBuffer = uploadRing.CurrentUploadBuffer;

        var meshletSize = (ulong)(_meshlets.Count * Marshal.SizeOf<GPUMeshlet>());
        var meshletCopy = new BufferCopy
        {
            SrcOffset = meshletSrcOffset,
            DstOffset = 0,
            Size = meshletSize
        };
        _vk.CmdCopyBuffer(transferCmd, srcBuffer, MeshletBuffer, 1, &meshletCopy);

        var meshletVertexIndexSize = (ulong)(_meshletVertexIndices.Count * sizeof(uint));
        var meshletVertexIndexCopy = new BufferCopy
        {
            SrcOffset = meshletVertexIndexSrcOffset,
            DstOffset = 0,
            Size = meshletVertexIndexSize
        };
        _vk.CmdCopyBuffer(transferCmd, srcBuffer, MeshletVertexIndicesBuffer, 1, &meshletVertexIndexCopy);

        var meshletTriangleIndexSize = (ulong)(_meshletTriangleIndices.Count * sizeof(uint));
        var meshletTriangleIndexCopy = new BufferCopy
        {
            SrcOffset = meshletTriangleIndexSrcOffset,
            DstOffset = 0,
            Size = meshletTriangleIndexSize
        };
        _vk.CmdCopyBuffer(transferCmd, srcBuffer, MeshletTriangleIndicesBuffer, 1, &meshletTriangleIndexCopy);

        _meshletDataUploaded = true;
    }

    private static uint GetOrAddLocalVertex(
        uint globalIndex,
        Dictionary<uint, uint> localMap,
        List<uint> localVerts,
        Data.RenderingData.Vertex[] vertices,
        ref Vector3 boundsMin,
        ref Vector3 boundsMax)
    {
        if (localMap.TryGetValue(globalIndex, out var localIndex))
            return localIndex;

        localIndex = (uint)localVerts.Count;
        localMap[globalIndex] = localIndex;
        localVerts.Add(globalIndex);

        var v = vertices[globalIndex].Position;
        boundsMin = Vector3.Min(boundsMin, v);
        boundsMax = Vector3.Max(boundsMax, v);

        return localIndex;
    }

    private static float ComputeBoundsRadius(Data.RenderingData.Mesh mesh)
    {
        var min = mesh.BoundingBoxMin;
        var max = mesh.BoundingBoxMax;
        var extent = (max - min) * 0.5f;
        return extent.Length();
    }

    // CPU-side mesh registry
    public class MeshEntry
    {
        public uint VertexOffset { get; set; } // Offset in vertex buffer (in vertices)
        public uint IndexOffset { get; set; } // Offset in index buffer (in indices)
        public uint VertexCount { get; set; }
        public uint IndexCount { get; set; }
        public uint MeshletOffset { get; set; }
        public uint MeshletCount { get; set; }
        public float BoundsRadius { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUMeshlet
    {
        public uint VertexOffset;
        public uint VertexCount;
        public uint TriangleOffset;
        public uint TriangleCount;
        public Vector4 BoundsMin;
        public Vector4 BoundsMax;
        public Vector4 ConeAxis;
        public Vector4 ConeData;
    }
}