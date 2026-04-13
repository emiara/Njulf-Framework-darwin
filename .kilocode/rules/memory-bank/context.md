# Njulf Framework - Current Context

## Current Work Focus
- **Core Rendering Pipeline**: The framework is actively being developed with a focus on:
  - Vulkan 1.3 integration with modern extensions
  - Bindless resource management system
  - Dynamic render graph with automatic barrier handling
  - Mesh shader pipeline for geometry processing

## Recent Changes
- **Architecture**: Implemented core components:
  - Vulkan context with validation layers
  - Bindless descriptor heap with 65,536 slots
  - Render graph system with sequential pass execution
  - Mesh shader pipeline with task/mesh shader support

- **Optimizations**:
  - Tiled light culling for efficient dynamic lighting
  - Custom memory allocator with defragmentation
  - Hybrid lighting system combining forward+ and ray tracing

## Next Steps
1. **Testing**: Validate the render graph with complex scenes
2. **Optimization**: Implement explicit dependency tracking in render graph
3. **Features**: Add variable-rate shading support
4. **Tooling**: Enhance shader hot-reload capabilities
5. **Documentation**: Expand API documentation for public interfaces

## Known Limitations
- Render graph currently uses simple sequential execution
- No fallback for mesh shaders on unsupported hardware
- Memory defragmentation is manual (on-demand)

## Development Priorities
1. **Stability**: Ensure robust error handling in Vulkan operations
2. **Performance**: Optimize descriptor updates and resource transitions
3. **Extensibility**: Design plugin system for custom render passes
4. **Cross-Platform**: Validate Linux compatibility
5. **Tooling**: Integrate RenderDoc capture automation