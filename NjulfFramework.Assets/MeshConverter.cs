// SPDX-License-Identifier: MPL-2.0

using System;
using System.Numerics;
using Silk.NET.Assimp;
using NjulfFramework.Assets.Models;

namespace NjulfFramework.Assets;

/// <summary>
/// Converts Assimp meshes to framework meshes
/// </summary>
public class MeshConverter
{
    /// <summary>
    /// Constructor
    /// </summary>
    public MeshConverter()
    {
    }

    /// <summary>
    /// Convert Assimp mesh to framework mesh
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
        var vertices = new FrameworkMesh.Vertex[vertexCount];

        // Get pointer to first texture coordinate channel
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

            vertices[i] = new FrameworkMesh.Vertex
            {
                Position = position,
                Normal = normal,
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

        return frameworkMesh;
    }

    /// <summary>
    /// Calculate bounding box for vertices
    /// </summary>
    private (Vector3 min, Vector3 max) CalculateBoundingBox(FrameworkMesh.Vertex[] vertices)
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