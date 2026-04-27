using System.Numerics;
using NjulfFramework.Core.Math;

namespace NjulfFramework.Core.Interfaces.Rendering
{
    /// <summary>
    /// Interface for mesh data
    /// </summary>
    public interface IMesh
    {
        /// <summary>
        /// Name of the mesh
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Bounding box of the mesh
        /// </summary>
        BoundingBox Bounds { get; }

        /// <summary>
        /// Material name used by this mesh
        /// </summary>
        string MaterialName { get; }

        /// <summary>
        /// Mesh data access
        /// </summary>
        // Additional mesh data access methods can be added here
    }
}