# AGENTS.md

## Build & Run
- `dotnet build` / `dotnet run` (project: `NjulfFramework/`). No custom scripts.
- Target: `net10.0`. All projects must stay on same TFM.
- `AllowUnsafeBlocks` true in `NjulfFramework.Rendering` and `NjulfFramework.Assets` (Vulkan interop).

## Project Dependency Graph
```
NjulfFramework.Input  (leaf)       -- Silk.NET.Input only
NjulfFramework.Core                 -- refs Input; defines interfaces, enums, GameFramework base
NjulfFramework.Assets               -- refs Core; AllowUnsafeBlocks
NjulfFramework.Rendering            -- refs Core, Assets; AllowUnsafeBlocks; Vulkan/VMA/StbImage
NjulfFramework.Physics              -- standalone stub (BepuPhysics dep only)
NjulfFramework.Tests                -- standalone stub; no test framework; Silk.NET version drift (2.22.0 vs 2.23.0)
NjulfFramework (EXE)                -- refs Core, Assets, Input, Rendering; entry point Program.cs
```
**Do not add project references to Physics or Tests** until they are wired into the DI graph.

## DI Registration Pattern
Each module registers services via `DependencyInjection/*ServiceCollectionExtensions.cs`:
- `AddNjulfFrameworkCore()` — currently empty (placeholder)
- `AddNjulfFrameworkInput()` — registers `IInputManager`
- `AddNjulfFrameworkAssets()` — registers `AssimpImporter`, `MaterialConverter`, `MeshConverter`, `ModelProcessor`, `AssetCache`, `IAssetLoader`, `IContentManager`
- `AddNjulfFrameworkRendering()` — registers `ICamera` (default at (0,0,5)), `VulkanRenderer` as `IRenderer` and `ISceneLoader`

**Call order in `Program.cs`:** `AddNjulfFrameworkCore().AddNjulfFrameworkRendering().AddNjulfFrameworkAssets().AddNjulfFrameworkInput()`.

## Lifecycle (`GameFramework` base class)
1. `ConfigureWindow()` → `WindowOptions.DefaultVulkan`
2. `RegisterFrameworkServices(IServiceCollection)` — DI registration
3. `Load()` — load assets (model path: `"vintage_video_camera_2k.gltf"`)
4. `Update(float deltaTime)` — game logic + `_inputManager.Update()`
5. `Draw()` — post-render custom drawing (currently empty in example)
6. `Cleanup()` — `Renderer?.Dispose()`, `Window?.Dispose()`

**InputManager** is obtained via reflection on `GameFramework._serviceProvider` (private field) because the base class doesn't expose the service provider publicly.

## Shaders
- All GLSL 460, compiled at runtime via `glslc` (Google shader compiler): `vertex.vert`, `fragment.frag`, `forward_plus.frag`, `light_cull.comp`, `mesh.mesh`, `mesh.task`.
- Shader source in `NjulfFramework.Rendering/Shaders/`, copied to output dir.
- No SPIR-V pre-compilation; `glslc` must be on PATH.

## Rendering Architecture
- **VulkanRenderer.cs** (1368 lines) — bindless descriptors, mesh shaders (ExtMeshShader), Forward+ with tiled light culling, ray tracing.
- **Render graph**: `RenderGraph` + `RenderGraphPass` + `RenderGraphContext`, composable passes.
- **Bindless buffer indices** defined in `Resources/Descriptors/BindlessBufferIndices.cs`.
- **Handle-based GPU resources**: `HandleGenerator`, `BufferHandle`, `TextureHandle`.

## TODOs (from Notes.txt)
1. Threaded scene loading: split `LoadModelIntoScene` into `BuildCpuPayload` (any thread) + `IntegratePayload` (render thread via `ConcurrentQueue`).
2. Replace `DeviceWaitIdle` in `FinalizeAndUpdateMeshBuffers` with a frame-indexed deletion queue.
3. Move mesh/index/meshlet buffers into bindless heap with update-after-bind (or double-buffer descriptor sets).

## Gotchas
- **Tests project** uses Silk.NET 2.22.0 vs 2.23.0 everywhere else — update if adding tests.
- **Physics/Tests are empty stubs** — `Class1.cs` with no real code.
- Input manager needs explicit `Initialize(keyboard, mouse)` call (done in `GameFramework.OnWindowLoad()`).
- Asset `.gltf`/`.bin`/texture files must exist at output (configured via `<CopyToOutputDirectory>` in csproj).
