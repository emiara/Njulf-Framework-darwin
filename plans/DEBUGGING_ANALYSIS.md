# Debugging Analysis: Exit Code -1,073,741,510

## Issue Description
The application runs without visible errors but no window appears. When manually shutting down the program, it returns exit code -1,073,741,510 (0xC0000005), which indicates an **access violation exception** on Windows.

## Root Cause Analysis

### 1. Exit Code Analysis
- **Exit Code**: -1,073,741,510 (0xC0000005)
- **Meaning**: STATUS_ACCESS_VIOLATION - The program tried to access memory it doesn't have permission to access
- **Common Causes**:
  - Null pointer dereference
  - Accessing freed memory
  - Buffer overflow/underflow
  - Invalid pointer operations

### 2. Application Flow Analysis

#### Program.cs Entry Point
- Creates window using Silk.NET.Windowing
- Sets up dependency injection
- Initializes Vulkan renderer
- Sets up event handlers for Load, Update, Render, and Closing
- Starts window event loop with `window.Run()`

#### VulkanRenderer Initialization
- Creates Vulkan context and surface
- Sets up swapchain, depth resources, command buffers
- Initializes synchronization primitives
- Creates render passes, framebuffers, descriptor managers
- Sets up mesh pipeline and bindless resources
- Creates render graph with tiled light culling and mesh passes
- Adds test cube to scene

### 3. Potential Issues Identified

#### A. Window Creation and Event Loop
- Window is created successfully (no null check failure)
- Event handlers are properly registered
- `window.Run()` should start the main loop
- **Issue**: The window might not be visible due to missing `window.Visible = true` or similar

#### B. Vulkan Initialization
- Vulkan context creation appears correct
- All required extensions are checked
- Surface creation uses window's VkSurface
- **Potential Issue**: The Vulkan initialization might be failing silently after surface creation

#### C. Rendering Loop
- `OnRender` calls `_renderer.RenderFrameAsync()`
- `RenderFrameAsync()` returns `Task.CompletedTask` immediately (no actual rendering)
- **Critical Issue**: The renderer's `RenderFrameAsync()` method is empty - it doesn't actually render anything!

#### D. Memory Management
- Multiple Vulkan resources are created but might not be properly initialized
- Buffer managers, descriptor heaps, and pipeline objects could have null references
- **Potential Issue**: Null reference exceptions in Vulkan calls that aren't properly caught

### 4. Most Likely Causes

#### Primary Issue: Empty RenderFrameAsync
The most obvious issue is in `VulkanRenderer.RenderFrameAsync()`:

```csharp
public Task RenderFrameAsync()
{
    // Render a frame using Vulkan
    return Task.CompletedTask;  // ← This does nothing!
}
```

This method should call the actual rendering logic (likely the `Draw()` method that exists in the class).

#### Secondary Issue: Missing Window Visibility
The window might be created but not made visible. Silk.NET windows should be visible by default, but this should be verified.

#### Potential Access Violation Sources
1. **Null Vulkan Context**: If `_vulkanContext` is null when Vulkan API calls are made
2. **Invalid Surface**: If the surface creation fails but isn't properly checked
3. **Uninitialized Swapchain**: If swapchain creation fails silently
4. **Null Pointer in Rendering**: Any Vulkan call with null pointers could cause access violation

### 5. Debugging Recommendations

#### Immediate Fixes
1. **Fix RenderFrameAsync**: Make it call the actual rendering logic
2. **Add Null Checks**: Ensure all Vulkan objects are properly initialized
3. **Add More Logging**: Log each step of Vulkan initialization
4. **Check Window Visibility**: Ensure window is actually visible

#### Code Changes Needed

```csharp
// In VulkanRenderer.cs
public Task RenderFrameAsync()
{
    try
    {
        Draw();  // Call the actual rendering method
        return Task.CompletedTask;
    }
    catch (Exception ex)
    {
        Console.WriteLine($