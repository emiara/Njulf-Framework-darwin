// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using NjulfFramework.Assets.Models;
using NjulfFramework.Core.Interfaces.Assets;

namespace NjulfFramework.Assets;

/// <summary>
///     Asset cache for performance optimization
/// </summary>
public class AssetCache
{
    private readonly ConcurrentDictionary<string, IModel> _cache = new();
    private readonly ConcurrentDictionary<string, int> _referenceCounts = new();

    /// <summary>
    ///     Constructor
    /// </summary>
    public AssetCache()
    {
    }

    /// <summary>
    ///     Cache an asset
    /// </summary>
    public void CacheAsset(string filePath, IModel model)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (model == null)
            throw new ArgumentNullException(nameof(model));

        _cache[filePath] = model;
        _referenceCounts[filePath] = 1;
    }

    /// <summary>
    ///     Get a cached asset
    /// </summary>
    public IModel GetCachedModel(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (_cache.TryGetValue(filePath, out var model))
        {
            _referenceCounts[filePath]++;
            return model;
        }

        return null;
    }

    /// <summary>
    ///     Release an asset (decrement reference count)
    /// </summary>
    public void ReleaseAsset(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (_referenceCounts.TryGetValue(filePath, out var refCount))
        {
            if (refCount <= 1)
            {
                _referenceCounts.TryRemove(filePath, out _);
                _cache.TryRemove(filePath, out _);
            }
            else
            {
                _referenceCounts[filePath] = refCount - 1;
            }
        }
    }

    /// <summary>
    ///     Clear the cache
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _referenceCounts.Clear();
    }

    /// <summary>
    ///     Get cache statistics
    /// </summary>
    public (int CachedAssets, int TotalReferences) GetCacheStats()
    {
        return (_cache.Count, _referenceCounts.Count);
    }
}