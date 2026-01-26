// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
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
    private const uint MaxPrimsPerMeshlet = 64;
    private const uint MaxVertsPerMeshlet = 128;

    private readonly BufferManager _bufferManager;
    private readonly Vk _vk;
    private readonly Device _device;

    // GPU-side consolidated buffers
    private BufferHandle _vertexBufferHandle;
    private BufferHandle _indexBufferHandle;
    private Buffer _vertexBuffer;
    private Buffer _indexBuffer;

    private BufferHandle _meshletBufferHandle;
    private BufferHandle _meshletVertexIndicesBufferHandle;
    private BufferHandle _meshletTriangleIndicesBufferHandle;
    private Buffer _meshletBuffer;
    private Buffer _meshletVertexIndicesBuffer;
    private Buffer _meshletTriangleIndicesBuffer;

    private readonly List<GPUMeshlet> _meshlets = new();
    private readonly List<uint> _meshletVertexIndices = new();
    private readonly List<uint> _meshletTriangleIndices = new();
    private bool _meshletDataUploaded = false;

    // CPU-side mesh registry
    public class MeshEntry
    {
        public uint VertexOffset { get; set; }      // Offset in vertex buffer (in vertices)
        public uint IndexOffset { get; set; }       // Offset in index buffer (in indices)
        public uint VertexCount { get; set; }
        public uint IndexCount { get; set; }
        public uint MeshletOffset { get; set; }
        public uint MeshletCount { get; set; }
        public float BoundsRadius { get; set; }
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
            IndexCount = (uint)(mesh.Indices?.Length ?? 0),
            BoundsRadius = ComputeBoundsRadius(mesh)
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

        BuildMeshlets();

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

        if (_meshlets.Count > 0)
        {
            var meshletSize = (ulong)(_meshlets.Count * System.Runtime.InteropServices.Marshal.SizeOf<GPUMeshlet>());
            _meshletBufferHandle = _bufferManager.AllocateBuffer(
                meshletSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.AutoPreferDevice);
            _meshletBuffer = _bufferManager.GetBuffer(_meshletBufferHandle);

            var meshletVertexIndexSize = (ulong)(_meshletVertexIndices.Count * sizeof(uint));
            _meshletVertexIndicesBufferHandle = _bufferManager.AllocateBuffer(
                meshletVertexIndexSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.AutoPreferDevice);
            _meshletVertexIndicesBuffer = _bufferManager.GetBuffer(_meshletVertexIndicesBufferHandle);

            var meshletTriangleIndexSize = (ulong)(_meshletTriangleIndices.Count * sizeof(uint));
            _meshletTriangleIndicesBufferHandle = _bufferManager.AllocateBuffer(
                meshletTriangleIndexSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.AutoPreferDevice);
            _meshletTriangleIndicesBuffer = _bufferManager.GetBuffer(_meshletTriangleIndicesBufferHandle);
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
            BoundingBoxMax = new System.Numerics.Vector3(1),
            MeshletOffset = entry.MeshletOffset,
            MeshletCount = entry.MeshletCount
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

        UploadMeshletData(transferCmd, uploadRing);

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

    public Buffer MeshletBuffer => _meshletBuffer;
    public Buffer MeshletVertexIndicesBuffer => _meshletVertexIndicesBuffer;
    public Buffer MeshletTriangleIndicesBuffer => _meshletTriangleIndicesBuffer;

    public BufferHandle MeshletBufferHandle => _meshletBufferHandle;
    public BufferHandle MeshletVertexIndicesBufferHandle => _meshletVertexIndicesBufferHandle;
    public BufferHandle MeshletTriangleIndicesBufferHandle => _meshletTriangleIndicesBufferHandle;

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
        _meshlets.Clear();
        _meshletVertexIndices.Clear();
        _meshletTriangleIndices.Clear();
        // BufferManager handles actual VMA deallocation
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct GPUMeshlet
    {
        public uint VertexOffset;
        public uint VertexCount;
        public uint TriangleOffset;
        public uint TriangleCount;
        public System.Numerics.Vector4 BoundsMin;
        public System.Numerics.Vector4 BoundsMax;
        public System.Numerics.Vector4 ConeAxis;
        public System.Numerics.Vector4 ConeData;
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

                var boundsMin = new System.Numerics.Vector3(float.MaxValue);
                var boundsMax = new System.Numerics.Vector3(float.MinValue);
                var normalSum = new System.Numerics.Vector3(0);
                var triNormals = new List<System.Numerics.Vector3>();

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

                    uint l0 = GetOrAddLocalVertex(i0, localMap, localVerts, vertices, ref boundsMin, ref boundsMax);
                    uint l1 = GetOrAddLocalVertex(i1, localMap, localVerts, vertices, ref boundsMin, ref boundsMax);
                    uint l2 = GetOrAddLocalVertex(i2, localMap, localVerts, vertices, ref boundsMin, ref boundsMax);

                    localTris.Add(l0);
                    localTris.Add(l1);
                    localTris.Add(l2);

                    var p0 = vertices[i0].Position;
                    var p1 = vertices[i1].Position;
                    var p2 = vertices[i2].Position;
                    var n = System.Numerics.Vector3.Cross(p1 - p0, p2 - p0);
                    if (n.LengthSquared() > 0.0f)
                    {
                        n = System.Numerics.Vector3.Normalize(n);
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
                    ? System.Numerics.Vector3.Normalize(normalSum)
                    : new System.Numerics.Vector3(0, 0, 1);
                var coneCutoff = -1.0f;
                if (triNormals.Count > 0)
                {
                    var maxAngle = 0.0f;
                    foreach (var n in triNormals)
                    {
                        var dot = Math.Clamp(System.Numerics.Vector3.Dot(coneAxis, n), -1.0f, 1.0f);
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
                    BoundsMin = new System.Numerics.Vector4(boundsMin, 0),
                    BoundsMax = new System.Numerics.Vector4(boundsMax, 0),
                    ConeAxis = new System.Numerics.Vector4(coneAxis, 0),
                    ConeData = new System.Numerics.Vector4(coneCutoff, 0, 0, 0)
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

        uploadRing.WriteData(_meshlets.ToArray(), out var meshletSrcOffset);
        uploadRing.WriteData(_meshletVertexIndices.ToArray(), out var meshletVertexIndexSrcOffset);
        uploadRing.WriteData(_meshletTriangleIndices.ToArray(), out var meshletTriangleIndexSrcOffset);

        var srcBuffer = uploadRing.CurrentUploadBuffer;

        var meshletSize = (ulong)(_meshlets.Count * System.Runtime.InteropServices.Marshal.SizeOf<GPUMeshlet>());
        var meshletCopy = new BufferCopy
        {
            SrcOffset = meshletSrcOffset,
            DstOffset = 0,
            Size = meshletSize
        };
        _vk.CmdCopyBuffer(transferCmd, srcBuffer, _meshletBuffer, 1, &meshletCopy);

        var meshletVertexIndexSize = (ulong)(_meshletVertexIndices.Count * sizeof(uint));
        var meshletVertexIndexCopy = new BufferCopy
        {
            SrcOffset = meshletVertexIndexSrcOffset,
            DstOffset = 0,
            Size = meshletVertexIndexSize
        };
        _vk.CmdCopyBuffer(transferCmd, srcBuffer, _meshletVertexIndicesBuffer, 1, &meshletVertexIndexCopy);

        var meshletTriangleIndexSize = (ulong)(_meshletTriangleIndices.Count * sizeof(uint));
        var meshletTriangleIndexCopy = new BufferCopy
        {
            SrcOffset = meshletTriangleIndexSrcOffset,
            DstOffset = 0,
            Size = meshletTriangleIndexSize
        };
        _vk.CmdCopyBuffer(transferCmd, srcBuffer, _meshletTriangleIndicesBuffer, 1, &meshletTriangleIndexCopy);

        _meshletDataUploaded = true;
    }

    private static uint GetOrAddLocalVertex(
        uint globalIndex,
        Dictionary<uint, uint> localMap,
        List<uint> localVerts,
        Data.RenderingData.Vertex[] vertices,
        ref System.Numerics.Vector3 boundsMin,
        ref System.Numerics.Vector3 boundsMax)
    {
        if (localMap.TryGetValue(globalIndex, out var localIndex))
            return localIndex;

        localIndex = (uint)localVerts.Count;
        localMap[globalIndex] = localIndex;
        localVerts.Add(globalIndex);

        var v = vertices[globalIndex].Position;
        boundsMin = System.Numerics.Vector3.Min(boundsMin, v);
        boundsMax = System.Numerics.Vector3.Max(boundsMax, v);

        return localIndex;
    }

    private static float ComputeBoundsRadius(Data.RenderingData.Mesh mesh)
    {
        var min = mesh.BoundingBoxMin;
        var max = mesh.BoundingBoxMax;
        var extent = (max - min) * 0.5f;
        return extent.Length();
    }
}
