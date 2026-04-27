// SPDX-License-Identifier: MPL-2.0

using NjulfFramework.Core.Interfaces.Rendering;

namespace NjulfFramework.Core.Interfaces.Rendering;

/// <summary>
/// Interface for mesh management
/// </summary>
public interface IMeshManager : IDisposable
{
    /// <summary>
    /// Register a mesh by name with its vertex and index data.
    /// </summary>
    /// <param name="name">Unique mesh name</param>
    /// <param name="vertices">Raw vertex buffer bytes</param>
    /// <param name="indices">Index data</param>
    void RegisterMesh(string name, ReadOnlySpan<byte> vertices, ReadOnlySpan<uint> indices);

    /// <summary>
    /// Finalize all pending mesh registrations and upload to the GPU.
    /// Must be called before any mesh is rendered.
    /// </summary>
    void Finalize();

    /// <summary>
    /// Try to retrieve the GPU descriptor for a registered mesh.
    /// </summary>
    /// <param name="name">Mesh name</param>
    /// <param name="descriptor">GPU descriptor, or <c>null</c> if not found</param>
    /// <returns><c>true</c> if the mesh was found</returns>
    bool TryGetMeshDescriptor(string name, out IMeshDescriptor? descriptor);
}
