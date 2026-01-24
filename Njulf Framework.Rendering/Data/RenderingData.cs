// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using System.Runtime.InteropServices;

namespace Njulf_Framework.Rendering.Data;

public class RenderingData
{
    /// <summary>
    /// Represents a 3D vertex with position, normal, and UV coordinates.
    /// Must match vertex shader layout exactly.
    /// </summary>
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;

        public Vertex(Vector3 position, Vector3 normal, Vector2 texCoord)
        {
            Position = position;
            Normal = normal;
            TexCoord = texCoord;
        }

        public static uint GetBindingDescription()
        {
            return 0; // Binding index
        }

        public static uint GetSizeInBytes()
        {
            return 32; // 12 (position) + 12 (normal) + 8 (texCoord) = 32 bytes
        }
    }
    
    /// <summary>
    /// Uniform buffer data for transformations.
    /// Sent to GPU once per object per frame.
    /// </summary>
    public struct UniformBufferObject
    {
        public Matrix4x4 Model;
        public Matrix4x4 View;
        public Matrix4x4 Projection;

        public UniformBufferObject(Matrix4x4 model, Matrix4x4 view, Matrix4x4 projection)
        {
            Model = model;
            View = view;
            Projection = projection;
        }

        public static uint GetSizeInBytes()
        {
            return 192; // 3 x 64 bytes = 192 bytes
        }
    }
    
    /// <summary>
    /// Material properties for rendering.
    /// Defines which shader and textures to use.
    /// </summary>
    public class Material : IDisposable
    {
        public string Name { get; set; }
        public string ShaderPath { get; set; }
        public string DiffuseTexturePath { get; set; }
        public Vector4 Color { get; set; } = Vector4.One;

        public Material(string name, string shaderPath, string diffuseTexturePath = "")
        {
            Name = name;
            ShaderPath = shaderPath;
            DiffuseTexturePath = diffuseTexturePath;
        }

        public void Dispose()
        {
            // Cleanup will be handled by texture manager
        }
    }
    
    /// <summary>
    /// Mesh data - vertices and indices.
    /// Immutable after creation.
    /// </summary>
    public class Mesh
    {
        public string Name { get; set; }
        public Vertex[] Vertices { get; private set; }
        public uint[] Indices { get; private set; }
        
        public Vector3 BoundingBoxMin { get; set; }
        public Vector3 BoundingBoxMax { get; set; }

        public Mesh(string name, Vertex[] vertices, uint[] indices, Vector3 boundingBoxMin, Vector3 boundingBoxMax)
        {
            Name = name;
            Vertices = vertices;
            Indices = indices;
            BoundingBoxMin = boundingBoxMin;
            BoundingBoxMax = boundingBoxMax;
        }

        /// <summary>
        /// Create a simple cube mesh for testing.
        /// </summary>
        public static Mesh CreateCube()
        {
            var vertices = new Vertex[]
            {
                // Front face (Z+) - Counter-clockwise when viewed from outside
                new Vertex(new Vector3(-0.5f, -0.5f, 0.5f), Vector3.UnitZ, new Vector2(0, 1)),
                new Vertex(new Vector3(0.5f, -0.5f, 0.5f), Vector3.UnitZ, new Vector2(1, 1)),
                new Vertex(new Vector3(0.5f, 0.5f, 0.5f), Vector3.UnitZ, new Vector2(1, 0)),
                new Vertex(new Vector3(-0.5f, 0.5f, 0.5f), Vector3.UnitZ, new Vector2(0, 0)),

                // Back face (Z-) - Counter-clockwise when viewed from outside
                new Vertex(new Vector3(0.5f, -0.5f, -0.5f), -Vector3.UnitZ, new Vector2(0, 1)),
                new Vertex(new Vector3(-0.5f, -0.5f, -0.5f), -Vector3.UnitZ, new Vector2(1, 1)),
                new Vertex(new Vector3(-0.5f, 0.5f, -0.5f), -Vector3.UnitZ, new Vector2(1, 0)),
                new Vertex(new Vector3(0.5f, 0.5f, -0.5f), -Vector3.UnitZ, new Vector2(0, 0)),

                // Top face (Y+) - Counter-clockwise when viewed from outside
                new Vertex(new Vector3(-0.5f, 0.5f, -0.5f), Vector3.UnitY, new Vector2(0, 1)),
                new Vertex(new Vector3(-0.5f, 0.5f, 0.5f), Vector3.UnitY, new Vector2(0, 0)),
                new Vertex(new Vector3(0.5f, 0.5f, 0.5f), Vector3.UnitY, new Vector2(1, 0)),
                new Vertex(new Vector3(0.5f, 0.5f, -0.5f), Vector3.UnitY, new Vector2(1, 1)),

                // Bottom face (Y-) - Counter-clockwise when viewed from outside
                new Vertex(new Vector3(-0.5f, -0.5f, 0.5f), -Vector3.UnitY, new Vector2(0, 0)),
                new Vertex(new Vector3(-0.5f, -0.5f, -0.5f), -Vector3.UnitY, new Vector2(0, 1)),
                new Vertex(new Vector3(0.5f, -0.5f, -0.5f), -Vector3.UnitY, new Vector2(1, 1)),
                new Vertex(new Vector3(0.5f, -0.5f, 0.5f), -Vector3.UnitY, new Vector2(1, 0)),

                // Right face (X+) - Counter-clockwise when viewed from outside
                new Vertex(new Vector3(0.5f, -0.5f, 0.5f), Vector3.UnitX, new Vector2(0, 1)),
                new Vertex(new Vector3(0.5f, -0.5f, -0.5f), Vector3.UnitX, new Vector2(1, 1)),
                new Vertex(new Vector3(0.5f, 0.5f, -0.5f), Vector3.UnitX, new Vector2(1, 0)),
                new Vertex(new Vector3(0.5f, 0.5f, 0.5f), Vector3.UnitX, new Vector2(0, 0)),

                // Left face (X-) - Counter-clockwise when viewed from outside
                new Vertex(new Vector3(-0.5f, -0.5f, -0.5f), -Vector3.UnitX, new Vector2(0, 1)),
                new Vertex(new Vector3(-0.5f, -0.5f, 0.5f), -Vector3.UnitX, new Vector2(1, 1)),
                new Vertex(new Vector3(-0.5f, 0.5f, 0.5f), -Vector3.UnitX, new Vector2(1, 0)),
                new Vertex(new Vector3(-0.5f, 0.5f, -0.5f), -Vector3.UnitX, new Vector2(0, 0)),
            };

            var indices = new uint[]
            {
                // Front - CCW when viewed from outside
                0, 3, 2, 2, 1, 0,
                // Back - CCW when viewed from outside  
                4, 7, 6, 6, 5, 4,
                // Top - CCW when viewed from outside
                8, 11, 10, 10, 9, 8,
                // Bottom - CCW when viewed from outside
                12, 15, 14, 14, 13, 12,
                // Right - CCW when viewed from outside
                16, 19, 18, 18, 17, 16,
                // Left - CCW when viewed from outside
                20, 23, 22, 22, 21, 20
            };

            return new Mesh("Cube", vertices, indices, new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f));
        }
    }
    
    /// <summary>
    /// Renderable object combining mesh, material, and transform.
    /// </summary>
    public class RenderObject
    {
        public string Name { get; set; }
        public Mesh Mesh { get; set; }
        public Material Material { get; set; }
        public Matrix4x4 Transform { get; set; }
        public bool Visible { get; set; } = true;

        public RenderObject(string name, Mesh mesh, Material material, Matrix4x4 transform = default)
        {
            Name = name;
            Mesh = mesh;
            Material = material;
            Transform = transform == default ? Matrix4x4.Identity : transform;
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct PushConstants
    {
        public Matrix4x4 Model;        // 64 bytes
        public Matrix4x4 View;         // 64 bytes
        public Matrix4x4 Projection;   // 64 bytes

        public uint MaterialIndex;     // 4 bytes
        public uint MeshIndex;         // 4 bytes
        public uint InstanceIndex;     // 4 bytes
        public uint Padding;           // 4 bytes (align to 16)
    }
}