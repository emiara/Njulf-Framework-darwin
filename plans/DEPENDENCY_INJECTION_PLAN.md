# Dependency Injection Pattern for NjulfFramework

## Overview
This document outlines the design and implementation of a dependency injection (DI) pattern for the NjulfFramework. The goal is to create a DI container setup that can be used across all modules to improve modularity, testability, and maintainability.

## Current Architecture Analysis

Based on the existing documentation and codebase analysis:

1. **Current Dependency Graph**:
```mermaid
graph TD
    A[NjulfFramework] -->|depends on| B[NjulfFramework.Rendering]
    C[NjulfFramework.Assets] -->|depends on| B
    C -->|RendererAdapter| B
    D[NjulfFramework.Input] -->|no dependencies|
    E[NjulfFramework.Core] -->|no dependencies|
```

2. **New Dependency Graph with DI**:
```mermaid
graph TD
    A[NjulfFramework] -->|depends on| E[NjulfFramework.Core]
    B[NjulfFramework.Rendering] -->|depends on| E
    C[NjulfFramework.Assets] -->|depends on| E
    D[NjulfFramework.Input] -->|depends on| E
    E[NjulfFramework.Core] -->|no dependencies|
    
    A -->|DI Container| B
    A -->|DI Container| C
    A -->|DI Container| D
```

2. **Key Issues**:
   - Tight coupling between Assets and Rendering modules
   - Direct type dependencies instead of interfaces
   - No centralized dependency injection pattern
   - Limited testability due to hard-coded dependencies

## DI Container Design

### 1. Container Structure

We'll use the Microsoft.Extensions.DependencyInjection package as the DI container, which is the standard for .NET applications and integrates well with the existing framework.

### 2. Module-Specific Service Collections

Each module will have its own service collection extensions:

```csharp
// NjulfFramework.Core/DependencyInjection/CoreServiceCollectionExtensions.cs
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddNjulfFrameworkCore(this IServiceCollection services)
    {
        // Register core services
        services.AddSingleton<IAssetCache, AssetCache>();
        services.AddTransient<IModelConverter, RendererAdapter>();
        
        return services;
    }
}

// NjulfFramework.Rendering/DependencyInjection/RenderingServiceCollectionExtensions.cs
public static class RenderingServiceCollectionExtensions
{
    public static IServiceCollection AddNjulfFrameworkRendering(this IServiceCollection services)
    {
        services.AddSingleton<IRenderer, VulkanRenderer>();
        services.AddSingleton<IMeshManager, MeshManager>();
        services.AddSingleton<ITextureManager, TextureManager>();
        services.AddSingleton<ISceneDataBuilder, SceneDataBuilder>();
        
        return services;
    }
}

// NjulfFramework.Assets/DependencyInjection/AssetsServiceCollectionExtensions.cs
public static class AssetsServiceCollectionExtensions
{
    public static IServiceCollection AddNjulfFrameworkAssets(this IServiceCollection services)
    {
        services.AddSingleton<IAssetLoader, AssetLoader>();
        services.AddSingleton<IAssimpImporter, AssimpImporter>();
        services.AddSingleton<ITextureLoader, TextureLoader>();
        
        return services;
    }
}

// NjulfFramework.Input/DependencyInjection/InputServiceCollectionExtensions.cs
public static class InputServiceCollectionExtensions
{
    public static IServiceCollection AddNjulfFrameworkInput(this IServiceCollection services)
    {
        services.AddSingleton<IInputManager, InputManager>();
        services.AddSingleton<IKeyboardDevice, KeyboardDevice>();
        services.AddSingleton<IMouseDevice, MouseDevice>();
        
        return services;
    }
}
```

### 3. Main Application Setup

The main application will configure the DI container:

```csharp
// Program.cs
var services = new ServiceCollection();

// Add all framework modules
services.AddNjulfFrameworkCore()
        .AddNjulfFrameworkRendering()
        .AddNjulfFrameworkAssets()
        .AddNjulfFrameworkInput();

// Build the service provider
var serviceProvider = services.BuildServiceProvider();

// Use DI for application components
var application = new NjulfApplication(serviceProvider);
await application.RunAsync();
```

## Interface-Based Service Registration

### 1. Core Interfaces

The framework already has core interfaces defined in `NjulfFramework.Core`:

- `IRenderer`, `IRenderable`, `IMaterial`, `IMesh` (Rendering)
- `IAsset`, `IModel`, `IAssetLoader` (Assets)
- `IScene`, `ISceneNode` (Scene)
- `IModelConverter` (Conversion)

### 2. Service Lifetimes

We'll use appropriate service lifetimes:

- **Singleton**: Services that maintain state (e.g., `IRenderer`, `IAssetCache`)
- **Transient**: Stateless services (e.g., `IModelConverter`, `IAssimpImporter`)
- **Scoped**: Not typically needed for this framework

### 3. Constructor Injection Pattern

All major components will use constructor injection:

```csharp
// Example: VulkanRenderer with constructor injection
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
    
    // Implementation
}
```

## Adapter Pattern Implementation

### 1. RendererAdapter Refactoring

The current `RendererAdapter` will be refactored to use interfaces:

```csharp
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
```

### 2. FrameworkModel Adapter

Create adapters to convert between concrete types and interfaces:

```csharp
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
```

## Testing Strategy

### 1. Unit Testing with Mocks

DI enables easy mocking for unit testing:

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
```

### 2. Integration Testing

Test the complete DI setup:

```csharp
[Test]
public void DI_Container_ShouldResolveAllServices()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddNjulfFrameworkCore()
            .AddNjulfFrameworkRendering()
            .AddNjulfFrameworkAssets()
            .AddNjulfFrameworkInput();
    
    var provider = services.BuildServiceProvider();
    
    // Act & Assert
    Assert.NotNull(provider.GetService<IRenderer>());
    Assert.NotNull(provider.GetService<IAssetLoader>());
    Assert.NotNull(provider.GetService<IInputManager>());
    Assert.NotNull(provider.GetService<IModelConverter>());
}
```

## Implementation Plan

### Phase 1: DI Container Setup (Current Phase)
- [x] Analyze current architecture and dependencies
- [ ] Design DI container structure
- [ ] Create service collection extensions for each module
- [ ] Update Program.cs to use DI

### Phase 2: Module Refactoring
- [ ] Refactor VulkanRenderer to use constructor injection
- [ ] Update AssetLoader to use DI
- [ ] Refactor RendererAdapter to use interfaces
- [ ] Update all rendering components

### Phase 3: Testing and Validation
- [ ] Write unit tests with mocks
- [ ] Create integration tests for DI container
- [ ] Validate performance impact
- [ ] Update documentation

## Benefits of This Approach

1. **Reduced Coupling**: Modules depend only on Core interfaces
2. **Improved Testability**: Easy to mock dependencies for unit testing
3. **Better Modularity**: Can swap implementations (e.g., different renderers)
4. **Clearer Architecture**: Explicit dependencies through DI
5. **Easier Maintenance**: Changes in one module don't cascade to others
6. **Standard .NET Pattern**: Uses familiar Microsoft.Extensions.DependencyInjection

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

1. Create DI container extensions for each module
2. Implement constructor injection in major components
3. Refactor RendererAdapter to use interfaces
4. Update Program.cs to use DI container
5. Write comprehensive tests for the new DI setup