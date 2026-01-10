using System.Numerics;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Silk.NET.Core.Contexts;
using Njulf_Framework.Rendering.Core;
using Njulf_Framework.Rendering.Resources;
using Njulf_Framework.Rendering.Pipeline;
using Njulf_Framework.Rendering.Data;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Njulf_Framework.Rendering;

public class VulkanRenderer : IDisposable
{
    private readonly IWindow _window;
    private Core.VulkanContext? _vulkanContext;
    private Core.SwapchainManager? _swapchainManager;
    private Core.CommandBufferManager? _commandBufferManager;
    private Core.SynchronizationManager? _synchronizationManager;
    private RenderPassManager? _renderPassManager;
    private FramebufferManager? _framebufferManager;
    private GraphicsPipeline? _graphicsPipeline;
    
    private KhrSwapchain? _khrSwapchain;
    private KhrSurface? _khrSurface;

    // Phase 2: Resource managers
    private BufferManager? _bufferManager;
    private MeshManager? _meshManager;
    private UniformBufferManager? _uniformBufferManager;
    private DescriptorManager? _descriptorManager;

    // Phase 2: Scene objects
    private Dictionary<string, RenderingData.RenderObject> _renderObjects = new();

    private SurfaceKHR _surface;
    private uint _currentFrameIndex = 0;
    private const uint MaxFramesInFlight = 2;

    // Camera matrices
    private Matrix4x4 _viewMatrix = Matrix4x4.CreateLookAt(
        new Vector3(0, 2, 3), //new Vector3(0, 2, 3),
        Vector3.Zero,
        Vector3.UnitY);

    private Matrix4x4 _projectionMatrix;

    public VulkanContext VulkanContext => _vulkanContext!;
    public Core.SwapchainManager SwapchainManager => _swapchainManager!;

    public VulkanRenderer(IWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>
    /// Initialize the renderer. Called once at startup.
    /// </summary>
    public void Load()
    {
        try
        {
            _vulkanContext = new VulkanContext(enableValidationLayers: true);
            Console.WriteLine("✓ Vulkan context created");
            
            // Get the KhrSwapchain extension
            if (!_vulkanContext.VulkanApi.TryGetDeviceExtension(_vulkanContext.Instance, _vulkanContext.Device, out _khrSwapchain))
            {
                throw new Exception("KHR_swapchain extension not available");
            }
            
            // Get the KhrSurface extension
            if (!_vulkanContext.VulkanApi.TryGetInstanceExtension(_vulkanContext.Instance, out _khrSurface))
            {
                throw new Exception("KHR_surface extension not available");
            }

            _surface = CreateSurface();
            Console.WriteLine("✓ Vulkan surface created");

            _swapchainManager = new SwapchainManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Instance,
                _vulkanContext.PhysicalDevice,
                _vulkanContext.Device,
                _surface,
                _vulkanContext.GraphicsQueue,
                (uint)_window.Size.X,
                (uint)_window.Size.Y);
            Console.WriteLine("✓ Swapchain created");
            
            Console.WriteLine($"Swapchain format: {_swapchainManager.SwapchainImageFormat}");


            _commandBufferManager = new CommandBufferManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _vulkanContext.GraphicsQueueFamily,
                MaxFramesInFlight);
            Console.WriteLine("✓ Command buffers allocated");

            _synchronizationManager = new SynchronizationManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _swapchainManager.SwapchainImageCount,  
                MaxFramesInFlight);
            Console.WriteLine("✓ Synchronization primitives created");

            // Create render pass
            _renderPassManager = new RenderPassManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _swapchainManager.SwapchainImageFormat);
            Console.WriteLine("✓ Render pass created");

            // Create framebuffers
            _framebufferManager = new FramebufferManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _renderPassManager.RenderPass,
                _swapchainManager.SwapchainImageViews,
                _swapchainManager.SwapchainExtent);
            Console.WriteLine("✓ Framebuffers created");
            

            // Create descriptor manager
            _descriptorManager = new DescriptorManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                MaxFramesInFlight);
            Console.WriteLine("✓ Descriptor manager initialized");
            
            // Phase 2: Initialize resource managers
            _bufferManager = new BufferManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _vulkanContext.PhysicalDevice,
                _vulkanContext.TransferQueue,
                _vulkanContext.TransferQueueFamily);
            Console.WriteLine("✓ Buffer manager initialized");
            
            _uniformBufferManager = new UniformBufferManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _bufferManager,
                MaxFramesInFlight);
            Console.WriteLine("✓ Uniform buffer manager initialized");
            
            _meshManager = new MeshManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _bufferManager);
            Console.WriteLine("✓ Mesh manager initialized");

            // Update descriptor sets with uniform buffers
            for (uint i = 0; i < MaxFramesInFlight; i++)
            {
                if (_uniformBufferManager != null)
                {
                    var uniformBuffer = _uniformBufferManager.GetUniformBuffer(i);
                    Console.WriteLine($"Frame {i}: Uniform buffer handle = {uniformBuffer.Handle}");
                    
                    _descriptorManager.UpdateDescriptorSet(i, uniformBuffer, 
                        RenderingData.UniformBufferObject.GetSizeInBytes());
                }

                // Initialize with identity matrices so descriptor has valid data
                var initialUbo = new RenderingData.UniformBufferObject(
                    Matrix4x4.Identity,
                    _viewMatrix,
                    _projectionMatrix);
                if (_uniformBufferManager != null) _uniformBufferManager.UpdateUniformBuffer(i, initialUbo);
                
                Console.WriteLine($"✓ Frame {i}: Descriptor set updated and uniform buffer initialized");
            }
            Console.WriteLine("✓ Descriptor sets updated with uniform buffers");

            // Create graphics pipeline
            // _graphicsPipeline = new GraphicsPipeline(
            //     _vulkanContext.VulkanApi,
            //     _vulkanContext.Device,
            //     _renderPassManager.RenderPass,
            //     _swapchainManager.SwapchainExtent,
            //     "Shaders/triangle.vert.spv",
            //     "Shaders/triangle.frag.spv");
            _graphicsPipeline = new GraphicsPipeline(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _renderPassManager.RenderPass,
                _swapchainManager.SwapchainExtent,
                _descriptorManager.DescriptorSetLayout);
            Console.WriteLine("✓ Graphics pipeline created");

            // Phase 2: Add test cube to scene
            var cubeMesh = RenderingData.Mesh.CreateCube();
            var material = new RenderingData.Material("default", "Shaders/test_vert.spv", "");
            var cube = new RenderingData.RenderObject("test_cube", cubeMesh, material, Matrix4x4.Identity);
            AddRenderObject(cube);
            Console.WriteLine("✓ Test cube added to scene");

            System.Diagnostics.Debug.WriteLine("Vulkan renderer initialized successfully");
            Console.WriteLine("\n✓ Renderer fully initialized - rendering started!\n");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize renderer: {ex.Message}");
            Console.WriteLine($"✗ Renderer initialization failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"  Inner: {ex.InnerException.Message}");
            }
            throw;
        }
        
        _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4,
            _window.Size.X / (float)_window.Size.Y,
            0.1f,
            100f);
    }

    /// <summary>
    /// Update logic. Called once per frame before rendering.
    /// </summary>
    public void Update(double deltaTime)
    {
        // Rotate the test cube
        if (_renderObjects.TryGetValue("test_cube", out var cube))
        {
            var rotation = Matrix4x4.CreateRotationY((float)deltaTime);
            cube.Transform = rotation * cube.Transform;
        }
    }

    /// <summary>
    /// Render logic. Called once per frame.
    /// </summary>
    public unsafe void Draw()
    {
        if (_vulkanContext == null || _swapchainManager == null || 
            _commandBufferManager == null || _synchronizationManager == null ||
            _renderPassManager == null || _framebufferManager == null ||
            _graphicsPipeline == null || _meshManager == null ||
            _uniformBufferManager == null)
        {
            return;
        }

        var vk = _vulkanContext.VulkanApi;
        var device = _vulkanContext.Device;
        var graphicsQueue = _vulkanContext.GraphicsQueue;

        // Get this frame's resources (per-frame)
        var frameIndex = _currentFrameIndex % MaxFramesInFlight;
        var inFlightFence = _synchronizationManager.InFlightFences[frameIndex];
        var imageAvailableSemaphore = _synchronizationManager.ImageAvailableSemaphores[frameIndex];

        // Wait for this frame's fence to complete
        vk.WaitForFences(device, 1, &inFlightFence, true, ulong.MaxValue);
        vk.ResetFences(device, 1, &inFlightFence);

        // Acquire next image - signals imageAvailableSemaphore when done
        uint imageIndex = 0;
        var result = _khrSwapchain!.AcquireNextImage(
            device,
            _swapchainManager.Swapchain,
            ulong.MaxValue,
            imageAvailableSemaphore,  // This semaphore will be signaled
            default,
            &imageIndex);

        if (result == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapchain();
            return;
        }
        else if (result != Result.Success && result != Result.SuboptimalKhr)
        {
            throw new Exception("Failed to acquire next image");
        }

        // Get the render finished semaphore for THIS IMAGE (per-image)
        var renderFinishedSemaphore = _synchronizationManager.RenderFinishedSemaphores[imageIndex];

        // Record commands
        var commandBuffer = _commandBufferManager.CommandBuffers[frameIndex];
        _commandBufferManager.ResetCommandBuffer(commandBuffer);
        _commandBufferManager.BeginRecording(commandBuffer);

        RecordRenderCommands(commandBuffer, imageIndex, frameIndex);

        _commandBufferManager.EndRecording(commandBuffer);

        // Submit command buffer
        SubmitCommandBuffer(graphicsQueue, commandBuffer, imageAvailableSemaphore, 
            renderFinishedSemaphore, inFlightFence);

        // Present frame
        PresentFrame(imageIndex, renderFinishedSemaphore);

        _currentFrameIndex++;
    }



    /// <summary>
    /// Add a renderable object to the scene.
    /// </summary>
    public void AddRenderObject(RenderingData.RenderObject obj)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        _renderObjects[obj.Name] = obj;
        Console.WriteLine($"Added render object: {obj.Name}");
    }

    /// <summary>
    /// Remove a renderable object from the scene.
    /// </summary>
    public void RemoveRenderObject(string name)
    {
        if (_renderObjects.Remove(name))
        {
            Console.WriteLine($"Removed render object: {name}");
        }
    }

    /// <summary>
    /// Get a render object by name.
    /// </summary>
    public RenderingData.RenderObject? GetRenderObject(string name)
    {
        _renderObjects.TryGetValue(name, out var obj);
        return obj;
    }

    private unsafe SurfaceKHR CreateSurface()
    {
        SurfaceKHR surface = new SurfaceKHR();
        
        if (_vulkanContext != null)
        {
            surface = _window!.VkSurface!.Create<AllocationCallbacks>(_vulkanContext.Instance.ToHandle(), null).ToSurface();
        }
        
        return surface;
    }

    private unsafe void RecordRenderCommands(CommandBuffer commandBuffer, uint imageIndex, uint frameIndex)
    {
        var vk = _vulkanContext!.VulkanApi;
        var framebuffer = _framebufferManager!.Framebuffers[imageIndex];
        
        // Clear color: dark gray (0.1, 0.1, 0.1)
        var clearValue = new ClearValue 
        { 
            Color = new ClearColorValue(0.1f, 0.1f, 0.1f, 1.0f)
            //Color = new ClearColorValue(1.0f, 0.0f, 0.0f, 1.0f)
        };
        
        var renderPassBeginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPassManager!.RenderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D
            {
                Offset = new Offset2D { X = 0, Y = 0 },
                Extent = _swapchainManager!.SwapchainExtent
            },
            ClearValueCount = 1,
            PClearValues = &clearValue
        };
        
        vk.CmdBeginRenderPass(commandBuffer, &renderPassBeginInfo, SubpassContents.Inline);
        
        // Bind pipeline
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _graphicsPipeline!.Pipeline);
        
        Console.WriteLine($"[Frame {frameIndex}] Recording draw commands for {_renderObjects.Count} objects");
        
        // Phase 2: Render all visible objects
        foreach (var renderObj in _renderObjects.Values)
        {
            if (!renderObj.Visible)
                continue;
            
            Console.WriteLine($"  Drawing: {renderObj.Mesh.Name}, IndexCount: {renderObj.Mesh.Indices.Length}");
        
            // Update uniform buffer with this object's transform BEFORE recording commands
            var ubo = new RenderingData.UniformBufferObject(renderObj.Transform, _viewMatrix, _projectionMatrix);
            _uniformBufferManager!.UpdateUniformBuffer(frameIndex, ubo);
        
            // Get mesh GPU data (cached if already uploaded)
            var meshGpu = _meshManager!.GetOrCreateMeshGpu(renderObj.Mesh);
        
            // Bind mesh
            _meshManager.BindMesh(commandBuffer, meshGpu);
            
            // Bind descriptor set for this frame
            var descriptorSet = _descriptorManager!.DescriptorSets[frameIndex];
            vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, 
                _graphicsPipeline!.PipelineLayout, 0, 1, &descriptorSet, 0, null);
            
            Console.WriteLine($"    Vertex buffer: {meshGpu.VertexBuffer.Handle}, Index buffer: {meshGpu.IndexBuffer.Handle}");
                
            // Draw
            _meshManager.DrawMesh(commandBuffer, meshGpu);
        }
        
        // End render pass
        vk.CmdEndRenderPass(commandBuffer);
    }

    private unsafe void SubmitCommandBuffer(Queue queue, CommandBuffer commandBuffer,
        Semaphore waitSemaphore, Semaphore signalSemaphore, Fence fence)
    {
        var vk = _vulkanContext!.VulkanApi;

        var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore
        };

        if (vk.QueueSubmit(queue, 1, &submitInfo, fence) != Result.Success)
        {
            throw new Exception("Failed to submit command buffer");
        }
    }

    private unsafe void PresentFrame(uint imageIndex, Semaphore renderFinishedSemaphore)
    {
        var vk = _vulkanContext!.VulkanApi;
        var swapchain = _swapchainManager!.Swapchain;

        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &renderFinishedSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex
        };

        var result = _khrSwapchain!.QueuePresent(_vulkanContext.GraphicsQueue, &presentInfo);
        if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr)
        {
            RecreateSwapchain();
        }
        else if (result != Result.Success)
        {
            throw new Exception("Failed to present frame");
        }
    }

    private void RecreateSwapchain()
    {
        var vk = _vulkanContext!.VulkanApi;
        vk.DeviceWaitIdle(_vulkanContext.Device);

        _framebufferManager?.Dispose();
        _swapchainManager?.Dispose();

        _swapchainManager = new Core.SwapchainManager(
            vk,
            _vulkanContext.Instance,
            _vulkanContext.PhysicalDevice,
            _vulkanContext.Device,
            _surface,
            _vulkanContext.GraphicsQueue,
            (uint)_window.Size.X,
            (uint)_window.Size.Y);

        _framebufferManager = new FramebufferManager(
            vk,
            _vulkanContext.Device,
            _renderPassManager!.RenderPass,
            _swapchainManager.SwapchainImageViews,
            _swapchainManager.SwapchainExtent);
    }

    public unsafe void Dispose()
    {
        if (_vulkanContext != null)
        {
            _vulkanContext.VulkanApi.DeviceWaitIdle(_vulkanContext.Device);
        }

        // Phase 2: Dispose resource managers
        _descriptorManager?.Dispose();
        _uniformBufferManager?.Dispose();
        _meshManager?.Dispose();
        _bufferManager?.Dispose();

        _graphicsPipeline?.Dispose();
        _framebufferManager?.Dispose();
        _renderPassManager?.Dispose();
        _synchronizationManager?.Dispose();
        _commandBufferManager?.Dispose();
        _swapchainManager?.Dispose();

        if (_vulkanContext != null && _surface.Handle != 0)
        {
            _khrSurface!.DestroySurface(_vulkanContext.Instance, _surface, null);
        }

        _vulkanContext?.Dispose();
    }
}