using System.Collections.Generic;
using NjulfFramework.Core.Interfaces.Rendering;

namespace NjulfFramework.Core.Interfaces.Assets
{
    /// <summary>
    /// Interface for 3D model assets
    /// </summary>
    public interface IModel : IAsset
    {
        /// <summary>
        /// Collection of meshes in this model
        /// </summary>
        IEnumerable<IMesh> Meshes { get; }

        /// <summary>
        /// Collection of materials in this model
        /// </summary>
        IEnumerable<IMaterial> Materials { get; }
    }
}