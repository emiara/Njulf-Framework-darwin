# Njulf Framework - Technology Stack

## Core Technologies
- **Language**: C# (.NET 10.0)
- **Rendering API**: Vulkan 1.3
- **Shader Languages**: GLSL, HLSL (compiled to SPIR-V)
- **Physics**: BepuPhysics2
- **Asset Processing**: Assimp via Silk.NET.Assimp

## Development Environment
- **IDE**: JetBrains Rider
- **Debugging**: RenderDoc integration
- **Shader Development**: Shader Playground with hot-reload

## Key Libraries and Packages
- **Silk.NET**: Vulkan bindings, windowing, input, and math utilities
- **GpuMemoryAllocator**: Vulkan memory management
- **BepuPhysics**: Physics simulation

## Project Structure
```
Njulf Framework/                  # Main application entry point
├── Njulf Framework.Core/         # Core framework utilities
├── Njulf Framework.Rendering/    # Vulkan rendering system
│   ├── Core/                     # Vulkan context and management
│   ├── Data/                     # GPU data structures
│   ├── Pipeline/                 # Rendering pipelines and passes
│   ├── Resources/                # Resource management
│   │   ├── Descriptors/           # Descriptor set management
│   │   └── Handles/               # Resource handles
│   └── Shaders/                  # Shader sources and compilation
├── Njulf Framework.Physics/      # Physics integration
├── Njulf Framework.Input/        # Input handling
└── Njulf Framework.Tests/        # Unit and integration tests
```

## Build System
- **.NET SDK**: MSBuild-based project system
- **NuGet Packages**: Dependency management

## Target Platforms
- **Operating Systems**: Windows, Linux
- **GPU Architectures**: NVIDIA RTX, AMD RDNA2+, Intel Arc
- **API Support**: Vulkan 1.3 with extensions

## Performance Optimizations
- **Bindless Resource Model**: VK_KHR_bind_memory2 for efficient resource binding
- **Dynamic Render Graph**: Runtime pass scheduling and dependency resolution
- **Custom Memory Allocator**: Defragmentation-on-demand to prevent stalls
- **Mesh Shaders**: Task/mesh shader pipeline for geometry processing
- **Hybrid Lighting**: Tiled forward+ combined with ray-traced effects