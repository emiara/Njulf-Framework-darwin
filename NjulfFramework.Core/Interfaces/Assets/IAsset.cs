using System;

namespace NjulfFramework.Core.Interfaces.Assets
{
    /// <summary>
    /// Base interface for all assets
    /// </summary>
    public interface IAsset : IDisposable
    {
        /// <summary>
        /// Name of the asset
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Source path of the asset
        /// </summary>
        string SourcePath { get; }
    }
}