// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Vma;
using Njulf_Framework.Rendering.Core;
using Njulf_Framework.Rendering.Resources;
using Njulf_Framework.Rendering.Resources.Descriptors;
using Njulf_Framework.Rendering.Pipeline;
using Njulf_Framework.Rendering.Data;
using Njulf_Framework.Rendering.Memory;
using Buffer = Silk.NET.Vulkan.Buffer;

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
    private DescriptorManager? _descriptorManager;
    
    private DescriptorSetLayouts? _descriptorSetLayouts;
    private BindlessDescriptorHeap? _bindlessHeap;
    
    private readonly SceneDataBuilder _sceneBuilder;
    private readonly FrameUploadRing _frameUploadRing;

    private Buffer _sceneObjectBuffer;
    private Buffer _sceneMaterialBuffer;
    private Buffer _sceneMeshBuffer;

    private uint _frameIndex;

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

    public unsafe VulkanRenderer(IWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        
        _vulkanContext = new VulkanContext(enableValidationLayers: true);
        
        _bufferManager = new BufferManager(_vulkanContext.VulkanApi, _vulkanContext.VmaAllocator);
        
        _sceneBuilder = new SceneDataBuilder();
        _frameUploadRing = new FrameUploadRing(_bufferManager);
    }

    /// <summary>
    /// Initialize the renderer. Called once at startup.
    /// </summary>
    public unsafe void Load()
    {
        try
        {
            // _vulkanContext = new VulkanContext(enableValidationLayers: true);
            // Console.WriteLine("✓ Vulkan context created");
            
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
                _vulkanContext,
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
            // _bufferManager = new BufferManager(
            //     _vulkanContext.VulkanApi,
            //     _vulkanContext.Device,
            //     _vulkanContext.PhysicalDevice,
            //     _vulkanContext.TransferQueue,
            //     _vulkanContext.TransferQueueFamily);
            //Console.WriteLine("✓ Buffer manager initialized");
            
            _meshManager = new MeshManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _bufferManager);
            Console.WriteLine("✓ Mesh manager initialized");

            // Create bindless descriptor layouts
            _descriptorSetLayouts = new DescriptorSetLayouts(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device);
            Console.WriteLine("✓ Bindless descriptor layouts created");

            // Create bindless descriptor heap
            _bindlessHeap = new BindlessDescriptorHeap(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _descriptorSetLayouts);
            Console.WriteLine("✓ Bindless descriptor heap created");

            // Register scene buffers in bindless heap
            _bindlessHeap.UpdateBuffer(0, _sceneObjectBuffer, 16 * 1024 * 1024);   // Object buffer index 0
            _bindlessHeap.UpdateBuffer(1, _sceneMaterialBuffer, 4 * 1024 * 1024);   // Material buffer index 1
            _bindlessHeap.UpdateBuffer(2, _sceneMeshBuffer, 8 * 1024 * 1024);       // Mesh buffer index 2
            Console.WriteLine("✓ Scene buffers registered in bindless heap");

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
        
        InitializeSceneBuffers();
        
        _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4,
            _window.Size.X / (float)_window.Size.Y,
            0.1f,
            100f);
    }
    
    private void InitializeSceneBuffers()
    {
        if (_bufferManager == null)
            throw new InvalidOperationException("BufferManager not initialized");

        const ulong objectBufferSize   = 16 * 1024 * 1024;   // 16 MB for objects
        const ulong materialBufferSize = 4 * 1024 * 1024;    // 4 MB for materials
        const ulong meshBufferSize     = 8 * 1024 * 1024;    // 8 MB for mesh data

        var storageUsage = BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit;

        var objectHandle = _bufferManager.AllocateBuffer(
            objectBufferSize,
            storageUsage,
            MemoryUsage.AutoPreferDevice);

        var materialHandle = _bufferManager.AllocateBuffer(
            materialBufferSize,
            storageUsage,
            MemoryUsage.AutoPreferDevice);

        var meshHandle = _bufferManager.AllocateBuffer(
            meshBufferSize,
            storageUsage,
            MemoryUsage.AutoPreferDevice);

        _sceneObjectBuffer = _bufferManager.GetBuffer(objectHandle);
        _sceneMaterialBuffer = _bufferManager.GetBuffer(materialHandle);
        _sceneMeshBuffer = _bufferManager.GetBuffer(meshHandle);

        // TODO: Register these in BindlessDescriptorHeap later (Phase 1.3)
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
            _graphicsPipeline == null || _meshManager == null)
        {
            return;
        }

        var vk = _vulkanContext.VulkanApi;
        var device = _vulkanContext.Device;
        var graphicsQueue = _vulkanContext.GraphicsQueue;
        var transferQueue = _vulkanContext.TransferQueue;

        // Get this frame's resources (per-frame)
        var frameIndex = _currentFrameIndex % MaxFramesInFlight;
        var inFlightFence = _synchronizationManager.InFlightFences[frameIndex];
        var imageAvailableSemaphore = _synchronizationManager.ImageAvailableSemaphores[frameIndex];
        var transferFinishedSemaphore = _synchronizationManager.TransferFinishedSemaphores[frameIndex];

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
        
        _sceneBuilder.BeginFrame();
        foreach (var kvp in _renderObjects)
        {
            var obj = kvp.Value;
            if (obj != null && obj.Visible)
            {
                _sceneBuilder.AddObject(obj);
            }
        }
        
        var transferCommandBuffer = _commandBufferManager.TransferCommandBuffers[frameIndex];
        _commandBufferManager.ResetCommandBuffer(transferCommandBuffer);
        _commandBufferManager.BeginRecording(transferCommandBuffer);

        // Write CPU scene data to staging buffer and record copy commands
        _sceneBuilder.UploadToGPU(
            transferCommandBuffer,
            _frameUploadRing,
            _sceneObjectBuffer,
            _sceneMaterialBuffer,
            _sceneMeshBuffer);

        _commandBufferManager.EndRecording(transferCommandBuffer);

        // Submit transfer work (asynchronously on transfer queue)
        SubmitTransferCommandBuffer(transferCommandBuffer, transferFinishedSemaphore);
        

        // Record commands
        var commandBuffer = _commandBufferManager.CommandBuffers[frameIndex];
        _commandBufferManager.ResetCommandBuffer(commandBuffer);
        _commandBufferManager.BeginRecording(commandBuffer);

        RecordRenderCommands(commandBuffer, imageIndex, frameIndex);

        _commandBufferManager.EndRecording(commandBuffer);

        // Submit command buffer
        SubmitCommandBuffer(graphicsQueue, commandBuffer, imageAvailableSemaphore, transferFinishedSemaphore,
            renderFinishedSemaphore, inFlightFence);

        // Present frame
        PresentFrame(imageIndex, renderFinishedSemaphore);
        
        _frameUploadRing.NextFrame(); 
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
        var colorImageView = _swapchainManager!.SwapchainImageViews[imageIndex];
        
        // ✅ DYNAMIC RENDERING: Define attachments inline
        // Clear color: dark gray (0.1, 0.1, 0.1)
        var colorAttachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = colorImageView,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = new ClearValue 
            { 
                Color = new ClearColorValue(0.1f, 0.1f, 0.1f, 1.0f)
            }
        };

        // Depth attachment (if needed - optional for now)
        // var depthAttachment = new RenderingAttachmentInfo
        // {
        //     SType = StructureType.RenderingAttachmentInfo,
        //     ImageView = _depthImageView,
        //     ImageLayout = ImageLayout.DepthAttachmentOptimal,
        //     LoadOp = AttachmentLoadOp.Clear,
        //     StoreOp = AttachmentStoreOp.DontCare,
        //     ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) }
        // };

        // ✅ Create rendering info (replaces RenderPassBeginInfo + Framebuffer)
        var renderingInfo = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D
            {
                Offset = new Offset2D { X = 0, Y = 0 },
                Extent = _swapchainManager.SwapchainExtent
            },
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachment
            // PDepthAttachment = &depthAttachment,  // Uncomment when depth is needed
        };

        // ✅ Begin dynamic rendering (NO render pass needed!)
        vk.CmdBeginRendering(commandBuffer, &renderingInfo);

        // Bind pipeline
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _graphicsPipeline!.Pipeline);
        
        // Bind bindless descriptor sets (buffers + textures)
        var descriptorSets = stackalloc DescriptorSet[2] 
        { 
            _bindlessHeap!.BufferSet, 
            _bindlessHeap!.TextureSet 
        };
        vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, 
            _graphicsPipeline!.PipelineLayout, 0, 2, descriptorSets, 0, null);
        
        // Phase 2: Render all visible objects
        foreach (var renderObj in _renderObjects.Values)
        {
            if (!renderObj.Visible)
                continue;
            
        
            // Get mesh GPU data (cached if already uploaded)
            var meshGpu = _meshManager!.GetOrCreateMeshGpu(renderObj.Mesh);
        
            // Bind mesh
            _meshManager.BindMesh(commandBuffer, meshGpu);
                
            // Draw
            _meshManager.DrawMesh(commandBuffer, meshGpu);
        }
        
        // ✅ End dynamic rendering (replaces vkCmdEndRenderPass)
        vk.CmdEndRendering(commandBuffer);
    }

    
    private unsafe void SubmitTransferCommandBuffer(CommandBuffer transferCmd, Semaphore transferFinishedSemaphore)
    {
        var vk = _vulkanContext.VulkanApi;
        var transferQueue = _vulkanContext.TransferQueue;

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &transferCmd,
            SignalSemaphoreCount = 1,           
            PSignalSemaphores = &transferFinishedSemaphore 
        };

        var result = vk.QueueSubmit(transferQueue, 1, &submitInfo, default);
        if (result != Result.Success)
            throw new Exception($"Transfer queue submit failed: {result}");
    }

    private unsafe void SubmitCommandBuffer(Queue queue, CommandBuffer commandBuffer,
        Semaphore waitImageReadySemaphore, Semaphore waitTransferFinishedSemaphore, Semaphore signalRenderSemaphore, Fence fence)
    {
        var vk = _vulkanContext!.VulkanApi;
        
        var waitSemaphores = stackalloc Semaphore[2]
        {
            waitImageReadySemaphore,
            waitTransferFinishedSemaphore
        };

        var waitStages = stackalloc PipelineStageFlags[2]
        {
            PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.TopOfPipeBit
        };
        
        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 2,
            PWaitSemaphores = waitSemaphores,
            PWaitDstStageMask = waitStages,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalRenderSemaphore
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
        _meshManager?.Dispose();
        _bufferManager?.Dispose();
        
        _bindlessHeap?.Dispose();
        _descriptorSetLayouts?.Dispose();

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