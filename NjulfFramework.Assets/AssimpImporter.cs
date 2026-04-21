// SPDX-License-Identifier: MPL-2.0

using System;
using System.IO;
using System.Threading.Tasks;
using Silk.NET.Assimp;
using File = System.IO.File;

namespace NjulfFramework.Assets;

/// <summary>
/// Assimp wrapper for model importing
/// </summary>
public class AssimpImporter : IDisposable
{
    private readonly Assimp _assimp;
    private IntPtr? _lastScenePtr;  // Tracks the most recently imported scene

    /// <summary>
    /// Constructor
    /// </summary>
    public AssimpImporter()
    {
        _assimp = Assimp.GetApi();
    }

    /// <summary>
    /// Import a scene asynchronously
    /// </summary>
    public Task<IntPtr> ImportSceneAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Model file not found", filePath);

        return Task.Run(() =>
        {
            unsafe
            {
                var scene = _assimp.ImportFile(filePath,
                    (uint)(PostProcessSteps.Triangulate |
                           PostProcessSteps.GenerateSmoothNormals |
                           PostProcessSteps.CalculateTangentSpace |
                           PostProcessSteps.JoinIdenticalVertices));

                if (scene == null)
                    throw new InvalidOperationException("Failed to import scene: " + _assimp.GetErrorStringS());

                // Store the pointer for later cleanup
                _lastScenePtr = (IntPtr)scene;
                return (IntPtr)scene;
            }
        });
    }

    /// <summary>
    /// Free the last imported scene if it exists
    /// </summary>
    public void FreeLastScene()
    {
        if (_lastScenePtr.HasValue)
        {
            unsafe
            {
                _assimp.FreeScene((Scene*)_lastScenePtr.Value);
            }
            _lastScenePtr = null;
        }
    }

    /// <summary>
    /// Dispose of the importer and free any pending scene
    /// </summary>
    public void Dispose()
    {
        FreeLastScene();
        _assimp?.Dispose();
    }
}
