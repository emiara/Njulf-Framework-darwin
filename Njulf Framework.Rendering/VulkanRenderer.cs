// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using System.Linq;
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

public unsafe class VulkanRenderer : IDisposable
{
    private readonly IWindow _window;
    private readonly VulkanContext? _vulkanContext;
    private SwapchainManager? _swapchainManager;
    private CommandBufferManager? _commandBufferManager;
    private SynchronizationManager? _synchronizationManager;
    private RenderPassManager? _renderPassManager;
    private FramebufferManager? _framebufferManager;
    private GraphicsPipeline? _graphicsPipeline;
    private GraphicsPipeline? _forwardPlusPipeline;
    
    private KhrSwapchain? _khrSwapchain;
    private KhrSurface? _khrSurface;

    // Phase 2: Resource managers
    private readonly BufferManager? _bufferManager;
    private MeshManager? _meshManager;
    private DescriptorManager? _descriptorManager;
    
    private DescriptorSetLayouts? _descriptorSetLayouts;
    private BindlessDescriptorHeap? _bindlessHeap;
    
    private readonly SceneDataBuilder _sceneBuilder;
    private readonly FrameUploadRing _frameUploadRing;

    private Buffer _sceneObjectBuffer;
    private Buffer _sceneMaterialBuffer;
    private Buffer _sceneMeshBuffer;
    
    private RenderGraph? _renderGraph;
    private ImageView _depthImageView;
    private Buffer _depthBuffer;
    private Image _depthImage;
    private Allocation* _depthAllocation;
    private ImageLayout _depthImageLayout = ImageLayout.Undefined;
    private ImageLayout[]? _swapchainImageLayouts;
 

    // Phase 3.5: Forward+ lighting
    private LightManager? _lightManager;
    private TiledLightCullingPass? _tiledLightCullingPass;
    
    private uint _frameIndex;

    // Phase 2: Scene objects
    private Dictionary<string, Data.RenderingData.RenderObject> _renderObjects = new();

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
    
    public LightManager? LightManager => _lightManager;

    public VulkanRenderer(IWindow window)
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
    public void Load()
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
            _swapchainImageLayouts = Enumerable.Repeat(ImageLayout.Undefined, (int)_swapchainManager.SwapchainImageCount).ToArray();

            // Create depth buffer and image view for rendering
            CreateDepthResources();
            Console.WriteLine("✓ Depth resources created");

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
                _vulkanContext.Device,
                _vulkanContext.PhysicalDevice);
            Console.WriteLine("✓ Bindless descriptor layouts created");

            // Create bindless descriptor heap
            _bindlessHeap = new BindlessDescriptorHeap(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _descriptorSetLayouts);
            Console.WriteLine("✓ Bindless descriptor heap created");
            
            InitializeSceneBuffers();

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
                new[]  // ✅ Array of 2 layouts
                {
                    _descriptorSetLayouts.BufferHeapLayout,   // Set 0
                    _descriptorSetLayouts.TextureHeapLayout   // Set 1
                });
            Console.WriteLine("✓ Graphics pipeline created");
            
            _forwardPlusPipeline = new GraphicsPipeline(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device, 
                _renderPassManager.RenderPass, 
                _swapchainManager.SwapchainExtent, 
                new[]  // ✅ Array of 2 layouts
                {
                    _descriptorSetLayouts.BufferHeapLayout,   // Set 0
                    _descriptorSetLayouts.TextureHeapLayout   // Set 1
                },
                _swapchainManager.SwapchainImageFormat, Format.D32Sfloat,
                "Shaders/vertex.vert",           // ← Same vertex shader
                "Shaders/forward_plus.frag"); 
            
            // Phase 3.5: Initialize light manager
            _lightManager = new LightManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _bufferManager,
                _bindlessHeap);
            Console.WriteLine("✓ Light manager initialized");

            // Phase 3.5: Initialize tiled light culling pass
            _tiledLightCullingPass = new TiledLightCullingPass(
                "Tiled Light Culling",
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _lightManager,
                _bufferManager,
                _descriptorSetLayouts,
                _bindlessHeap);
            Console.WriteLine("✓ Tiled light culling pass initialized");
            
            // Setup render graph with passes
            SetupRenderGraph();
            Console.WriteLine("✓ Render graph initialized");

            // Phase 2: Add test cube to scene
            var cubeMesh = Data.RenderingData.Mesh.CreateCube();
            var material = new Data.RenderingData.Material("default", "Shaders/test_vert.spv", "");
            var cube = new Data.RenderingData.RenderObject("test_cube", cubeMesh, material, Matrix4x4.Identity);
            AddRenderObject(cube);
            Console.WriteLine("✓ Test cube added to scene");
            
            _meshManager.Finalize();
            Console.WriteLine("✓ Mesh manager finalized");

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

    private void CreateDepthResources()
    {
        if (_bufferManager == null || _vulkanContext == null)
            throw new InvalidOperationException("Prerequisites not initialized");

        var extent = _swapchainManager!.SwapchainExtent;

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.D32Sfloat,
            Extent = new Extent3D(extent.Width, extent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.AutoPreferDevice
        };

        Image image;
        Allocation* allocation;
        AllocationInfo allocationInfo;

        var result = Apis.CreateImage(
            _vulkanContext.VmaAllocator,
            &imageInfo,
            &allocInfo,
            &image,
            &allocation,
            &allocationInfo);

        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to allocate depth image: {result}");

        _depthImage = image;
        _depthAllocation = allocation;
        
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _depthImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.D32Sfloat,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.DepthBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        _vulkanContext.VulkanApi.CreateImageView(_vulkanContext.Device, &viewInfo, null, out _depthImageView);
    }
    
    private void SetupRenderGraph()
    {
        if (_vulkanContext == null || _graphicsPipeline == null || _bindlessHeap == null)
            throw new InvalidOperationException("Prerequisites not initialized");

        _renderGraph = new RenderGraph("MainRenderGraph");
        
        // Stage 1: Tiled light culling (compute shader)
        // This runs once per frame and outputs per-tile light lists
        if (_tiledLightCullingPass != null)
        {
            _renderGraph.AddPass(_tiledLightCullingPass);
            Console.WriteLine("  └─ Added: Tiled Light Culling (compute)");
        }
        
        // Stage 2: Forward+ raster pass
        // Fragment shader reads per-tile light lists and shades accordingly
        var rasterPass = new DynamicRasterPass(
            _vulkanContext!.VulkanApi,
            _vulkanContext!.Device,
            _swapchainManager!.SwapchainImageViews[0],  // Color: swapchain
            default,                                     // Depth: none
            _forwardPlusPipeline!,                          // Pipeline: forward+ shading
            new Vector4(0.1f, 0.1f, 0.1f, 1.0f)); 
        _renderGraph.AddPass(rasterPass);
        Console.WriteLine("  └─ Added: Forward+ Shading (raster)");

        Console.WriteLine($"✓ Render graph setup with {_renderGraph.PassCount} pass(es)");

        // Create a single G-Buffer pass for now (can add RT pass later)
        // var gBufferPass = new DynamicRasterPass(
        //     vk: _vulkanContext.VulkanApi,
        //     device: _vulkanContext.Device,
        //     colorAttachment: default,  // Will be set per-frame
        //     depthAttachment: _depthImageView,
        //     pipeline: _graphicsPipeline,
        //     clearColor: new Vector4(0.1f, 0.1f, 0.1f, 1.0f))
        // {
        //     Name = "G-Buffer"
        // };
        // _renderGraph.AddPass(gBufferPass);

        // Future: Add reflection pass
        // var reflectionPass = new DynamicRayTracingPass(...);
        // _renderGraph.AddPass(reflectionPass);

        // Future: Add composite pass
        // var compositePass = new DynamicRasterPass(...);
        // _renderGraph.AddPass(compositePass);
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
        
        // Add test lights (temporary - for demonstration)
        if (_frameIndex == 0 && _lightManager != null)
        {
            _lightManager.AddPointLight(new Vector3(5, 3, 0), 10, new Vector3(1, 1, 1), 1.0f);
            _lightManager.AddPointLight(new Vector3(-5, 3, 0), 10, new Vector3(1, 0, 0), 0.5f);
            _lightManager.AddPointLight(new Vector3(0, 3, 5), 10, new Vector3(0, 1, 0), 0.5f);
            Console.WriteLine("✓ Test lights added to scene");

            _frameIndex++;
        }
    }

    /// <summary>
    /// Render logic. Called once per frame.
    /// </summary>
    public void Draw()
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

        // Upload mesh data once (no-op for already uploaded meshes)
        foreach (var renderObject in _renderObjects.Values)
        {
            if (renderObject?.Mesh != null)
            {
                _meshManager.UploadMeshToGPU(renderObject.Mesh, transferCommandBuffer, _frameUploadRing);
            }
        }

        // Write CPU scene data to staging buffer and record copy commands
        _sceneBuilder.UploadToGPU(
            _vulkanContext.VulkanApi,
            transferCommandBuffer,
            _frameUploadRing,
            _sceneObjectBuffer,
            _sceneMaterialBuffer,
            _sceneMeshBuffer);
        
        // Phase 3.5: Upload lights
        _lightManager?.UploadToGPU(transferCommandBuffer, _frameUploadRing);

        _commandBufferManager.EndRecording(transferCommandBuffer);

        // Submit transfer work (asynchronously on transfer queue)
        SubmitTransferCommandBuffer(transferCommandBuffer, transferFinishedSemaphore);
        

        // Record commands
        var commandBuffer = _commandBufferManager.CommandBuffers[frameIndex];
        _commandBufferManager.ResetCommandBuffer(commandBuffer);
        _commandBufferManager.BeginRecording(commandBuffer);
        
        // Transition: tracked layout -> COLOR_ATTACHMENT_OPTIMAL
        var swapchainImage = _swapchainManager.SwapchainImages[imageIndex];
        var oldSwapchainLayout = _swapchainImageLayouts![imageIndex];
        TransitionImageLayout(commandBuffer, swapchainImage,
            oldSwapchainLayout, ImageLayout.ColorAttachmentOptimal);
        _swapchainImageLayouts[imageIndex] = ImageLayout.ColorAttachmentOptimal;

        var rgContext = new RenderGraphContext(
            _swapchainManager.SwapchainExtent.Width,
            _swapchainManager.SwapchainExtent.Height,
            _bindlessHeap)
        {
            ColorAttachmentView = _swapchainManager.SwapchainImageViews[imageIndex],
            DepthAttachmentView = _depthImageView,
            FrameIndex = frameIndex,
            VisibleObjects = _renderObjects.Values.Where(o => o.Visible).ToList(),
            ViewProjection = _viewMatrix * _projectionMatrix,
            CameraPosition = new Vector3(0, 2, 3),
            BindlessHeap = _bindlessHeap,
            MeshManager = _meshManager
        };

        if (_depthImage.Handle != 0 && _depthImageLayout != ImageLayout.DepthAttachmentOptimal)
        {
            TransitionImageLayout(commandBuffer, _depthImage,
                _depthImageLayout, ImageLayout.DepthAttachmentOptimal, ImageAspectFlags.DepthBit);
            _depthImageLayout = ImageLayout.DepthAttachmentOptimal;
        }

        _renderGraph?.Execute(commandBuffer, rgContext);

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
    public void AddRenderObject(Data.RenderingData.RenderObject obj)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        _renderObjects[obj.Name] = obj;
        
        if (_meshManager != null)
        {
            _meshManager.RegisterMesh(obj.Mesh);
        }
        
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
    public Data.RenderingData.RenderObject? GetRenderObject(string name)
    {
        _renderObjects.TryGetValue(name, out var obj);
        return obj;
    }

    private SurfaceKHR CreateSurface()
    {
        SurfaceKHR surface = new SurfaceKHR();
        
        if (_vulkanContext != null)
        {
            surface = _window!.VkSurface!.Create<AllocationCallbacks>(_vulkanContext.Instance.ToHandle(), null).ToSurface();
        }
        
        return surface;
    }

    private void RecordRenderCommands(CommandBuffer commandBuffer, uint imageIndex, uint frameIndex)
    {
        if (_renderGraph == null || _swapchainManager == null || _forwardPlusPipeline == null || _bindlessHeap == null)
            throw new InvalidOperationException("RenderGraph, SwapchainManager, Pipeline, or BindlessHeap not initialized");
        
        Vk _vk = _vulkanContext.VulkanApi;
        
        // Get the swapchain image for layout transition
        var swapchainImage = _swapchainManager.SwapchainImages[imageIndex];

        // Build render context
        var context = new RenderGraphContext(
            _swapchainManager.SwapchainExtent.Width,
            _swapchainManager.SwapchainExtent.Height,
            _bindlessHeap)
        {
            Width = _swapchainManager.SwapchainExtent.Width,
            Height = _swapchainManager.SwapchainExtent.Height,
            FrameIndex = frameIndex,
            VisibleObjects = _renderObjects.Values
                .Where(obj => obj != null && obj.Visible)
                .ToList(),
            ViewProjection = _viewMatrix * _projectionMatrix,
            CameraPosition = new Vector3(0, 2, 3),
            BindlessHeap = _bindlessHeap,
            MeshManager = _meshManager
        };

        // Get the swapchain image view for this frame
        var colorImageView = _swapchainManager.SwapchainImageViews[imageIndex];
        if (colorImageView.Handle == 0)
            throw new InvalidOperationException($"Failed to get swapchain image view for index {imageIndex}");

        // Define color attachment (swapchain image)
        var colorAttachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = colorImageView,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = new ClearValue { Color = new ClearColorValue(0.1f, 0.1f, 0.1f, 1.0f) }
        };

        // Define depth attachment (if you have one)
        RenderingAttachmentInfo* depthAttachment = null;
        RenderingAttachmentInfo depthAttachmentInfo = default;
        if (_depthImageView.Handle != 0)
        {
            depthAttachmentInfo = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = _depthImageView,
                ImageLayout = ImageLayout.DepthAttachmentOptimal,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.DontCare,
                ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) }
            };
            depthAttachment = &depthAttachmentInfo;
        }

        // Begin dynamic rendering
        var renderingInfo = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D
            {
                Offset = new Offset2D(0, 0),
                Extent = _swapchainManager.SwapchainExtent
            },
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachment,
            PDepthAttachment = depthAttachment
        };

        _vk.CmdBeginRendering(commandBuffer, &renderingInfo);

        // Bind graphics pipeline
        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _forwardPlusPipeline.Pipeline);

        // Bind descriptor sets (set 0 = buffers, set 1 = textures)
        var descriptorSets = stackalloc DescriptorSet[]
        {
            _bindlessHeap.BufferSet,
            _bindlessHeap.TextureSet
        };
        _vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _forwardPlusPipeline.PipelineLayout,
                                  0, 2, descriptorSets, 0, null);

        // Set viewport
        var viewport = new Viewport
        {
            X = 0,
            Y = 0,
            Width = _swapchainManager.SwapchainExtent.Width,
            Height = _swapchainManager.SwapchainExtent.Height,
            MinDepth = 0,
            MaxDepth = 1
        };
        _vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);

        // Set scissor
        var scissor = new Rect2D
        {
            Offset = new Offset2D(0, 0),
            Extent = _swapchainManager.SwapchainExtent
        };
        _vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);

        // Draw all visible objects
        _meshManager.BindMeshBuffers(commandBuffer);

        // Draw all visible objects
        foreach (var renderObject in context.VisibleObjects)
        {
            if (renderObject?.Mesh == null)
                continue;

            var mesh = renderObject.Mesh;

            // ✅ Use PushConstants from RenderingData
            var pushConstants = new Data.RenderingData.PushConstants
            {
                Model = renderObject.Transform,
                View = _viewMatrix,
                Projection = _projectionMatrix,
                MaterialIndex = 0,
                MeshIndex = 0,
                InstanceIndex = 0,
                Padding = 0
            };

            _vk.CmdPushConstants(commandBuffer, _forwardPlusPipeline.PipelineLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                0, (uint)sizeof(Data.RenderingData.PushConstants), &pushConstants);

            _meshManager.DrawMesh(commandBuffer, mesh);
        }

        // End dynamic rendering
        _vk.CmdEndRendering(commandBuffer);
        
        // Transition: COLOR_ATTACHMENT_OPTIMAL -> PRESENT_SRC_KHR
        TransitionImageLayout(commandBuffer, swapchainImage,
            ImageLayout.ColorAttachmentOptimal, ImageLayout.PresentSrcKhr);
        _swapchainImageLayouts![imageIndex] = ImageLayout.PresentSrcKhr;
    }



    
    private void SubmitTransferCommandBuffer(CommandBuffer transferCmd, Semaphore transferFinishedSemaphore)
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

    private void SubmitCommandBuffer(Queue queue, CommandBuffer commandBuffer,
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
            PipelineStageFlags.AllCommandsBit
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

    private void PresentFrame(uint imageIndex, Semaphore renderFinishedSemaphore)
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
        _swapchainImageLayouts = Enumerable.Repeat(ImageLayout.Undefined, (int)_swapchainManager.SwapchainImageCount).ToArray();

        _framebufferManager = new FramebufferManager(
            vk,
            _vulkanContext.Device,
            _renderPassManager!.RenderPass,
            _swapchainManager.SwapchainImageViews,
            _swapchainManager.SwapchainExtent);
    }
    
    private void TransitionImageLayout(CommandBuffer commandBuffer, Image image, 
        ImageLayout oldLayout, ImageLayout newLayout)
    {
        TransitionImageLayout(commandBuffer, image, oldLayout, newLayout, ImageAspectFlags.ColorBit);
    }

    private void TransitionImageLayout(CommandBuffer commandBuffer, Image image, 
        ImageLayout oldLayout, ImageLayout newLayout, ImageAspectFlags aspectMask)
    {
        var vk = _vulkanContext!.VulkanApi;

        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = aspectMask,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        PipelineStageFlags sourceStage;
        PipelineStageFlags destinationStage;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.ColorAttachmentWriteBit;
            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.ColorAttachmentOutputBit;
        }
        else if (oldLayout == ImageLayout.PresentSrcKhr && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.ColorAttachmentWriteBit;
            sourceStage = PipelineStageFlags.BottomOfPipeBit;
            destinationStage = PipelineStageFlags.ColorAttachmentOutputBit;
        }
        else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.DepthAttachmentOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit;
            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.EarlyFragmentTestsBit;
        }
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal && newLayout == ImageLayout.PresentSrcKhr)
        {
            barrier.SrcAccessMask = AccessFlags.ColorAttachmentWriteBit;
            barrier.DstAccessMask = 0;
            sourceStage = PipelineStageFlags.ColorAttachmentOutputBit;
            destinationStage = PipelineStageFlags.BottomOfPipeBit;
        }
        else
        {
            throw new ArgumentException("Unsupported layout transition");
        }

        vk.CmdPipelineBarrier(commandBuffer, sourceStage, destinationStage, 0, 
            0, null, 0, null, 1, &barrier);
    }

    public void Dispose()
    {
        if (_vulkanContext != null)
        {
            _vulkanContext.VulkanApi.DeviceWaitIdle(_vulkanContext.Device);
        }

        // Phase 2: Dispose resource managers
        _descriptorManager?.Dispose();
        _meshManager?.Dispose();
        _bufferManager?.Dispose();
        _lightManager?.Dispose();
        _tiledLightCullingPass?.Dispose();
        
        _bindlessHeap?.Dispose();
        _descriptorSetLayouts?.Dispose();

        _graphicsPipeline?.Dispose();
        _framebufferManager?.Dispose();
        _renderPassManager?.Dispose();
        _synchronizationManager?.Dispose();
        _commandBufferManager?.Dispose();
        _swapchainManager?.Dispose();
        _renderGraph = null;
        
        if (_depthImageView.Handle != 0)
            _vulkanContext.VulkanApi.DestroyImageView(_vulkanContext.Device, _depthImageView, null);
        if (_depthImage.Handle != 0)
            Apis.DestroyImage(_vulkanContext.VmaAllocator, _depthImage, null);

        if (_vulkanContext != null && _surface.Handle != 0)
        {
            _khrSurface!.DestroySurface(_vulkanContext.Instance, _surface, null);
        }

        _vulkanContext?.Dispose();
    }
}
