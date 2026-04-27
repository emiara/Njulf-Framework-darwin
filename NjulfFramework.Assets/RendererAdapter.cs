// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using NjulfFramework.Assets.Models;
using NjulfFramework.Core.Interfaces.Conversion;
using NjulfFramework.Core.Interfaces.Rendering;
using NjulfFramework.Core.Interfaces.Assets;
using NjulfFramework.Core.Enums;
using NjulfFramework.Core.Math;

namespace NjulfFramework.Assets;

/// <summary>
///     Bridge between Assets and Rendering systems
/// </summary>
public class RendererAdapter : IModelConverter
{
    private readonly IModelConverter _modelConverter;

    /// <summary>
    ///     Constructor
    /// </summary>
    public RendererAdapter(IModelConverter modelConverter)
    {
        _modelConverter = modelConverter;
    }

    /// <summary>
    ///     Convert framework model to renderable objects
    /// </summary>
    public IEnumerable<IRenderable> ConvertToRenderables(IModel model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        if (model is not FrameworkModel frameworkModel)
            throw new ArgumentException("Model must be a FrameworkModel", nameof(model));

        var renderObjects = new List<IRenderable>();

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
            var renderObject = new RenderObject(frameworkMesh.Name, renderingMesh, renderingMaterial,
                Matrix4x4.Identity);
            renderObjects.Add(renderObject);
        }

        return renderObjects;
    }

    /// <summary>
    ///     Convert framework mesh to rendering mesh
    /// </summary>
    private IMesh ConvertMesh(FrameworkMesh frameworkMesh)
    {
        // Convert vertices
        var vertices = new Vertex[frameworkMesh.Vertices.Length];
        for (var i = 0; i < frameworkMesh.Vertices.Length; i++)
        {
            var frameworkVertex = frameworkMesh.Vertices[i];
            vertices[i] = new Vertex(
                frameworkVertex.Position,
                frameworkVertex.Normal,
                frameworkVertex.TexCoord
            );
        }

        // Create rendering mesh
        return new Mesh(
            frameworkMesh.Name,
            vertices,
            Array.ConvertAll(frameworkMesh.Indices, i => (int)i),
            frameworkMesh.BoundingBoxMin,
            frameworkMesh.BoundingBoxMax,
            frameworkMesh.PrimitiveMode
        );
    }

    /// <summary>
    ///     Convert framework material to rendering material
    /// </summary>
    private IMaterial ConvertMaterial(FrameworkMaterial frameworkMaterial)
    {
        // Create rendering material with default shader
        var renderingMaterial = new Material(
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
        renderingMaterial.AlphaMode = (AlphaMode)frameworkMaterial.AlphaMode;
        renderingMaterial.AlphaCutoff = frameworkMaterial.AlphaCutoff;
        renderingMaterial.DoubleSided = frameworkMaterial.DoubleSided;

        return renderingMaterial;
    }

    /// <summary>
    ///     Convert framework model to renderable objects with scene hierarchy
    /// </summary>
    public IEnumerable<IRenderable> ConvertWithHierarchy(IModel model, FrameworkModel.SceneNode sceneNode, Matrix4x4 parentTransform)
    {
        if (model is not FrameworkModel frameworkModel)
            throw new ArgumentException("Model must be a FrameworkModel", nameof(model));

        var renderObjects = new List<IRenderable>();

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

            var renderObject = new RenderObject(frameworkMesh.Name, renderingMesh, renderingMaterial,
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

    // Inner classes that implement the rendering interfaces
    private class RenderObject : IRenderable
    {
        public string Name { get; }
        public Matrix4x4 Transform { get; set; }
        public IMesh Mesh { get; }
        public IMaterial Material { get; }

        public RenderObject(string name, IMesh mesh, IMaterial material, Matrix4x4 transform)
        {
            Name = name;
            Mesh = mesh;
            Material = material;
            Transform = transform;
        }

        public void Update(double deltaTime)
        {
            // No update logic needed for basic render objects
        }
    }

    private class Mesh : IMesh
    {
        public string Name { get; }
        public Vertex[] Vertices { get; }
        public int[] Indices { get; }
        public Vector3 BoundingBoxMin { get; }
        public Vector3 BoundingBoxMax { get; }
        public PrimitiveMode PrimitiveMode { get; }
        public string MaterialName { get; }

        public Mesh(string name, Vertex[] vertices, int[] indices, Vector3 boundingBoxMin, Vector3 boundingBoxMax, PrimitiveMode primitiveMode)
        {
            Name = name;
            Vertices = vertices;
            Indices = indices;
            BoundingBoxMin = boundingBoxMin;
            BoundingBoxMax = boundingBoxMax;
            PrimitiveMode = primitiveMode;
            MaterialName = name; // Using mesh name as material name
        }

        public BoundingBox Bounds => new BoundingBox(BoundingBoxMin, BoundingBoxMax);
    }

    private class Material : IMaterial
    {
        public string Name { get; }
        public string ShaderPath { get; }
        public string BaseColorTexturePath { get; }
        public Vector4 BaseColorFactor { get; set; }
        public float MetallicFactor { get; set; }
        public float RoughnessFactor { get; set; }
        public string MetallicRoughnessTexturePath { get; set; }
        public string NormalTexturePath { get; set; }
        public float NormalScale { get; set; }
        public string OcclusionTexturePath { get; set; }
        public float OcclusionStrength { get; set; }
        public string EmissiveTexturePath { get; set; }
        public Vector3 EmissiveFactor { get; set; }
        public AlphaMode AlphaMode { get; set; }
        public float AlphaCutoff { get; set; }
        public bool DoubleSided { get; set; }

        public Material(string name, string shaderPath, string baseColorTexturePath)
        {
            Name = name;
            ShaderPath = shaderPath;
            BaseColorTexturePath = baseColorTexturePath;
        }
    }

    private class Vertex
    {
        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public Vector2 TexCoord { get; }

        public Vertex(Vector3 position, Vector3 normal, Vector2 texCoord)
        {
            Position = position;
            Normal = normal;
            TexCoord = texCoord;
        }
    }
}