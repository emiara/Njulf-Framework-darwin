// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using System.Runtime.InteropServices;

namespace NjulfFramework.Rendering.Data;

/// <summary>
///     Per-instance data consumed by mesh/fragment shaders in GPU-driven rendering.
///     One entry per visible RenderObject. Indexed by the meshlet's InstanceIndex.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GPUInstanceData
{
    public Matrix4x4 Model;          // 64 bytes
    public uint MaterialIndex;       // 4
    public uint MeshletBaseOffset;   // 4  - first meshlet of this instance's mesh in the global meshlet buffer
    public uint MeshletCount;        // 4
    public uint Pad;                 // 4  -> 80 bytes, 16-byte aligned

    public static uint GetSizeInBytes() => (uint)Marshal.SizeOf<GPUInstanceData>();
}

/// <summary>
///     One per (instance, meshlet) pair. The task shader is dispatched with
///     one workgroup per entry; it culls and emits mesh workgroups.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GPUMeshletDraw
{
    public uint InstanceIndex;   // -> GPUInstanceData
    public uint MeshletIndex;    // -> global GPUMeshlet buffer

    public static uint GetSizeInBytes() => (uint)Marshal.SizeOf<GPUMeshletDraw>();
}