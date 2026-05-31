using System.Numerics;
using Silk.NET.Vulkan;

namespace NjulfFramework.Core.Interfaces.Rendering;

/// <summary>
/// Interface for managing lights in the scene
/// </summary>
public interface ILightManager
{
    /// <summary>
    /// Get total light count.
    /// </summary>
    uint LightCount { get; }

    /// <summary>
    /// Bindless descriptor heap index for the GPU light buffer.
    /// </summary>
    uint LightBufferBindlessIndex { get; }

    /// <summary>
    /// Add a point light to the scene.
    /// </summary>
    /// <param name="position">World position of the light</param>
    /// <param name="radius">Light radius/influence distance</param>
    /// <param name="color">Light color (RGB)</param>
    /// <param name="intensity">Light intensity/brightness</param>
    void AddPointLight(Vector3 position, float radius, Vector3 color, float intensity);

    /// <summary>
    /// Remove all lights from the scene.
    /// </summary>
    void Dispose();
}
