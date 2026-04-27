using System.Numerics;

namespace NjulfFramework.Core.Math;

/// <summary>
///     Axis-aligned bounding box (AABB).
/// </summary>
public readonly struct BoundingBox
{
    public Vector3 Min { get; }
    public Vector3 Max { get; }

    public BoundingBox(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;
}