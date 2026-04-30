using System.Numerics;
using NjulfFramework.Core.Enums;

namespace NjulfFramework.Core.Interfaces.Rendering
{
    /// <summary>
    /// Interface for material definitions
    /// </summary>
    public interface IMaterial
    {
        string Name { get; }
        string ShaderPath { get; }

        // PBR Metallic-Roughness
        Vector4 BaseColorFactor { get; }
        string  BaseColorTexturePath { get; }
        float   MetallicFactor { get; }
        float   RoughnessFactor { get; }
        string  MetallicRoughnessTexturePath { get; }

        // Additional maps
        string NormalTexturePath { get; }
        float  NormalScale { get; }
        string OcclusionTexturePath { get; }
        float  OcclusionStrength { get; }
        string EmissiveTexturePath { get; }
        Vector3 EmissiveFactor { get; }

        // Rendering properties
        AlphaMode AlphaMode { get; }
        float     AlphaCutoff { get; }
        bool      DoubleSided { get; }
    }
}