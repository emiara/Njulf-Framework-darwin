# AGENTS.md

This file contains project-specific information discovered during analysis.

## Project Overview
- **Project Name**: NjulfFramework
- **Type**: C# Framework for Graphics and Rendering
- **Primary Focus**: Vulkan-based rendering, asset management, and input handling

## Key Components

### 1. **Rendering System**
- **VulkanRenderer.cs**: Core rendering engine using Vulkan API
- **Pipeline**: Includes mesh, raster, and ray tracing passes
- **Resource Management**: Descriptor heaps, texture management, and buffer handling
- **Shaders**: Forward+, mesh, and task shaders for modern rendering techniques

### 2. **Asset System**
- **AssetLoader.cs**: Handles loading and caching of assets
- **AssimpImporter.cs**: Uses Assimp library for model import
- **MaterialConverter.cs**: Converts materials for framework compatibility
- **GLTF Support**: Integrated GLTF model loading and processing

### 3. **Input System**
- **InputManager.cs**: Manages input devices and actions
- **KeyboardDevice.cs** and **MouseDevice.cs**: Device-specific implementations
- **InputActionBuilder.cs**: Fluent API for defining input actions

### 4. **Core Architecture**
- **FrameworkModel.cs**: Base model structure for the framework
- **SceneDataBuilder.cs**: Builds scene data for rendering
- **GPUData.cs**: Manages GPU-related data structures

## Technical Details

### Vulkan Integration
- Uses bindless descriptor heaps for efficient resource management
- Implements tiled light culling for performance optimization
- Supports dynamic mesh and raster pipelines

### Asset Pipeline
- Processes GLTF models with textures and materials
- Includes texture loading and conversion utilities
- Manages asset caching for performance

### Input Handling
- Supports keyboard and mouse input devices
- Action-based input system with type safety
- Binding system for flexible input configuration

## Project-Specific Patterns

1. **Handle-Based Resource Management**: Uses handle generators for GPU resources
2. **Data-Driven Rendering**: Scene data is built dynamically for each frame
3. **Modular Pipeline Design**: Render passes are composable and extensible
4. **Cross-Platform Considerations**: Designed for Windows with Vulkan backend

## Notable Files

- `vintage_video_camera_2k.gltf`: Sample GLTF model included in project
- `PBR_PLAN.md`: Physical Based Rendering implementation plan
- `GLTF_INTEGRATION_PLAN.md`: GLTF integration strategy document

## Build and Configuration

- **Solution File**: `NjulfFramework.sln`
- **Project Files**: Multiple CSProj files for modular structure
- **Dependencies**: Vulkan SDK, Assimp library, and other graphics-related dependencies

## Development Notes

- The framework is actively developed with focus on Vulkan rendering
- Includes experimental features like mesh shaders and ray tracing
- Designed for game development and real-time graphics applications
- Follows modern C# practices with async/await patterns where appropriate