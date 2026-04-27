# Detailed Changes for glTF Integration

## Asset Importer Changes

### AssimpImporter.cs
```csharp
// Add glTF-specific post-processing flags
public Task<IntPtr> ImportSceneAsync(string filePath)
{
    return Task.Run(() =>
    {
        unsafe
        {
            // Add glTF-specific flags
            var flags = (uint)(PostProcessSteps.Triangulate |
                         PostProcessSteps.GenerateSmoothNormals |
                         PostProcessSteps.CalculateTangentSpace |
                         PostProcessSteps.JoinIdenticalVertices);
            
            // For glTF files, we might want different processing
            if (filePath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
            {
                // glTF files are typically already triangulated and optimized
                flags = (uint)(PostProcessSteps.GenerateSmoothNormals |
                             PostProcessSteps.CalculateTangentSpace);
            }
            
            var scene = _assimp.ImportFile(filePath, flags);
            
            if (scene == null)
                throw new InvalidOperationException("Failed to import scene: " + _assimp.GetErrorStringS());
            
            _lastScenePtr = (IntPtr)scene;
            return (IntPtr)scene;
        }
    });
}
```

## Material System Changes

### MaterialConverter.cs
```csharp
// Enhance material conversion for glTF PBR properties
public FrameworkMaterial ConvertMaterial(Material* assimpMaterial, string basePath)
{
    var material = new FrameworkMaterial();
    
    // Standard glTF PBR properties
    GetPbrMetallicRoughness(assimpMaterial, basePath, material);
    
    // glTF extensions
    GetGltfExtensions(assimpMaterial, material);
    
    // Alpha mode handling
    material.AlphaMode = GetGltfAlphaMode(assimpMaterial);
    
    return material;
}

private void GetPbrMetallicRoughness(Material* material, string basePath, FrameworkMaterial frameworkMaterial)
{
    // Implement glTF metallic-roughness workflow
    // Base color factor
    material->Get(AI_MATKEY_GLTF_PBRMETALLICROUGHNESS_BASE_COLOR_FACTOR, 
                  out Vector4 baseColorFactor);
    frameworkMaterial.BaseColorFactor = baseColorFactor;
    
    // Metallic factor
    material->Get(AI_MATKEY_GLTF_PBRMETALLICROUGHNESS_METALLIC_FACTOR, 
                  out float metallicFactor);
    frameworkMaterial.MetallicFactor = metallicFactor;
    
    // Roughness factor
    material->Get(AI_MATKEY_GLTF_PBRMETALLICROUGHNESS_ROUGHNESS_FACTOR, 
                  out float roughnessFactor);
    frameworkMaterial.RoughnessFactor = roughnessFactor;
    
    // Base color texture
    if (material->GetTexture(aiTextureType_BASE_COLOR, 0, out aiString texturePath) == Return.SUCCESS)
    {
        frameworkMaterial.BaseColorTexture = LoadTexture(texturePath, basePath);
    }
    
    // Metallic-roughness texture
    if (material->GetTexture(aiTextureType_METALNESS, 0, out aiString mrTexturePath) == Return.SUCCESS)
    {
        frameworkMaterial.MetallicRoughnessTexture = LoadTexture(mrTexturePath, basePath);
    }
}
```

## Mesh Data Changes

### MeshConverter.cs
```csharp
// Handle glTF-specific mesh features
public FrameworkMesh ConvertMesh(Mesh* assimpMesh, int meshIndex)
{
    // Existing conversion code...
    
    // Add glTF-specific properties
    frameworkMesh.PrimitiveMode = GetGltfPrimitiveMode(assimpMesh);
    
    // Handle glTF morph targets if present
    if (assimpMesh->MNumAnimMeshes > 0)
    {
        frameworkMesh.MorphTargets = ConvertMorphTargets(assimpMesh);
    }
    
    return frameworkMesh;
}

private PrimitiveMode GetGltfPrimitiveMode(Mesh* mesh)
{
    // Determine primitive mode from glTF mesh
    // glTF supports points, lines, and triangles
    if (mesh->MPrimitiveTypes.HasFlag(PrimitiveType.Point))
        return PrimitiveMode.PointList;
    if (mesh->MPrimitiveTypes.HasFlag(PrimitiveType.Line))
        return PrimitiveMode.LineList;
    return PrimitiveMode.TriangleList; // Default
}
```

## Scene Hierarchy Changes

### ModelProcessor.cs
```csharp
// Enhance scene hierarchy processing for glTF
public FrameworkModel ProcessScene(Scene* scene, string basePath)
{
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
    
    // Build scene hierarchy with glTF-specific features
    frameworkModel.RootNode = BuildSceneHierarchy(scene->MRootNode, scene);
    
    // Process glTF animations if present
    if (scene->MNumAnimations > 0)
    {
        frameworkModel.Animations = ProcessAnimations(scene);
    }
    
    return frameworkModel;
}

private List<FrameworkAnimation> ProcessAnimations(Scene* scene)
{
    var animations = new List<FrameworkAnimation>();
    
    for (var i = 0; i < scene->MNumAnimations; i++)
    {
        var assimpAnim = scene->MAnimations[i];
        var frameworkAnim = new FrameworkAnimation
        {
            Name = assimpAnim->MName.AsString,
            Duration = assimpAnim->MDuration,
            TicksPerSecond = assimpAnim->MTicksPerSecond
        };
        
        // Convert animation channels
        for (var j = 0; j < assimpAnim->MNumChannels; j++)
        {
            var channel = assimpAnim->MChannels[j];
            var frameworkChannel = ConvertAnimationChannel(channel);
            frameworkAnim.Channels.Add(frameworkChannel);
        }
        
        animations.Add(frameworkAnim);
    }
    
    return animations;
}
```

## Texture Handling Changes

### TextureLoader.cs
```csharp
// Enhance texture loading for glTF
public TextureHandle LoadTexture(string texturePath, string basePath, TextureType textureType = TextureType.Diffuse)
{
    // Handle glTF-specific texture types
    switch (textureType)
    {
        case TextureType.BaseColor:
            // Apply sRGB for base color textures
            return LoadTextureInternal(texturePath, basePath, true);
        case TextureType.Normal:
            // Normal maps don't need sRGB
            return LoadTextureInternal(texturePath, basePath, false);
        case TextureType.MetallicRoughness:
            // Metallic-roughness textures don't need sRGB
            return LoadTextureInternal(texturePath, basePath, false);
        // Handle other glTF texture types...
        default:
            return LoadTextureInternal(texturePath, basePath, true);
    }
}

private TextureHandle LoadTextureInternal(string texturePath, string basePath, bool srgb)
{
    // Existing texture loading logic
    // Apply glTF-specific texture settings
    var samplerSettings = new TextureSamplerSettings
    {
        Filter = TextureFilter.Linear, // glTF typically uses linear filtering
        AddressMode = TextureAddressMode.Repeat,
        Anisotropy = 16f
    };
    
    return _textureManager.LoadTexture(texturePath, basePath, samplerSettings, srgb);
}
```

## Rendering Pipeline Changes

### MeshManager.cs
```csharp
// Add support for glTF primitive modes
public void DrawMesh(CommandBuffer cmd, Data.RenderingData.Mesh mesh)
{
    var entry = _meshBuffer.GetMeshEntry(mesh);
    
    // Bind vertex and index buffers
    _meshBuffer.BindBuffers(cmd);
    
    // Set primitive topology based on glTF primitive mode
    var topology = GetVulkanPrimitiveTopology(mesh.PrimitiveMode);
    
    // Draw call
    _vk.CmdDrawIndexed(cmd, mesh.IndexCount, 1, entry.IndexOffset, 0, 0);
}

private PrimitiveTopology GetVulkanPrimitiveTopology(PrimitiveMode mode)
{
    switch (mode)
    {
        case PrimitiveMode.PointList:
            return PrimitiveTopology.PointList;
        case PrimitiveMode.LineList:
            return PrimitiveTopology.LineList;
        case PrimitiveMode.TriangleList:
        default:
            return PrimitiveTopology.TriangleList;
    }
}
```

## Framework Model Extensions

### FrameworkModel.cs
```csharp
// Add glTF-specific properties
public class FrameworkModel
{
    // Existing properties...
    
    public List<FrameworkAnimation> Animations { get; } = new();
    public List<FrameworkSkin> Skins { get; } = new();
    public List<FrameworkCamera> Cameras { get; } = new();
    public List<FrameworkLight> Lights { get; } = new();
}

public class FrameworkAnimation
{
    public string Name { get; set; }
    public double Duration { get; set; }
    public double TicksPerSecond { get; set; }
    public List<FrameworkAnimationChannel> Channels { get; } = new();
}

public class FrameworkSkin
{
    public string Name { get; set; }
    public List<int> Joints { get; } = new();
    public Matrix4x4[] InverseBindMatrices { get; set; }
    public int SkeletonRoot { get; set; }
}

public class FrameworkCamera
{
    public string Name { get; set; }
    public CameraType Type { get; set; }
    public float Yfov { get; set; }
    public float Znear { get; set; }
    public float Zfar { get; set; }
}

public class FrameworkLight
{
    public string Name { get; set; }
    public LightType Type { get; set; }
    public Vector3 Color { get; set; }
    public float Intensity { get; set; }
    public float Range { get; set; }
}
```

## Implementation Notes

### Key Considerations
1. **Backward Compatibility**: Ensure changes don't break existing model formats
2. **Performance**: Optimize for glTF's typical access patterns
3. **Memory**: Handle potentially large glTF models efficiently
4. **Error Handling**: Robust error handling for malformed glTF files
5. **Testing**: Comprehensive testing with various glTF models

### Testing Plan
1. Test with simple glTF models first
2. Gradually test with more complex models
3. Validate PBR material rendering
4. Test animations and skinning
5. Performance profiling
6. Memory usage analysis

### Risk Mitigation
1. **Incremental Implementation**: Implement features in phases
2. **Fallback Mechanisms**: Provide fallbacks for unsupported features
3. **Validation**: Add validation for glTF-specific data
4. **Documentation**: Document glTF-specific behaviors
5. **Testing**: Extensive testing with diverse glTF models

## Next Steps

1. Implement the changes outlined above
2. Test with sample glTF models
3. Profile performance
4. Optimize as needed
5. Document the implementation
6. Create user documentation for glTF support