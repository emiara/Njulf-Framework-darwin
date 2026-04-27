// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using NjulfFramework.Assets.Models;
using NjulfFramework.Core.Enums;
using Silk.NET.Assimp;

namespace NjulfFramework.Assets;

/// <summary>
///     Converts Assimp materials to framework materials
/// </summary>
public class MaterialConverter
{
    // Assimp C API material key strings
    private const string AiMatkeyName = "?mat.name";
    private const string AiMatkeyColorDiffuse = "$clr.diffuse";
    private const string AiMatkeyColorEmissive = "$clr.emissive";
    private const string AiMatkeyOpacity = "$mat.opacity";
    private const string AiMatkeyTwosided = "$mat.twosided";
    private readonly Assimp _assimp;

    /// <summary>
    ///     Constructor
    /// </summary>
    public MaterialConverter()
    {
        _assimp = Assimp.GetApi();
    }

    /// <summary>
    ///     Convert Assimp material to framework material
    /// </summary>
    public unsafe FrameworkMaterial ConvertMaterial(Material* assimpMaterial, string basePath)
    {
        var frameworkMaterial = new FrameworkMaterial
        {
            Name = GetMaterialName(assimpMaterial)
        };

        // Convert PBR properties
        ConvertPBRProperties(assimpMaterial, frameworkMaterial, basePath);

        // Convert alpha properties
        ConvertAlphaProperties(assimpMaterial, frameworkMaterial);

        // Convert double-sided property
        ConvertDoubleSidedProperty(assimpMaterial, frameworkMaterial);

        return frameworkMaterial;
    }

    /// <summary>
    ///     Get material name from Assimp material
    /// </summary>
    private unsafe string GetMaterialName(Material* material)
    {
        AssimpString name = default;
        var result = _assimp.GetMaterialString(material, AiMatkeyName, 0, 0, ref name);
        if (result == Return.Success && name.Length > 0)
            return name.AsString;
        return "Material";
    }

    /// <summary>
    ///     Helper to get a texture path for a given texture type
    /// </summary>
    private unsafe string GetTexturePath(Material* assimpMaterial, TextureType type, string basePath)
    {
        AssimpString texPath = default;
        TextureMapping mapping = default;
        uint uvIndex = 0;
        float blend = 0;
        TextureOp op = default;
        TextureMapMode mapMode = default;
        uint flags = 0;

        var result = _assimp.GetMaterialTexture(
            assimpMaterial, type, 0, ref texPath,
            ref mapping, ref uvIndex, ref blend, ref op, ref mapMode, ref flags);

        if (result == Return.Success)
            return GetAbsoluteTexturePath(texPath.AsString, basePath);

        return string.Empty;
    }

    /// <summary>
    ///     Convert PBR properties
    /// </summary>
    private unsafe void ConvertPBRProperties(Material* assimpMaterial, FrameworkMaterial frameworkMaterial,
        string basePath)
    {
        // Base color (diffuse color)
        var color = new Vector4();
        uint max = 4;
        if (_assimp.GetMaterialFloatArray(assimpMaterial, AiMatkeyColorDiffuse, 0, 0, (float*)&color, ref max) ==
            Return.Success) frameworkMaterial.BaseColorFactor = color;

        // Base color texture (diffuse)
        var texPath = GetTexturePath(assimpMaterial, TextureType.Diffuse, basePath);
        if (!string.IsNullOrEmpty(texPath))
            frameworkMaterial.BaseColorTexturePath = texPath;

        // Normal map
        texPath = GetTexturePath(assimpMaterial, TextureType.Normals, basePath);
        if (!string.IsNullOrEmpty(texPath))
            frameworkMaterial.NormalTexturePath = texPath;

        // Metalness texture
        texPath = GetTexturePath(assimpMaterial, TextureType.Metalness, basePath);
        if (!string.IsNullOrEmpty(texPath))
            frameworkMaterial.MetallicRoughnessTexturePath = texPath;

        // Ambient occlusion texture
        texPath = GetTexturePath(assimpMaterial, TextureType.AmbientOcclusion, basePath);
        if (!string.IsNullOrEmpty(texPath))
            frameworkMaterial.OcclusionTexturePath = texPath;

        // Emissive texture
        texPath = GetTexturePath(assimpMaterial, TextureType.Emissive, basePath);
        if (!string.IsNullOrEmpty(texPath))
            frameworkMaterial.EmissiveTexturePath = texPath;

        // Emissive color
        var emissiveColor = new Vector3();
        max = 3;
        if (_assimp.GetMaterialFloatArray(assimpMaterial, AiMatkeyColorEmissive, 0, 0, (float*)&emissiveColor,
                ref max) == Return.Success) frameworkMaterial.EmissiveFactor = emissiveColor;

        // glTF PBR metallic-roughness workflow properties
        GetGltfPbrMetallicRoughness(assimpMaterial, frameworkMaterial);
    }

    /// <summary>
    ///     Get glTF PBR metallic-roughness properties
    /// </summary>
    private unsafe void GetGltfPbrMetallicRoughness(Material* assimpMaterial, FrameworkMaterial frameworkMaterial)
    {
        // Metallic factor
        var metallicFactor = 1.0f;
        uint maxVal = 1;
        if (_assimp.GetMaterialFloatArray(assimpMaterial, "$mat.gltf.pbrMetallicRoughness.metallicFactor", 0, 0, &metallicFactor, ref maxVal) == Return.Success)
            frameworkMaterial.MetallicFactor = metallicFactor;

        // Roughness factor
        var roughnessFactor = 1.0f;
        if (_assimp.GetMaterialFloatArray(assimpMaterial, "$mat.gltf.pbrMetallicRoughness.roughnessFactor", 0, 0, &roughnessFactor, ref maxVal) == Return.Success)
            frameworkMaterial.RoughnessFactor = roughnessFactor;

        // Base color factor (already handled above)
        // Metallic-roughness texture (already handled above)
    }

    /// <summary>
    ///     Convert alpha properties
    /// </summary>
    private unsafe void ConvertAlphaProperties(Material* assimpMaterial, FrameworkMaterial frameworkMaterial)
    {
        // Alpha cutoff (opacity)
        var opacity = 1.0f;
        uint max = 1;
        if (_assimp.GetMaterialFloatArray(assimpMaterial, AiMatkeyOpacity, 0, 0, &opacity, ref max) == Return.Success)
            if (opacity < 1.0f)
                frameworkMaterial.AlphaMode = AlphaMode.Blend;

        // glTF alpha mode
        var alphaMode = "OPAQUE";
        AssimpString alphaModeStr = default;
        if (_assimp.GetMaterialString(assimpMaterial, "$mat.gltf.alphaMode", 0, 0, ref alphaModeStr) == Return.Success)
            alphaMode = alphaModeStr.AsString;

        // glTF alpha cutoff
        var alphaCutoff = 0.5f;
        if (_assimp.GetMaterialFloatArray(assimpMaterial, "$mat.gltf.alphaCutoff", 0, 0, &alphaCutoff, ref max) == Return.Success)
            frameworkMaterial.AlphaCutoff = alphaCutoff;

        // Set alpha mode based on glTF properties
        if (alphaMode == "MASK")
            frameworkMaterial.AlphaMode = AlphaMode.Mask;
        else if (alphaMode == "BLEND")
            frameworkMaterial.AlphaMode = AlphaMode.Blend;
        else
            frameworkMaterial.AlphaMode = AlphaMode.Opaque;
    }

    /// <summary>
    ///     Convert double-sided property
    /// </summary>
    private unsafe void ConvertDoubleSidedProperty(Material* assimpMaterial, FrameworkMaterial frameworkMaterial)
    {
        var twoSided = 0;
        uint max = 1;
        if (_assimp.GetMaterialIntegerArray(assimpMaterial, AiMatkeyTwosided, 0, 0, &twoSided, ref max) ==
            Return.Success) frameworkMaterial.DoubleSided = twoSided != 0;
    }

    /// <summary>
    ///     Get absolute texture path
    /// </summary>
    private string GetAbsoluteTexturePath(string texturePath, string basePath)
    {
        if (string.IsNullOrEmpty(texturePath))
            return string.Empty;

        if (Path.IsPathRooted(texturePath))
            return texturePath;

        // Relative to base path
        var baseDirectory = Path.GetDirectoryName(basePath);
        return Path.Combine(baseDirectory ?? string.Empty, texturePath);
    }
}