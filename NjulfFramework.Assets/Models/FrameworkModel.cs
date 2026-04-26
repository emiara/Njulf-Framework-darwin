// SPDX-License-Identifier: MPL-2.0

using System.Numerics;

namespace NjulfFramework.Assets.Models;

/// <summary>
///     Framework-agnostic 3D model representation
/// </summary>
public class FrameworkModel
{
    public string Name { get; set; } = "Unnamed";
    public List<FrameworkMesh> Meshes { get; } = new();
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
}

/// <summary>
///     Framework-agnostic mesh representation
/// </summary>
public class FrameworkMesh
{
    public string Name { get; set; } = "Mesh";
    public Vertex[] Vertices { get; set; } = Array.Empty<Vertex>();
    public uint[] Indices { get; set; } = Array.Empty<uint>();
    public Vector3 BoundingBoxMin { get; set; } = Vector3.Zero;
    public Vector3 BoundingBoxMax { get; set; } = Vector3.One;
    public int MaterialIndex { get; set; } = 0;

    /// <summary>
    ///     Vertex structure matching renderer's Vertex format
    /// </summary>
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;

        public Vector2 TexCoord;
        // Additional attributes as needed
    }
}

/// <summary>
///     Framework-agnostic material representation
/// </summary>
public class FrameworkMaterial
{
    public string Name { get; set; } = "Material";

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

/// <summary>
///     Alpha mode for material transparency
/// </summary>
public enum AlphaMode
{
    Opaque, // Fully opaque
    Mask, // Binary transparency with cutoff
    Blend // Alpha blending
}