// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Numerics;
using NjulfFramework.Assets.Models;
using NjulfFramework.Rendering.Data;

namespace NjulfFramework.Assets;

/// <summary>
/// Bridge between Assets and Rendering systems
/// </summary>
public class RendererAdapter
{
    /// <summary>
    /// Constructor
    /// </summary>
    public RendererAdapter()
    {
    }

    /// <summary>
    /// Convert framework model to renderable objects
    /// </summary>
    public List<RenderingData.RenderObject> ConvertToRenderObjects(FrameworkModel frameworkModel)
    {
        if (frameworkModel == null)
            throw new ArgumentNullException(nameof(frameworkModel));

        var renderObjects = new List<RenderingData.RenderObject>();


        // Convert each mesh to a render object
        for (var meshIndex = 0; meshIndex < frameworkModel.Meshes.Count; meshIndex++)
        {
            var frameworkMesh = frameworkModel.Meshes[meshIndex];
            var materialIndex = frameworkMesh.MaterialIndex;

            // Ensure material index is valid
            if (materialIndex < 0 || materialIndex >= frameworkModel.Materials.Count) materialIndex = 0;

            var frameworkMaterial = frameworkModel.Materials[materialIndex];

            // Convert mesh
            var renderingMesh = ConvertMesh(frameworkMesh);

            // Convert material
            var renderingMaterial = ConvertMaterial(frameworkMaterial);

            // Create render object
            var renderObject = new RenderingData.RenderObject(frameworkMesh.Name, renderingMesh, renderingMaterial,
                Matrix4x4.Identity);
            renderObjects.Add(renderObject);
        }


        return renderObjects;
    }

    /// <summary>
    /// Convert framework mesh to rendering mesh
    /// </summary>
    private RenderingData.Mesh ConvertMesh(FrameworkMesh frameworkMesh)
    {
        // Convert vertices
        var vertices = new RenderingData.Vertex[frameworkMesh.Vertices.Length];
        for (var i = 0; i < frameworkMesh.Vertices.Length; i++)
        {
            var frameworkVertex = frameworkMesh.Vertices[i];
            vertices[i] = new RenderingData.Vertex(
                frameworkVertex.Position,
                frameworkVertex.Normal,
                frameworkVertex.TexCoord
            );
        }

        // Create rendering mesh
        return new RenderingData.Mesh(
            frameworkMesh.Name,
            vertices,
            frameworkMesh.Indices,
            frameworkMesh.BoundingBoxMin,
            frameworkMesh.BoundingBoxMax
        );
    }

    /// <summary>
    /// Convert framework material to rendering material
    /// </summary>
    private RenderingData.Material ConvertMaterial(FrameworkMaterial frameworkMaterial)
    {
        // Create rendering material with default shader
        var renderingMaterial = new RenderingData.Material(
            frameworkMaterial.Name,
            "Shaders/forward_plus.frag",
            frameworkMaterial.BaseColorTexturePath
        );

        // Copy PBR properties
        renderingMaterial.BaseColorFactor = frameworkMaterial.BaseColorFactor;
        renderingMaterial.MetallicFactor = frameworkMaterial.MetallicFactor;
        renderingMaterial.RoughnessFactor = frameworkMaterial.RoughnessFactor;
        renderingMaterial.MetallicRoughnessTexturePath = frameworkMaterial.MetallicRoughnessTexturePath;
        renderingMaterial.NormalTexturePath = frameworkMaterial.NormalTexturePath;
        renderingMaterial.NormalScale = frameworkMaterial.NormalScale;
        renderingMaterial.OcclusionTexturePath = frameworkMaterial.OcclusionTexturePath;
        renderingMaterial.OcclusionStrength = frameworkMaterial.OcclusionStrength;
        renderingMaterial.EmissiveTexturePath = frameworkMaterial.EmissiveTexturePath;
        renderingMaterial.EmissiveFactor = frameworkMaterial.EmissiveFactor;

        // Copy rendering properties
        renderingMaterial.AlphaMode = (RenderingData.AlphaMode)frameworkMaterial.AlphaMode;
        renderingMaterial.AlphaCutoff = frameworkMaterial.AlphaCutoff;
        renderingMaterial.DoubleSided = frameworkMaterial.DoubleSided;

        return renderingMaterial;
    }

    /// <summary>
    /// Convert framework model to renderable objects with scene hierarchy
    /// </summary>
    public List<RenderingData.RenderObject> ConvertWithHierarchy(FrameworkModel frameworkModel,
        FrameworkModel.SceneNode sceneNode, Matrix4x4 parentTransform)
    {
        var renderObjects = new List<RenderingData.RenderObject>();

        // Calculate current transform
        var currentTransform = sceneNode.Transform * parentTransform;

        // Create render objects for meshes in this node
        foreach (var meshIndex in sceneNode.MeshIndices)
        {
            if (meshIndex < 0 || meshIndex >= frameworkModel.Meshes.Count) continue;

            var frameworkMesh = frameworkModel.Meshes[meshIndex];
            var materialIndex = frameworkMesh.MaterialIndex;

            if (materialIndex < 0 || materialIndex >= frameworkModel.Materials.Count) materialIndex = 0;

            var frameworkMaterial = frameworkModel.Materials[materialIndex];
            var renderingMesh = ConvertMesh(frameworkMesh);
            var renderingMaterial = ConvertMaterial(frameworkMaterial);

            var renderObject = new RenderingData.RenderObject(frameworkMesh.Name, renderingMesh, renderingMaterial,
                currentTransform);
            renderObjects.Add(renderObject);
        }

        // Process child nodes
        foreach (var childNode in sceneNode.Children)
        {
            var childObjects = ConvertWithHierarchy(frameworkModel, childNode, currentTransform);
            renderObjects.AddRange(childObjects);
        }

        return renderObjects;
    }
}