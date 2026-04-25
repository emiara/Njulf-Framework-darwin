// SPDX-License-Identifier: MPL-2.0

using System;

namespace NjulfFramework.Rendering.Resources.Handles;

public readonly struct BufferHandle(uint index, uint generation) : IEquatable<BufferHandle>
{
    public readonly uint Index = index;
    public readonly uint Generation = generation;

    public bool IsValid => Generation != 0;

    public static BufferHandle Invalid => default;

    public bool Equals(BufferHandle other)
    {
        return Index == other.Index && Generation == other.Generation;
    }

    public override bool Equals(object? obj)
    {
        return obj is BufferHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Index, Generation);
    }

    public static bool operator ==(BufferHandle left, BufferHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BufferHandle left, BufferHandle right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return IsValid ? $"BufferHandle({Index}, gen {Generation})" : "BufferHandle(Invalid)";
    }
}