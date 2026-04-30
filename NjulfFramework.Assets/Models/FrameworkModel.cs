// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using System.Collections.Generic;
using NjulfFramework.Core.Enums;
using NjulfFramework.Core.Math;
using NjulfFramework.Core.Interfaces.Assets;
using NjulfFramework.Core.Interfaces.Rendering;

namespace NjulfFramework.Assets.Models;

/// <summary>
///     Framework-agnostic 3D model representation
/// </summary>
public class FrameworkModel : IModel
{
    public string Name { get; set; } = "Unnamed";
    public string SourcePath { get; set; } = string.Empty;
    public List<FrameworkMesh> Meshes { get; } = new();
    IEnumerable<IMaterial> IModel.Materials => Materials;

    IEnumerable<IMesh> IModel.Meshes => Meshes;

    public List<FrameworkMaterial> Materials { get; } = new();
    public SceneNode RootNode { get; set; }

    /// <summary>
    ///     Scene hierarchy node
    /// </summary>
    public class SceneNode
    {
        public string Name { get; set; } = "Node";
        public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
        public List<SceneNode> Children { get; } = new();
        public List<int> MeshIndices { get; } = new();
    }

    public void Dispose()
    {
        // Dispose of materials and meshes
        foreach (var material in Materials)
        {
            // Materials don't have explicit disposal logic yet
        }
        
        foreach (var mesh in Meshes)
        {
            // Meshes don't have explicit disposal logic yet
        }
    }
}

    /// <summary>
    ///     Framework-agnostic mesh representation
    /// </summary>
    public class FrameworkMesh : IMesh
    {
        public string Name { get; set; } = "Mesh";
        public BoundingBox Bounds => new BoundingBox(BoundingBoxMin, BoundingBoxMax);
        public string MaterialName { get; set; } = string.Empty;
        public MeshVertex[] Vertices { get; set; } = Array.Empty<MeshVertex>();   // ← was FrameworkMesh.Vertex[]
        public uint[] Indices { get; set; } = Array.Empty<uint>();
        public Vector3 BoundingBoxMin { get; set; } = Vector3.Zero;
        public Vector3 BoundingBoxMax { get; set; } = Vector3.One;
        public int MaterialIndex { get; set; } = 0;
        public PrimitiveMode PrimitiveMode { get; set; } = PrimitiveMode.Triangles;

    // Removed the inner Vertex struct — use MeshVertex from IMesh instead
}

/// <summary>
///     Framework-agnostic material representation
/// </summary>
public class FrameworkMaterial : IMaterial
{
    public string Name { get; set; } = "Material";
    public string ShaderPath { get; set; } = "Shaders/forward_plus.frag";

        // PBR Properties
    public Vector4 BaseColorFactor { get; set; } = Vector4.One;
    public string BaseColorTexturePath { get; set; } = string.Empty;
    public float MetallicFactor { get; set; } = 1.0f;
    public float RoughnessFactor { get; set; } = 1.0f;
    public string MetallicRoughnessTexturePath { get; set; } = string.Empty;
    public string NormalTexturePath { get; set; } = string.Empty;
    public float NormalScale { get; set; } = 1.0f;
    public string OcclusionTexturePath { get; set; } = string.Empty;
    public float OcclusionStrength { get; set; } = 1.0f;
    public string EmissiveTexturePath { get; set; } = string.Empty;
    public Vector3 EmissiveFactor { get; set; } = Vector3.Zero;

    // Rendering properties
    public AlphaMode AlphaMode { get; set; } = AlphaMode.Opaque;
    public float AlphaCutoff { get; set; } = 0.5f;
    public bool DoubleSided { get; set; } = false;
}