using System;
using System.Threading;
using System.Threading.Tasks;

namespace NjulfFramework.Core.Interfaces.Assets
{
    /// <summary>
    /// Event arguments for asset loading progress
    /// </summary>
    public class AssetLoadProgress : EventArgs
    {
        public string FilePath { get; set; }
        public float Progress { get; set; }
        public string Status { get; set; }
    }

    /// <summary>
    /// Interface for asset loading
    /// </summary>
    public interface IAssetLoader : IDisposable
    {
        /// <summary>
        /// Load a 3D model asynchronously
        /// </summary>
        /// <param name="filePath">Path to the model file</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task containing the loaded model</returns>
        Task<IModel> LoadModelAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get a cached model if available
        /// </summary>
        /// <param name="filePath">Path to the model file</param>
        /// <returns>Cached model or null</returns>
        IModel GetCachedModel(string filePath);

        /// <summary>
        /// Clear the asset cache
        /// </summary>
        void ClearCache();

        /// <summary>
        /// Event for loading progress updates
        /// </summary>
        event EventHandler<AssetLoadProgress> LoadProgress;
    }
}