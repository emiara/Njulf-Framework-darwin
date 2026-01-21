// SPDX-License-Identifier: MPL-2.0

using System;

namespace Njulf_Framework.Rendering.Resources.Handles;

public readonly struct BufferHandle(uint index, uint generation) : IEquatable<BufferHandle>
{
    public readonly uint Index = index;
    public readonly uint Generation = generation;

    public bool IsValid => Generation != 0;

    public static BufferHandle Invalid => default;

    public bool Equals(BufferHandle other) =>
        Index == other.Index && Generation == other.Generation;

    public override bool Equals(object? obj) =>
        obj is BufferHandle other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Index, Generation);

    public static bool operator ==(BufferHandle left, BufferHandle right) => left.Equals(right);
    public static bool operator !=(BufferHandle left, BufferHandle right) => !left.Equals(right);

    public override string ToString() => IsValid ? $"BufferHandle({Index}, gen {Generation})" : "BufferHandle(Invalid)";
}