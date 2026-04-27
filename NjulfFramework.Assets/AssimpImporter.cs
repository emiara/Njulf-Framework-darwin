// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Assimp;
using File = System.IO.File;

namespace NjulfFramework.Assets;

/// <summary>
///     Assimp wrapper for model importing
/// </summary>
public class AssimpImporter : IDisposable
{
    private readonly Assimp _assimp;
    private IntPtr? _lastScenePtr; // Tracks the most recently imported scene

    /// <summary>
    ///     Constructor
    /// </summary>
    public AssimpImporter()
    {
        _assimp = Assimp.GetApi();
    }

    /// <summary>
    ///     Dispose of the importer and free any pending scene
    /// </summary>
    public void Dispose()
    {
        FreeLastScene();
        _assimp?.Dispose();
    }

    /// <summary>
    ///     Import a scene asynchronously
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
                // Add glTF-specific post-processing flags
                var flags = (uint)(PostProcessSteps.Triangulate |
                             PostProcessSteps.GenerateSmoothNormals |
                             PostProcessSteps.CalculateTangentSpace |
                             PostProcessSteps.JoinIdenticalVertices);

                // For glTF files, we might want different processing
                if (filePath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
                {
                    // glTF files are typically already triangulated and optimized
                    flags = (uint)(PostProcessSteps.GenerateSmoothNormals |
                                 PostProcessSteps.CalculateTangentSpace);
                }

                var scene = _assimp.ImportFile(filePath, flags);

                if (scene == null)
                    throw new InvalidOperationException("Failed to import scene: " + _assimp.GetErrorStringS());

                // Store the pointer for later cleanup
                _lastScenePtr = (IntPtr)scene;
                return (IntPtr)scene;
            }
        });
    }

    /// <summary>
    ///     Free the last imported scene if it exists
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
}