using System.Collections.Generic;
using System.Numerics;

namespace NjulfFramework.Core.Interfaces.Scene;

/// <summary>
/// Interface for scene node hierarchy
/// </summary>
public interface ISceneNode
{
    /// <summary>
    /// Name of the scene node
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Transformation matrix
    /// </summary>
    Matrix4x4 Transform { get; set; }

    /// <summary>
    /// Parent node
    /// </summary>
    ISceneNode Parent { get; }

    /// <summary>
    /// Child nodes
    /// </summary>
    IEnumerable<ISceneNode> Children { get; }
}
