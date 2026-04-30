// SPDX-License-Identifier: MPL-2.0

using NjulfFramework.Core.Interfaces.Assets;
using NjulfFramework.Core.Interfaces.Rendering;

namespace NjulfFramework.Assets;

/// <summary>
///     Default implementation of <see cref="IContentManager"/>.
///     Supports loading <see cref="IModel"/> assets; more types (audio, textures, …)
///     can be registered via <see cref="RegisterLoader{T}"/>.
/// </summary>
public class ContentManager : IContentManager
{
    private readonly IAssetLoader _assetLoader;
    private readonly ISceneLoader _sceneLoader;

    // Per-type loader delegates — extensible for audio, textures, etc.
    private readonly Dictionary<Type, Func<string, object>> _loaders = new();

    public ContentManager(IAssetLoader assetLoader, ISceneLoader sceneLoader)
    {
        _assetLoader = assetLoader ?? throw new ArgumentNullException(nameof(assetLoader));
        _sceneLoader = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));

        // Built-in: IModel
        _loaders[typeof(IModel)] = path =>
        {
            var model = _assetLoader.LoadModelAsync(path).GetAwaiter().GetResult();
            _sceneLoader.LoadModelIntoScene(model);
            return model;
        };
    }

    /// <summary>
    ///     Register a custom loader for asset type <typeparamref name="T"/>.
    ///     Allows loading audio clips, raw textures, etc.
    /// </summary>
    public void RegisterLoader<T>(Func<string, T> loader) where T : class
        => _loaders[typeof(T)] = path => loader(path);

    /// <inheritdoc/>
    public T Load<T>(string assetPath) where T : class
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            throw new ArgumentException("Asset path must not be empty.", nameof(assetPath));

        var type = typeof(T);

        if (!_loaders.TryGetValue(type, out var loader))
            throw new NotSupportedException(
                $"No loader registered for asset type '{type.Name}'. " +
                $"Register one with ContentManager.RegisterLoader<{type.Name}>().");

        return (T)loader(assetPath);
    }

    /// <inheritdoc/>
    public void Unload(string assetPath) => _assetLoader.ClearCache(); // TODO: targeted eviction

    /// <inheritdoc/>
    public void UnloadAll() => _assetLoader.ClearCache();
}