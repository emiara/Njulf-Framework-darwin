// SPDX-License-Identifier: MPL-2.0

using System;

namespace NjulfFramework.Core.Interfaces.Rendering
{
    /// <summary>
    /// Interface for texture management. Backend-agnostic.
    /// </summary>
    public interface ITextureManager : IDisposable
    {
        /// <summary>
        /// Allocate a texture with raw RGBA data.
        /// </summary>
        /// <param name="width">Texture width in pixels</param>
        /// <param name="height">Texture height in pixels</param>
        /// <param name="data">Raw pixel data (RGBA8 by default)</param>
        /// <returns>An opaque texture handle</returns>
        ITextureHandle AllocateTexture(uint width, uint height, ReadOnlySpan<byte> data);

        /// <summary>
        /// Free a previously allocated texture.
        /// </summary>
        /// <param name="handle">The handle returned by <see cref="AllocateTexture"/></param>
        void FreeTexture(ITextureHandle handle);
    }
}