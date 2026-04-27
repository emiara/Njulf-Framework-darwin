using System;
using System.Threading.Tasks;

namespace NjulfFramework.Core.Interfaces.Rendering
{
    /// <summary>
    /// Interface for the main renderer
    /// </summary>
    public interface IRenderer : IDisposable
    {
        /// <summary>
        /// Initialize the renderer asynchronously
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Load and set up all rendering resources (must be called after window is initialized)
        /// </summary>
        void Load();

        /// <summary>
        /// Render a frame asynchronously
        /// </summary>
        Task RenderFrameAsync();

        /// <summary>
        /// Handle window resize
        /// </summary>
        /// <param name="width">New width</param>
        /// <param name="height">New height</param>
        void Resize(int width, int height);
    }
}