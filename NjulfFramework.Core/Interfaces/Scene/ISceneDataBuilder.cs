// SPDX-License-Identifier: MPL-2.0

using System.Numerics;

namespace NjulfFramework.Core.Interfaces.Scene;

/// <summary>
/// Describes a single renderable object to be submitted to the scene.
/// This is a backend-agnostic descriptor owned by Core.
/// </summary>
public readonly struct RenderObjectDescriptor
{
    /// <summary>Unique name of the mesh to render.</summary>
    public string MeshName { get; init; }

    /// <summary>World-space transform matrix.</summary>
    public Matrix4x4 Transform { get; init; }

    /// <summary>Name of the material to apply, or null for a default.</summary>
    public string? MaterialName { get; init; }
}

/// <summary>
/// Interface for building scene data for rendering.
/// Implementations receive their resource manager dependencies
/// via constructor injection — not through setter methods on this interface.
/// </summary>
public interface ISceneDataBuilder
{
    /// <summary>
    /// Add a renderable object to the scene.
    /// </summary>
    /// <param name="descriptor">Backend-agnostic descriptor of the object to render.</param>
    void AddObject(RenderObjectDescriptor descriptor);

    /// <summary>
    /// Clear all objects, ready for the next frame.
    /// </summary>
    void Clear();

    /// <summary>
    /// Build and submit all accumulated scene data to the GPU.
    /// </summary>
    void BuildSceneData();
}
