// SPDX-License-Identifier: MPL-2.0

using NjulfFramework.Core.Interfaces.Assets;

namespace NjulfFramework.Core.Interfaces.Rendering;

/// <summary>
///     Implemented by the renderer. Lets the asset layer push a loaded model
///     into the scene without the asset layer needing a hard reference to
///     <c>VulkanRenderer</c>.
/// </summary>
public interface ISceneLoader
{
    /// <summary>
    ///     Convert a loaded model and add every mesh/material pair as a render object.
    ///     Also re-finalizes GPU mesh buffers so the objects are visible immediately.
    /// </summary>
    void LoadModelIntoScene(IModel model);
}