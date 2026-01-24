//SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using Silk.NET.Vulkan;
using Njulf_Framework.Rendering.Core;
using Njulf_Framework.Rendering.Resources;

namespace Njulf_Framework.Rendering.Data;

/// <summary>
/// GPU-side light data structure. Matches compute shader layout.
/// Must be POD (Plain Old Data) and tightly packed.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct GPULight
{
    /// <summary>
    /// Light position in world space (xyz), radius (w).
    /// </summary>
    public Vector4 PositionRadius;

    /// <summary>
    /// Light color (xyz), intensity (w).
    /// </summary>
    public Vector4 ColorIntensity;

    /// <summary>
    /// Light type: 0 = point, 1 = spot, 2 = directional.
    /// </summary>
    public uint LightType;

    /// <summary>
    /// Unused padding for alignment.
    /// </summary>
    public uint Padding1;
    public uint Padding2;
    public uint Padding3;

    /// <summary>
    /// Helper to create a point light.
    /// </summary>
    public static GPULight CreatePointLight(Vector3 position, float radius, Vector3 color, float intensity)
    {
        return new GPULight
        {
            PositionRadius = new Vector4(position, radius),
            ColorIntensity = new Vector4(color, intensity),
            LightType = 0  // Point light
        };
    }
}

/// <summary>
/// Per-tile light list header. Points to light indices in shared buffer.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct TiledLightHeader
{
    /// <summary>
    /// Offset into the global light index buffer for this tile's first light.
    /// </summary>
    public uint LightListOffset;

    /// <summary>
    /// Number of lights affecting this tile.
    /// </summary>
    public uint LightCount;
}