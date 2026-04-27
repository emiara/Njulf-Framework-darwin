# Interface-Based Design Plan for NjulfFramework

## Overview
This document outlines the plan to refactor NjulfFramework to use interface-based design, move shared interfaces to NjulfFramework.Core, implement dependency injection, and reduce direct type dependencies between modules.

## Current Architecture Analysis

### Key Issues Identified
1. **Tight Coupling**: Assets module directly depends on Rendering module through RendererAdapter
2. **Limited Core Functionality**: Core project lacks shared interfaces and base classes
3. **Direct Type Usage**: Modules use concrete types instead of interfaces
4. **No Dependency Injection**: Hard to swap implementations or create mocks for testing

### Current Dependency Graph
```mermaid
graph TD
    A[NjulfFramework] -->|depends on| B[NjulfFramework.Rendering]
    C[NjulfFramework.Assets] -->|depends on| B
    C -->|RendererAdapter| B
    D[NjulfFramework.Input] -->|no dependencies|
    E[NjulfFramework.Core] -->|no dependencies|
```

## Refactoring Goals

1. **Move shared interfaces to NjulfFramework.Core**
2. **Implement dependency injection pattern**
3. **Reduce direct type dependencies between modules**
4. **Improve testability and modularity**

## Step 1: Create Core Interfaces

### New Core Interfaces to Create

#### 1. Rendering Abstractions (NjulfFramework.Core/Rendering)
```csharp
// IRenderer.cs
public interface IRenderer : IDisposable
{
    Task InitializeAsync();
    Task RenderFrameAsync();
    void Resize(int width, int height);
}

// IRenderable.cs
public interface IRenderable
{
    string Name { get; }
    Matrix4x4 Transform { get; set; }
    void Update(double deltaTime);
}

// IMaterial.cs
public interface IMaterial
{
    string Name { get; }
    string ShaderPath { get; }
    // Common material properties
}

// IMesh.cs
public interface IMesh
{
    string Name { get; }
    BoundingBox Bounds { get; }
    // Mesh data access
}
```

#### 2. Asset Abstractions (NjulfFramework.Core/Assets)
```csharp
// IAsset.cs
public interface IAsset : IDisposable
{
    string Name { get; }
    string SourcePath { get; }
}

// IModel.cs
public interface IModel : IAsset
{
    IEnumerable<IMesh> Meshes { get; }
    IEnumerable<IMaterial> Materials { get; }
}

// IAssetLoader.cs (move from NjulfFramework.Assets)
public interface IAssetLoader : IDisposable
{
    Task<IModel> LoadModelAsync(string filePath, CancellationToken cancellationToken = default);
    IModel GetCachedModel(string filePath);
    void ClearCache();
    event EventHandler<AssetLoadProgress> LoadProgress;
}
```

#### 3. Scene Abstractions (NjulfFramework.Core/Scene)
```csharp
// IScene.cs
public interface IScene
{
    IEnumerable<IRenderable> Renderables { get; }
    void AddRenderable(IRenderable renderable);
    void RemoveRenderable(IRenderable renderable);
}

// ISceneNode.cs
public interface ISceneNode
{
    string Name { get; }
    Matrix4x4 Transform { get; set; }
    ISceneNode Parent { get; }
    IEnumerable<ISceneNode> Children { get; }
}
```

## Step 2: Implement Adapter Pattern with Interfaces

### Refactor RendererAdapter to use Interfaces

```csharp
// New interface-based adapter
public class RendererAdapter : IModelConverter
{
    private readonly IRenderer _renderer;
    
    public RendererAdapter(IRenderer renderer)
    {
        _renderer = renderer;
    }
    
    public IEnumerable<IRenderable> ConvertToRenderables(IModel model)
    {
        foreach (var mesh in model.Meshes)
        {
            var material = model.Materials.FirstOrDefault(m => m.Name == mesh.MaterialName);
            yield return new RenderObject(mesh, material ?? CreateDefaultMaterial());
        }
    }
}

// Interface for model conversion
public interface IModelConverter
{
    IEnumerable<IRenderable> ConvertToRenderables(IModel model);
}
```

## Step 3: Implement Dependency Injection

### Create DI Container Setup

```csharp
// In NjulfFramework.Core/DependencyInjection
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNjulfFrameworkCore(this IServiceCollection services)
    {
        // Register core services
        services.AddSingleton<IAssetCache, AssetCache>();
        services.AddTransient<IModelConverter, RendererAdapter>();
        
        return services;
    }
}

// In NjulfFramework.Rendering/DependencyInjection
public static class RenderingServiceCollectionExtensions
{
    public static IServiceCollection AddNjulfFrameworkRendering(this IServiceCollection services)
    {
        services.AddSingleton<IRenderer, VulkanRenderer>();
        services.AddSingleton<IMeshManager, MeshManager>();
        services.AddSingleton<ITextureManager, TextureManager>();
        
        return services;
    }
}

// In NjulfFramework.Assets/DependencyInjection
public static class AssetsServiceCollectionExtensions
{
    public static IServiceCollection AddNjulfFrameworkAssets(this IServiceCollection services)
    {
        services.AddSingleton<IAssetLoader, AssetLoader>();
        services.AddSingleton<IAssimpImporter, AssimpImporter>();
        
        return services;
    }
}
```

### Update Program.cs to use DI

```csharp
// Before
var assetLoader = new AssetLoader();
var renderer = new VulkanRenderer();
var adapter = new RendererAdapter();

// After
var services = new ServiceCollection();
services.AddNjulfFrameworkCore()
        .AddNjulfFrameworkRendering()
        .AddNjulfFrameworkAssets();

var serviceProvider = services.BuildServiceProvider();

var assetLoader = serviceProvider.GetRequiredService<IAssetLoader>();
var renderer = serviceProvider.GetRequiredService<IRenderer>();
var converter = serviceProvider.GetRequiredService<IModelConverter>();
```

## Step 4: Refactor Module Dependencies

### New Dependency Graph
```mermaid
graph TD
    A[NjulfFramework] -->|depends on| E[NjulfFramework.Core]
    B[NjulfFramework.Rendering] -->|depends on| E
    C[NjulfFramework.Assets] -->|depends on| E
    D[NjulfFramework.Input] -->|depends on| E
    E[NjulfFramework.Core] -->|no dependencies|
```

### Update Project References

1. **NjulfFramework.Core**: No dependencies
2. **NjulfFramework.Rendering**: Reference Core
3. **NjulfFramework.Assets**: Reference Core
4. **NjulfFramework.Input**: Reference Core
5. **NjulfFramework**: Reference Core, Rendering, Assets, Input

## Step 5: Implement Interface-Based Rendering Pipeline

### Refactor VulkanRenderer to use interfaces

```csharp
public class VulkanRenderer : IRenderer
{
    private readonly IMeshManager _meshManager;
    private readonly ITextureManager _textureManager;
    private readonly ISceneDataBuilder _sceneDataBuilder;
    
    public VulkanRenderer(
        IMeshManager meshManager,
        ITextureManager textureManager,
        ISceneDataBuilder sceneDataBuilder)
    {
        _meshManager = meshManager;
        _textureManager = textureManager;
        _sceneDataBuilder = sceneDataBuilder;
    }
    
    public async Task RenderFrameAsync()
    {
        // Use interfaces instead of concrete types
        var sceneData = _sceneDataBuilder.BuildSceneData();
        // Rendering logic
    }
}
```

## Step 6: Create Adapter Implementations

### Implement Concrete Adapters

```csharp
// FrameworkModel to IModel adapter
public class FrameworkModelAdapter : IModel
{
    private readonly FrameworkModel _model;
    
    public FrameworkModelAdapter(FrameworkModel model)
    {
        _model = model;
    }
    
    public IEnumerable<IMesh> Meshes => _model.Meshes.Select(m => new FrameworkMeshAdapter(m));
    public IEnumerable<IMaterial> Materials => _model.Materials.Select(m => new FrameworkMaterialAdapter(m));
    
    // Implement IModel interface
}

// Similar adapters for Mesh, Material, etc.
```

## Step 7: Update Asset Loading Pipeline

### Refactor AssetLoader to use interfaces

```csharp
public class AssetLoader : IAssetLoader
{
    private readonly IAssimpImporter _importer;
    private readonly IModelConverter _converter;
    private readonly IAssetCache _cache;
    
    public AssetLoader(
        IAssimpImporter importer,
        IModelConverter converter,
        IAssetCache cache)
    {
        _importer = importer;
        _converter = converter;
        _cache = cache;
    }
    
    public async Task<IModel> LoadModelAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_cache.TryGetModel(filePath, out var cachedModel))
            return cachedModel;
        
        // Import and convert
        var frameworkModel = await _importer.ImportModelAsync(filePath, cancellationToken);
        var model = new FrameworkModelAdapter(frameworkModel);
        
        // Cache and return
        _cache.AddModel(filePath, model);
        return model;
    }
}
```

## Step 8: Testing Strategy

### Unit Testing with Mocks

```csharp
[Test]
public async Task AssetLoader_ShouldCacheModels()
{
    // Arrange
    var mockImporter = new Mock<IAssimpImporter>();
    var mockConverter = new Mock<IModelConverter>();
    var mockCache = new Mock<IAssetCache>();
    
    var assetLoader = new AssetLoader(mockImporter.Object, mockConverter.Object, mockCache.Object);
    
    // Act
    await assetLoader.LoadModelAsync("test.gltf");
    
    // Assert
    mockCache.Verify(c => c.AddModel("test.gltf", It.IsAny<IModel>()), Times.Once);
}

[Test]
public async Task RendererAdapter_ShouldConvertModelToRenderables()
{
    // Arrange
    var mockRenderer = new Mock<IRenderer>();
    var adapter = new RendererAdapter(mockRenderer.Object);
    var mockModel = new Mock<IModel>();
    
    // Act
    var renderables = adapter.ConvertToRenderables(mockModel.Object);
    
    // Assert
    Assert.IsNotNull(renderables);
}
```

## Implementation Timeline

1. **Phase 1: Core Interfaces** (1-2 days)
   - Create all core interfaces in NjulfFramework.Core
   - Ensure no dependencies on other projects

2. **Phase 2: Dependency Injection** (2-3 days)
   - Implement DI container extensions
   - Refactor entry point to use DI
   - Update test projects

3. **Phase 3: Adapter Implementation** (3-5 days)
   - Create concrete adapter implementations
   - Refactor RendererAdapter to use interfaces
   - Update asset loading pipeline

4. **Phase 4: Module Refactoring** (5-7 days)
   - Refactor VulkanRenderer to use interfaces
   - Update all rendering components
   - Refactor input system if needed

5. **Phase 5: Testing & Validation** (3-5 days)
   - Write unit tests with mocks
   - Integration testing
   - Performance validation

## Benefits of This Approach

1. **Reduced Coupling**: Modules depend only on Core interfaces
2. **Improved Testability**: Easy to mock dependencies for unit testing
3. **Better Modularity**: Can swap implementations (e.g., different renderers)
4. **Clearer Architecture**: Explicit dependencies through DI
5. **Easier Maintenance**: Changes in one module don't cascade to others

## Risks and Mitigations

1. **Performance Overhead**: Interface calls vs direct calls
   - Mitigation: Use aggressive inlining, profile critical paths

2. **Complexity Increase**: More interfaces and adapters
   - Mitigation: Clear documentation, keep adapters simple

3. **Breaking Changes**: Existing code may need updates
   - Mitigation: Provide migration guide, maintain backward compatibility where possible

4. **Learning Curve**: Team needs to understand new patterns
   - Mitigation: Training sessions, code reviews, documentation

## Success Metrics

1. **Coupling Metrics**: Measure reduction in direct module dependencies
2. **Test Coverage**: Increase in unit test coverage due to better testability
3. **Build Time**: Should remain similar or improve due to clearer dependencies
4. **Performance**: No significant regression in rendering performance
5. **Developer Productivity**: Easier to add new features without breaking changes

## Next Steps

1. Create NjulfFramework.Core project structure
2. Implement core interfaces
3. Set up dependency injection
4. Refactor modules to use interfaces
5. Test and validate the new architecture