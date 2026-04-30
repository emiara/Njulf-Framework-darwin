// SPDX-License-Identifier: MPL-2.0

using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using NjulfFramework.Assets.Models;
using NjulfFramework.Core.Enums;
using NjulfFramework.Core.Interfaces.Assets;
using NjulfFramework.Core.Interfaces.Conversion;
using NjulfFramework.Core.Interfaces.Rendering;
using Silk.NET.Assimp;

namespace NjulfFramework.Assets;

    /// <summary>
    ///     Converts Assimp meshes to framework meshes
    /// </summary>
public class MeshConverter : IModelConverter
{
    /// <summary>
    ///     Constructor
    /// </summary>
    public MeshConverter()
    {
    }

    /// <summary>
    ///     Convert a model to renderable objects
    /// </summary>
    /// <param name="model">Model to convert</param>
    /// <returns>Collection of renderable objects</returns>
    public IEnumerable<IRenderable> ConvertToRenderables(IModel model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        var renderables = new List<IRenderable>();
        
        // Convert each mesh in the model to a renderable object
        var meshes = model.Meshes.ToList();
        var materials = model.Materials.ToList();
        for (int i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            // Get the material index from the mesh, default to 0 if not available
            int materialIndex = 0;
            var frameworkMesh = mesh as FrameworkMesh;
            if (frameworkMesh != null)
            {
                materialIndex = frameworkMesh.MaterialIndex;
            }
            
            // Ensure material index is within bounds
            if (materialIndex < 0 || materialIndex >= materials.Count)
                materialIndex = 0;
            
            var material = materials[materialIndex];
            
            var renderObject = new RenderObject
            {
                Name = mesh.Name,
                Mesh = mesh,
                Material = material,
                Transform = Matrix4x4.Identity
            };
            
            renderables.Add(renderObject);
        }
        
        return renderables;
    }

    /// <summary>
    ///     Convert Assimp mesh to framework mesh
    /// </summary>
    public unsafe FrameworkMesh ConvertMesh(Mesh* assimpMesh, int meshIndex)
    {
        if (assimpMesh == null)
            throw new ArgumentNullException(nameof(assimpMesh));

        var meshName = assimpMesh->MName;
        var name = meshName.Length > 0 ? meshName.AsString : "Mesh_" + meshIndex;

        var frameworkMesh = new FrameworkMesh
        {
            Name = name
        };

        // Convert vertices
        var vertexCount = (int)assimpMesh->MNumVertices;
        var vertices = new MeshVertex[vertexCount];

        var texCoords0 = assimpMesh->MTextureCoords.Element0;

        for (var i = 0; i < vertexCount; i++)
        {
            var position = assimpMesh->MVertices[i];
            var normal = assimpMesh->MNormals != null
                ? assimpMesh->MNormals[i]
                : new Vector3(0, 1, 0);

            var texCoord = Vector2.Zero;
            if (texCoords0 != null)
            {
                var tc = texCoords0[i];
                texCoord = new Vector2(tc.X, tc.Y);
            }

            vertices[i] = new MeshVertex
            {
                Position = position,
                Normal   = normal,
                TexCoord = texCoord
            };
        }

        // Convert indices
        var faceCount = (int)assimpMesh->MNumFaces;
        var indices = new uint[faceCount * 3]; // Assuming triangulated
        for (var i = 0; i < faceCount; i++)
        {
            var face = assimpMesh->MFaces[i];
            if (face.MNumIndices != 3)
                throw new InvalidOperationException("Mesh is not triangulated");

            indices[i * 3] = face.MIndices[0];
            indices[i * 3 + 1] = face.MIndices[1];
            indices[i * 3 + 2] = face.MIndices[2];
        }

        // Calculate bounding box
        var (boundingBoxMin, boundingBoxMax) = CalculateBoundingBox(vertices);

        frameworkMesh.Vertices = vertices;
        frameworkMesh.Indices = indices;
        frameworkMesh.BoundingBoxMin = boundingBoxMin;
        frameworkMesh.BoundingBoxMax = boundingBoxMax;
        frameworkMesh.MaterialIndex = (int)assimpMesh->MMaterialIndex;
        frameworkMesh.PrimitiveMode = GetGltfPrimitiveMode(assimpMesh);

        return frameworkMesh;
    }

    /// <summary>
    ///     Get glTF primitive mode
    /// </summary>
    private unsafe PrimitiveMode GetGltfPrimitiveMode(Mesh* assimpMesh)
    {
        // Determine primitive mode from glTF mesh
        // glTF supports points, lines, and triangles
        var primitiveTypes = (PrimitiveType)assimpMesh->MPrimitiveTypes;
        if (primitiveTypes.HasFlag(PrimitiveType.Point))
            return PrimitiveMode.Points;
        if (primitiveTypes.HasFlag(PrimitiveType.Line))
            return PrimitiveMode.Lines;
        return PrimitiveMode.Triangles; // Default
    }

    private class RenderObject : IRenderable
    {
        public string Name { get; set; }
        public Matrix4x4 Transform { get; set; }
        public IMesh Mesh { get; set; }
        public IMaterial Material { get; set; }

        public void Update(double deltaTime)
        {
            // No update logic needed for basic render objects
        }
    }

    /// <summary>
    ///     Calculate bounding box for vertices
    /// </summary>
    private (Vector3 min, Vector3 max) CalculateBoundingBox(MeshVertex[] vertices)
    {
        if (vertices == null || vertices.Length == 0)
            return (Vector3.Zero, Vector3.One);

        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (var vertex in vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        return (min, max);
    }
}