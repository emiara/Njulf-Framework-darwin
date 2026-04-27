using System.Collections.Generic;
using NjulfFramework.Core.Interfaces.Rendering;

namespace NjulfFramework.Core.Interfaces.Scene
{
    /// <summary>
    /// Interface for scene management
    /// </summary>
    public interface IScene
    {
        /// <summary>
        /// Collection of renderable objects in the scene
        /// </summary>
        IEnumerable<IRenderable> Renderables { get; }

        /// <summary>
        /// Add a renderable object to the scene
        /// </summary>
        /// <param name="renderable">Renderable object to add</param>
        void AddRenderable(IRenderable renderable);

        /// <summary>
        /// Remove a renderable object from the scene
        /// </summary>
        /// <param name="renderable">Renderable object to remove</param>
        void RemoveRenderable(IRenderable renderable);
    }
}