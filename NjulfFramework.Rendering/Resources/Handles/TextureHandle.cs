// SPDX-License-Identifier: MPL-2.0

namespace NjulfFramework.Rendering.Resources.Handles;

public readonly struct TextureHandle(uint index, uint generation) : IEquatable<TextureHandle>
{
    public readonly uint Index = index;
    public readonly uint Generation = generation;

    public bool IsValid => Generation != 0;

    public static TextureHandle Invalid => default;

    public bool Equals(TextureHandle other)
    {
        return Index == other.Index && Generation == other.Generation;
    }

    public override bool Equals(object? obj)
    {
        return obj is TextureHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Index, Generation);
    }

    public static bool operator ==(TextureHandle left, TextureHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TextureHandle left, TextureHandle right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return IsValid ? $"TextureHandle({Index}, gen {Generation})" : "TextureHandle(Invalid)";
    }
}