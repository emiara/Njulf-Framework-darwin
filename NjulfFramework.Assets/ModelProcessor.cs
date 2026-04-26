// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using NjulfFramework.Assets.Models;
using Silk.NET.Assimp;

namespace NjulfFramework.Assets;

/// <summary>
///     Processes Assimp scenes into framework models
/// </summary>
public class ModelProcessor
{
    private readonly MaterialConverter _materialConverter;
    private readonly MeshConverter _meshConverter;

    /// <summary>
    ///     Constructor
    /// </summary>
    public ModelProcessor(MeshConverter meshConverter, MaterialConverter materialConverter)
    {
        _meshConverter = meshConverter;
        _materialConverter = materialConverter;
    }

    /// <summary>
    ///     Process an Assimp scene into a framework model
    /// </summary>
    public unsafe FrameworkModel ProcessScene(Scene* scene, string basePath)
    {
        if (scene == null)
            throw new ArgumentNullException(nameof(scene));

        var frameworkModel = new FrameworkModel
        {
            Name = Path.GetFileNameWithoutExtension(basePath)
        };

        // Convert materials
        for (var i = 0; i < scene->MNumMaterials; i++)
        {
            var assimpMaterial = scene->MMaterials[i];
            var frameworkMaterial = _materialConverter.ConvertMaterial(assimpMaterial, basePath);
            frameworkModel.Materials.Add(frameworkMaterial);
        }

        // Convert meshes
        for (var i = 0; i < scene->MNumMeshes; i++)
        {
            var assimpMesh = scene->MMeshes[i];
            var frameworkMesh = _meshConverter.ConvertMesh(assimpMesh, i);
            frameworkModel.Meshes.Add(frameworkMesh);
        }

        // Build scene hierarchy
        frameworkModel.RootNode = BuildSceneHierarchy(scene->MRootNode, scene);

        return frameworkModel;
    }

    /// <summary>
    ///     Build scene hierarchy recursively
    /// </summary>
    private unsafe FrameworkModel.SceneNode BuildSceneHierarchy(Node* assimpNode, Scene* scene)
    {
        var nodeName = assimpNode->MName;

        var frameworkNode = new FrameworkModel.SceneNode
        {
            Name = nodeName.Data != null && nodeName.Length > 0 ? nodeName.AsString : "Node",
            Transform = ConvertMatrix(assimpNode->MTransformation)
        };

        // Add mesh indices for this node
        for (var i = 0; i < assimpNode->MNumMeshes; i++)
        {
            var meshIndex = (int)assimpNode->MMeshes[i];
            if (meshIndex < scene->MNumMeshes) frameworkNode.MeshIndices.Add(meshIndex);
        }

        // Process children recursively
        for (var i = 0; i < assimpNode->MNumChildren; i++)
        {
            var childNode = BuildSceneHierarchy(assimpNode->MChildren[i], scene);
            frameworkNode.Children.Add(childNode);
        }

        return frameworkNode;
    }

    /// <summary>
    ///     Convert Assimp matrix to Vulkan-compatible matrix
    ///     Assimp uses row-major matrices, Vulkan expects column-major
    /// </summary>
    private Matrix4x4 ConvertMatrix(Matrix4x4 assimpMatrix)
    {
        // Assimp stores matrices in row-major order
        // Vulkan shaders expect column-major order
        // System.Numerics.Matrix4x4 is row-major in memory
        // 
        // Transpose converts: row-major -> column-major for Vulkan
        return Matrix4x4.Transpose(assimpMatrix);
    }
}