// SPDX-License-Identifier: MPL-2.0

using System;

namespace Njulf_Framework.Rendering.Resources.Handles;

public readonly struct TextureHandle(uint index, uint generation) : IEquatable<TextureHandle>
{
    public readonly uint Index = index;
    public readonly uint Generation = generation;

    public bool IsValid => Generation != 0;

    public static TextureHandle Invalid => default;

    public bool Equals(TextureHandle other) =>
        Index == other.Index && Generation == other.Generation;

    public override bool Equals(object? obj) =>
        obj is TextureHandle other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Index, Generation);

    public static bool operator ==(TextureHandle left, TextureHandle right) => left.Equals(right);
    public static bool operator !=(TextureHandle left, TextureHandle right) => !left.Equals(right);

    public override string ToString() => IsValid ? $"TextureHandle({Index}, gen {Generation})" : "TextureHandle(Invalid)";
}