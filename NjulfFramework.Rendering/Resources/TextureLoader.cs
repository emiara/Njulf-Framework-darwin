// SPDX-License-Identifier: MPL-2.0

using System;
using System.IO;
using Silk.NET.Vulkan;
using StbImageSharp;
using Buffer = System.Buffer;

namespace NjulfFramework.Rendering.Resources;

/// <summary>
/// Texture loading utility using STB Image
/// </summary>
public static class TextureLoader
{
    /// <summary>
    /// Load a 2D texture from file
    /// </summary>
    public static (byte[] Pixels, int Width, int Height, int Components) LoadTextureFromFile(
        string filePath, bool flipVertically = true)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Texture file not found", filePath);

        try
        {
            // Use STB Image Sharp to load the image
            using var stream = File.OpenRead(filePath);
            var result = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            if (result == null)
                throw new InvalidOperationException("Failed to load texture: " + filePath);

            // Convert to byte array
            byte[] pixels;
            if (result.Data != null)
            {
                pixels = new byte[result.Data.Length];
                Buffer.BlockCopy(result.Data, 0, pixels, 0, result.Data.Length);
            }
            else
            {
                // Fallback for null data (shouldn't happen with valid images)
                var pixelCount = result.Width * result.Height * 4;
                pixels = new byte[pixelCount];
                Array.Fill(pixels, (byte)255); // White fallback
            }

            return (pixels, result.Width, result.Height, (int)result.Comp);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to load texture: " + filePath, ex);
        }
    }

    /// <summary>
    /// Get the appropriate Vulkan format for a given number of components
    /// </summary>
    public static Format GetVulkanFormat(int components, bool srgb = false)
    {
        return components switch
        {
            1 => srgb ? Format.R8Srgb : Format.R8Unorm,
            2 => srgb ? Format.R8G8Srgb : Format.R8G8Unorm,
            3 => srgb ? Format.R8G8B8Srgb : Format.R8G8B8Unorm,
            4 => srgb ? Format.R8G8B8A8Srgb : Format.R8G8B8A8Unorm,
            _ => Format.R8G8B8A8Unorm
        };
    }

    /// <summary>
    /// Determine if a texture should use sRGB format based on its type
    /// </summary>
    public static bool ShouldUseSRGB(string texturePath, string textureType)
    {
        // Albedo/base color textures should use sRGB
        // Normal, metallic, roughness, etc. should use linear
        if (textureType.Equals("baseColor", StringComparison.OrdinalIgnoreCase) ||
            textureType.Equals("albedo", StringComparison.OrdinalIgnoreCase) ||
            textureType.Equals("diffuse", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check file extension as additional hint
        var extension = Path.GetExtension(texturePath)?.ToLower();
        if (extension == ".jpg" || extension == ".jpeg")
            return true;

        return false;
    }
}