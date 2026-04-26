// SPDX-License-Identifier: MPL-2.0

using NjulfFramework.Assets.Interfaces;
using NjulfFramework.Assets.Models;
using Silk.NET.Assimp;
using File = System.IO.File;

namespace NjulfFramework.Assets;

/// <summary>
///     Main asset loader implementation
/// </summary>
public class AssetLoader : IAssetLoader
{
    private readonly AssetCache _assetCache;
    private readonly AssimpImporter _importer;
    private readonly ModelProcessor _processor;

    /// <summary>
    ///     Constructor
    /// </summary>
    public AssetLoader(AssetCache assetCache, AssimpImporter importer, ModelProcessor processor)
    {
        _assetCache = assetCache;
        _importer = importer;
        _processor = processor;
    }

    public event EventHandler<AssetLoadProgress> LoadProgress;

    /// <summary>
    ///     Load a 3D model asynchronously
    /// </summary>
    public async Task<FrameworkModel> LoadModelAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Model file not found", filePath);

        cancellationToken.ThrowIfCancellationRequested();

        var cachedModel = _assetCache.GetCachedModel(filePath);
        if (cachedModel != null)
            return cachedModel;

        OnLoadProgress(new AssetLoadProgress { FilePath = filePath, Progress = 0.1f, Status = "Importing" });

        var assimpScenePtr = await _importer.ImportSceneAsync(filePath);

        cancellationToken.ThrowIfCancellationRequested();

        OnLoadProgress(new AssetLoadProgress { FilePath = filePath, Progress = 0.3f, Status = "Processing" });

        // Process scene
        unsafe
        {
            var assimpScene = (Scene*)assimpScenePtr;
            var frameworkModel = _processor.ProcessScene(assimpScene, filePath);
            OnLoadProgress(new AssetLoadProgress { FilePath = filePath, Progress = 0.8f, Status = "Caching" });

            // Cache the model
            _assetCache.CacheAsset(filePath, frameworkModel);
            OnLoadProgress(new AssetLoadProgress { FilePath = filePath, Progress = 1.0f, Status = "Complete" });

            return frameworkModel;
        }
    }

    /// <summary>
    ///     Get a cached model if available
    /// </summary>
    public FrameworkModel GetCachedModel(string filePath)
    {
        return _assetCache.GetCachedModel(filePath);
    }

    /// <summary>
    ///     Clear the asset cache
    /// </summary>
    public void ClearCache()
    {
        _assetCache.Clear();
    }


    public void Dispose()
    {
        _importer.Dispose();
    }

    /// <summary>
    ///     Raise load progress event
    /// </summary>
    protected virtual void OnLoadProgress(AssetLoadProgress progress)
    {
        LoadProgress?.Invoke(this, progress);
    }
}