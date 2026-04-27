namespace NjulfFramework.Core.Interfaces.Rendering;

public interface IMeshDescriptor
{
    /// <summary>
    /// Name of the mesh.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Number of indices in the mesh.
    /// </summary>
    uint IndexCount { get; }

    /// <summary>
    /// Byte offset of vertices in the shared vertex buffer.
    /// </summary>
    ulong VertexOffset { get; }

    /// <summary>
    /// First index offset in the shared index buffer.
    /// </summary>
    uint FirstIndex { get; }
}