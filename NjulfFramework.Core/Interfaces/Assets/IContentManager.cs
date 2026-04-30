// SPDX-License-Identifier: MPL-2.0

namespace NjulfFramework.Core.Interfaces.Assets;

/// <summary>
///     MonoGame-style content manager. The user calls <c>Content.Load&lt;T&gt;("path")</c>
///     and the framework resolves the correct loader, converts the result, and — for
///     model assets — submits the data to the renderer automatically.
/// </summary>
public interface IContentManager
{
    /// <summary>
    ///     Load an asset of type <typeparamref name="T"/> from <paramref name="assetPath"/>.
    ///     The first call imports and caches; subsequent calls return the cached instance.
    /// </summary>
    T Load<T>(string assetPath) where T : class;

    /// <summary>
    ///     Evict a single asset from the cache.
    /// </summary>
    void Unload(string assetPath);

    /// <summary>
    ///     Evict all cached assets.
    /// </summary>
    void UnloadAll();
}