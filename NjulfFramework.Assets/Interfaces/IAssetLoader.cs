// SPDX-License-Identifier: MPL-2.0

using System;
using System.Threading.Tasks;
using NjulfFramework.Assets.Models;

namespace NjulfFramework.Assets.Interfaces;

/// <summary>
/// Progress event arguments for asset loading
/// </summary>
public class AssetLoadProgress
{
    public string FilePath { get; set; }
    public float Progress { get; set; } = 0f;
    public string Status { get; set; } = "Loading";
}

/// <summary>
/// Main interface for loading 3D assets
/// </summary>
public interface IAssetLoader : IDisposable
{
    /// <summary>
    /// Load a 3D model asynchronously
    /// </summary>
    /// <param name="filePath">Path to the model file</param>
    /// <returns>Task containing the loaded framework model</returns>
    
    Task<FrameworkModel> LoadModelAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a cached model if available
    /// </summary>
    /// <param name="filePath">Path to the model file</param>
    /// <returns>Cached framework model or null</returns>
    FrameworkModel GetCachedModel(string filePath);

    /// <summary>
    /// Clear the asset cache
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Event for loading progress updates
    /// </summary>
    event EventHandler<AssetLoadProgress> LoadProgress;
}