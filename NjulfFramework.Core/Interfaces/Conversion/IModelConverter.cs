using System.Collections.Generic;
using NjulfFramework.Core.Interfaces.Assets;
using NjulfFramework.Core.Interfaces.Rendering;

namespace NjulfFramework.Core.Interfaces.Conversion
{
    /// <summary>
    /// Interface for converting models to renderable objects
    /// </summary>
    public interface IModelConverter
    {
        /// <summary>
        /// Convert a model to renderable objects
        /// </summary>
        /// <param name="model">Model to convert</param>
        /// <returns>Collection of renderable objects</returns>
        IEnumerable<IRenderable> ConvertToRenderables(IModel model);
    }
}