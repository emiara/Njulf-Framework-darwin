using System.Numerics;

namespace NjulfFramework.Core.Interfaces.Rendering
{
    /// <summary>
    /// Interface for renderable objects
    /// </summary>
    public interface IRenderable
    {
        /// <summary>
        /// Name of the renderable object
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Transformation matrix
        /// </summary>
        Matrix4x4 Transform { get; set; }

        /// <summary>
        /// Update the renderable object
        /// </summary>
        /// <param name="deltaTime">Time since last update in seconds</param>
        void Update(double deltaTime);
    }
}