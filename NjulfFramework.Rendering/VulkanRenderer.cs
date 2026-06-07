// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using NjulfFramework.Core.Interfaces.Assets;
using NjulfFramework.Core.Interfaces.Rendering;
using NjulfFramework.Rendering.Core;
using NjulfFramework.Rendering.Data;
using NjulfFramework.Rendering.Memory;
using NjulfFramework.Rendering.Pipeline;
using NjulfFramework.Rendering.Resources;
using NjulfFramework.Rendering.Resources.Descriptors;
using NjulfFramework.Rendering.Resources.Handles;
using static NjulfFramework.Rendering.Resources.Descriptors.BindlessBufferIndices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Vma;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace NjulfFramework.Rendering;

public unsafe class VulkanRenderer : IRenderer, ISceneLoader
{
    private const uint MaxFramesInFlight = 2;

    // Phase 2: Resource managers
    private readonly BufferManager? _bufferManager;
    private readonly FrameUploadRing _frameUploadRing;
    private readonly BufferResizer _bufferResizer;
    private TextureManager? _textureManager;
    private LightManager? _lightManagerImpl; // concrete type for internal Vulkan calls

    // Phase 2: Scene objects
    private readonly Dictionary<string, Data.RenderingData.RenderObject> _renderObjects = new();
    private readonly Dictionary<string, List<string>> _modelToRenderObjectNames = new();
    private readonly ConcurrentQueue<ScenePayload> _scenePayloadQueue = new();
    private FenceBasedBufferDeleter? _bufferDeleter;
    private readonly object _bufferResizeLock = new object(); // Thread safety for buffer resizing

    private SceneDataBuilder? _sceneBuilder;
    private BufferSizes _bufferSizes;

    // Buffer handles for dynamic resizing
    private BufferHandle _sceneObjectBufferHandle;
    private BufferHandle _sceneMaterialBufferHandle;
    private BufferHandle _sceneMeshBufferHandle;
    private BufferHandle[] _instanceBufferHandles = new BufferHandle[2];
    private BufferHandle[] _meshletDrawBufferHandles = new BufferHandle[2];

    // Camera - provided via DI, used for view/projection matrices
    private readonly ICamera _camera;
    private readonly VulkanContext? _vulkanContext;
    private readonly IWindow _window;
    private BindlessDescriptorHeap? _bindlessHeap;
    private CommandBufferManager? _commandBufferManager;
    private uint _currentFrameIndex;
    private Allocation* _depthAllocation;
    private Buffer _depthBuffer;
    private Image _depthImage;
    private ImageLayout _depthImageLayout = ImageLayout.Undefined;
    private ImageView _depthImageView;
    private DescriptorManager? _descriptorManager;

    private DescriptorSetLayouts? _descriptorSetLayouts;
    private ExtMeshShader? _extMeshShader;

    private uint _frameIndex;
    private KhrSurface? _khrSurface;

    private KhrSwapchain? _khrSwapchain;


    // Bindless buffer indices - using centralized constants for maintainability
    // These values must match BindlessBufferIndices.cs and shader expectations
    private MeshManager? _meshManager;
    private MeshPipeline? _meshPipeline;

    private RenderGraph? _renderGraph;
    private Buffer _sceneMaterialBuffer;
    private Buffer _sceneMeshBuffer;

    private Buffer _sceneObjectBuffer;
    private Buffer[] _instanceBuffers = new Buffer[2];
    private Buffer[] _meshletDrawBuffers = new Buffer[2];
    private int _currentSceneBufferIndex = 0;
    private Sampler _defaultSampler;

    private SurfaceKHR _surface;
    private ImageLayout[]? _swapchainImageLayouts;
    private SwapchainManager? _swapchainManager;
    private SynchronizationManager? _synchronizationManager;
    private TiledLightCullingPass? _tiledLightCullingPass;

    public VulkanRenderer(IWindow window, ICamera camera, BufferSizes? bufferSizes = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _bufferSizes = bufferSizes ?? BufferSizes.Default;

        _vulkanContext = new VulkanContext(enableValidationLayers: true);
        _bufferManager = new BufferManager(_vulkanContext.VulkanApi, _vulkanContext.VmaAllocator);
        _bufferResizer = new BufferResizer(_bufferManager);
        _frameUploadRing = new FrameUploadRing(_bufferManager);
        _bufferDeleter = new FenceBasedBufferDeleter(_vulkanContext.VulkanApi, _vulkanContext.Device, _bufferManager);
    }

    public VulkanContext VulkanContext => _vulkanContext!;
    public SwapchainManager SwapchainManager => _swapchainManager!;

    
    public ILightManager? LightManager => _lightManagerImpl;

    /// <summary>
    /// Sets a debug name for a Vulkan object using VK_EXT_debug_utils.
    /// </summary>
    private unsafe void SetDebugName<T>(T handle, ObjectType objectType, string name) where T : unmanaged
    {
        // Try to get debug utils extension
        if (!_vulkanContext!.VulkanApi.TryGetInstanceExtension(_vulkanContext.Instance, out ExtDebugUtils debugUtils))
            return; // Debug utils not available

        ulong handleValue = 0;
        if (handle is Image img) handleValue = img.Handle;
        else if (handle is ImageView iv) handleValue = iv.Handle;
        else if (handle is Sampler s) handleValue = s.Handle;
        else if (handle is SurfaceKHR surf) handleValue = surf.Handle;
        else if (handle is Buffer buf) handleValue = buf.Handle;
        else return;

        if (handleValue == 0) return;

        var namePtr = SilkMarshal.StringToPtr(name);

        try
        {
            var debugNameInfo = new DebugUtilsObjectNameInfoEXT
            {
                SType = StructureType.DebugUtilsObjectNameInfoExt,
                ObjectType = objectType,
                ObjectHandle = handleValue,
                PObjectName = (byte*)namePtr
            };
            debugUtils.SetDebugUtilsObjectName(_vulkanContext.Device, &debugNameInfo);
        }
        finally
        {
            SilkMarshal.Free((nint)namePtr);
        }
    }

    /// <summary>
    /// Validates and returns a supported depth format.
    /// </summary>
    private Format FindSupportedDepthFormat()
    {
        var candidates = new[]
        {
            Format.D32Sfloat,
            Format.D32SfloatS8Uint,
            Format.D24UnormS8Uint,
            Format.D16UnormS8Uint,
            Format.D16Unorm
        };

        foreach (var format in candidates)
        {
            var formatProps = new FormatProperties();
            _vulkanContext!.VulkanApi.GetPhysicalDeviceFormatProperties(
                _vulkanContext.PhysicalDevice,
                format,
                &formatProps);

            if ((formatProps.OptimalTilingFeatures & FormatFeatureFlags.DepthStencilAttachmentBit) != 0)
                return format;
        }

        throw new Exception("No supported depth format found");
    }

    /// <summary>
    /// CPU-only payload for model scene data. Built on any thread, consumed on render thread.
    /// </summary>
    public sealed class ScenePayload
    {
        public string ModelName { get; set; } = string.Empty;
        public Matrix4x4 ModelTransform { get; set; }
        public List<Data.RenderingData.RenderObject> RenderObjects { get; } = new();
        public List<string> RenderObjectNames { get; } = new();
    }

    public void Dispose()
    {
        if (_vulkanContext != null) _vulkanContext.VulkanApi.DeviceWaitIdle(_vulkanContext.Device);

        // Phase 2: Dispose resource managers
        _descriptorManager?.Dispose();
        _meshManager?.Dispose();
        _textureManager?.Dispose();
        _bufferManager?.Dispose();
        _lightManagerImpl?.Dispose();
        _tiledLightCullingPass?.Dispose();

        _bindlessHeap?.Dispose();
        _descriptorSetLayouts?.Dispose();
        
        _meshPipeline?.Dispose();
        _synchronizationManager?.Dispose();
        _commandBufferManager?.Dispose();
        
        // Destroy swapchain BEFORE surface (Vulkan spec requirement)
        _swapchainManager?.Dispose();
        _renderGraph = null;

        if (_depthImageView.Handle != 0)
            _vulkanContext.VulkanApi.DestroyImageView(_vulkanContext.Device, _depthImageView, null);
        if (_depthImage.Handle != 0)
        {
            Apis.DestroyImage(_vulkanContext.VmaAllocator, _depthImage, _depthAllocation);
            _depthAllocation = null;
        }
        
        if (_defaultSampler.Handle != 0)
            _vulkanContext.VulkanApi.DestroySampler(_vulkanContext.Device, _defaultSampler, null);

        // Surface must be destroyed AFTER all swapchains created from it
        if (_vulkanContext != null && _surface.Handle != 0)
            _khrSurface!.DestroySurface(_vulkanContext.Instance, _surface, null);

        _vulkanContext?.Dispose();
    }

    /// <summary>
    /// Ensures scene buffers have sufficient capacity for the current frame.
    /// </summary>
    private void EnsureBufferCapacityForScene()
    {
        if (_sceneBuilder == null || _bindlessHeap == null || _bufferManager == null) return;
        lock (_bufferResizeLock)
        {
            // Estimate required buffer sizes
            ulong requiredObjectBufferSize = _sceneBuilder.EstimateObjectBufferSize();
            ulong requiredMaterialBufferSize = _sceneBuilder.EstimateMaterialBufferSize();
            ulong requiredMeshBufferSize = _sceneBuilder.EstimateMeshBufferSize();

            // Resize buffers if needed
            _sceneObjectBufferHandle = _bufferResizer.EnsureBufferCapacity(
                _sceneObjectBufferHandle,
                requiredObjectBufferSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.AutoPreferDevice);
            _sceneObjectBuffer = _bufferManager.GetBuffer(_sceneObjectBufferHandle);
            _bindlessHeap.UpdateBuffer(ObjectBuffer, _sceneObjectBuffer, _bufferManager.GetBufferSize(_sceneObjectBufferHandle));

            _sceneMaterialBufferHandle = _bufferResizer.EnsureBufferCapacity(
                _sceneMaterialBufferHandle,
                requiredMaterialBufferSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.AutoPreferDevice);
            _sceneMaterialBuffer = _bufferManager.GetBuffer(_sceneMaterialBufferHandle);
            _bindlessHeap.UpdateBuffer(MaterialBuffer, _sceneMaterialBuffer, _bufferManager.GetBufferSize(_sceneMaterialBufferHandle));

            _sceneMeshBufferHandle = _bufferResizer.EnsureBufferCapacity(
                _sceneMeshBufferHandle,
                requiredMeshBufferSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.AutoPreferDevice);
            _sceneMeshBuffer = _bufferManager.GetBuffer(_sceneMeshBufferHandle);
            _bindlessHeap.UpdateBuffer(SceneMeshBuffer, _sceneMeshBuffer, _bufferManager.GetBufferSize(_sceneMeshBufferHandle));
        }
    }

    /// <summary>
    /// Ensures frame-specific buffers have sufficient capacity.
    /// </summary>
    private void EnsureBufferCapacityForFrame(uint frameIndex)
    {
        if (_sceneBuilder == null || _bindlessHeap == null || _bufferManager == null) return;
        lock (_bufferResizeLock)
        {
            // Estimate required buffer sizes
            ulong requiredInstanceBufferSize = _sceneBuilder.EstimateInstanceBufferSize();
            ulong requiredMeshletDrawBufferSize = _sceneBuilder.EstimateMeshletDrawBufferSize();

            // Resize buffers if needed
            _instanceBufferHandles[frameIndex] = _bufferResizer.EnsureBufferCapacity(
                _instanceBufferHandles[frameIndex],
                requiredInstanceBufferSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.AutoPreferDevice);
            _instanceBuffers[frameIndex] = _bufferManager.GetBuffer(_instanceBufferHandles[frameIndex]);
            _bindlessHeap.UpdateBuffer(InstanceBufferBase + frameIndex, _instanceBuffers[frameIndex], _bufferManager.GetBufferSize(_instanceBufferHandles[frameIndex]));

            _meshletDrawBufferHandles[frameIndex] = _bufferResizer.EnsureBufferCapacity(
                _meshletDrawBufferHandles[frameIndex],
                requiredMeshletDrawBufferSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.AutoPreferDevice);
            _meshletDrawBuffers[frameIndex] = _bufferManager.GetBuffer(_meshletDrawBufferHandles[frameIndex]);
            _bindlessHeap.UpdateBuffer(MeshletDrawBufferBase + frameIndex, _meshletDrawBuffers[frameIndex], _bufferManager.GetBufferSize(_meshletDrawBufferHandles[frameIndex]));
        }
    }

    /// <summary>
    ///     Initialize the renderer. Called once at startup.
    /// </summary>
    public void Load()
    {
        try
        {
            // _vulkanContext = new VulkanContext(enableValidationLayers: true);
            // Console.WriteLine("✓ Vulkan context created");

            // Get the KhrSwapchain extension
            if (!_vulkanContext.VulkanApi.TryGetDeviceExtension(_vulkanContext.Instance, _vulkanContext.Device,
                    out _khrSwapchain)) throw new Exception("KHR_swapchain extension not available");

            // Get the KhrSurface extension
            if (!_vulkanContext.VulkanApi.TryGetInstanceExtension(_vulkanContext.Instance, out _khrSurface))
                throw new Exception("KHR_surface extension not available");

            // Get the ExtMeshShader extension
            if (!_vulkanContext.VulkanApi.TryGetDeviceExtension(_vulkanContext.Instance, _vulkanContext.Device,
                    out _extMeshShader)) throw new Exception("EXT_mesh_shader extension not available");

            _surface = CreateSurface();
            SetDebugName(_surface, ObjectType.SurfaceKhr, "MainSurface");
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
            _swapchainImageLayouts =
                Enumerable.Repeat(ImageLayout.Undefined, (int)_swapchainManager.SwapchainImageCount).ToArray();

            // Create depth buffer and image view for rendering
            CreateDepthResources();
            Console.WriteLine("✓ Depth resources created");

            _commandBufferManager = new CommandBufferManager(
                _vulkanContext,
                _vulkanContext.GraphicsQueueFamily);
            Console.WriteLine("✓ Command buffers allocated");

            _synchronizationManager = new SynchronizationManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _swapchainManager.SwapchainImageCount);
            Console.WriteLine("✓ Synchronization primitives created");


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
                _bufferManager,
                _vulkanContext.GraphicsQueueFamily,
                _vulkanContext.TransferQueueFamily);
            Console.WriteLine("✓ Mesh manager initialized");

            // Create texture manager
            _textureManager = new TextureManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _vulkanContext.VmaAllocator);
            Console.WriteLine("✓ Texture manager initialized");

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

            CreateDefaultSampler();

            InitializeSceneBuffers();

            // Register scene buffers in bindless heap at fixed indices
            // These must match the shader binding indices and BindlessBufferIndices.cs
            _bindlessHeap.UpdateBuffer(ObjectBuffer, _sceneObjectBuffer, 16 * 1024 * 1024);
            _bindlessHeap.UpdateBuffer(MaterialBuffer, _sceneMaterialBuffer, 4 * 1024 * 1024);
            _bindlessHeap.UpdateBuffer(SceneMeshBuffer, _sceneMeshBuffer, 8 * 1024 * 1024);

            // Mesh manager finalization is automatic: LoadModelIntoScene() -> IntegratePayload() -> FinalizeAndUpdateMeshBuffers()
            // This ensures mesh buffers are allocated immediately when models are loaded via Content.Load()

            // Register double-buffered instance and meshlet draw buffers
            // Frame 0 at base index, Frame 1 at base index + 1
            for (uint i = 0; i < MaxFramesInFlight; i++)
            {
                _bindlessHeap.UpdateBuffer(InstanceBufferBase + i, _instanceBuffers[i], 16 * 1024 * 1024);
                _bindlessHeap.UpdateBuffer(MeshletDrawBufferBase + i, _meshletDrawBuffers[i], 32 * 1024 * 1024);
            }

            // Pre-register mesh buffers with minimal size - will be updated when models are loaded
            // This ensures bindless indices 3,5,6,7 always have valid buffers
            RegisterMeshBuffersInBindlessHeap();

            Console.WriteLine("✓ Scene buffers registered in bindless heap with double buffering");

            // Industry standard: Dispose existing pipeline before recreating to ensure push constant range matches current struct size
            _meshPipeline?.Dispose();
            _meshPipeline = new MeshPipeline(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                new[]
                {
                    _descriptorSetLayouts.BufferHeapLayout, // Set 0
                    _descriptorSetLayouts.TextureHeapLayout  // Set 1
                },
                _swapchainManager.SwapchainImageFormat);
            Console.WriteLine("✓ Mesh pipeline created (using bindless sets 0 and 1, push constant size: " + Data.RenderingData.PushConstants.SizeInBytes + " bytes)");

                // Phase 3.5: Initialize light manager
                _lightManagerImpl = new LightManager(
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
                    _lightManagerImpl,
                    _bufferManager,
                    _descriptorSetLayouts,
                    _bindlessHeap);
            Console.WriteLine("✓ Tiled light culling pass initialized");

            // Setup render graph with passes
            SetupRenderGraph();
            Console.WriteLine("✓ Render graph initialized");

            // Create scene data builder now that all resources are ready
            _sceneBuilder = new SceneDataBuilder(_meshManager, _textureManager, _bindlessHeap);
            _sceneBuilder.SetDefaultSampler(_defaultSampler);
            Console.WriteLine("✓ Scene data builder created");

            // Phase 2: Add test cube to scene
            // var cubeMesh = Data.RenderingData.Mesh.CreateCube();
            // var material = new Data.RenderingData.Material("default", "Shaders/test_vert.spv");
            // var cube = new Data.RenderingData.RenderObject("test_cube", cubeMesh, material, Matrix4x4.Identity);
            // AddRenderObject(cube);
            // Console.WriteLine("✓ Test cube added to scene");

            // _meshManager.Finalize();
            // Console.WriteLine("✓ Mesh manager finalized");

            Debug.WriteLine("Vulkan renderer initialized successfully");
            Console.WriteLine("\n✓ Renderer fully initialized - rendering started!\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize renderer: {ex.Message}");
            Console.WriteLine($"✗ Renderer initialization failed: {ex.Message}");
            if (ex.InnerException != null) Console.WriteLine($"  Inner: {ex.InnerException.Message}");
            // Cleanup on failure: destroy swapchain BEFORE surface (Vulkan spec requirement)
            _swapchainManager?.Dispose();
            if (_surface.Handle != 0 && _khrSurface != null)
                _khrSurface.DestroySurface(_vulkanContext!.Instance, _surface, null);
            throw;
        }

    }

    private void CreateDepthResources()
    {
        if (_bufferManager == null || _vulkanContext == null)
            throw new InvalidOperationException("Prerequisites not initialized");

        var extent = _swapchainManager!.SwapchainExtent;

        // Validate depth format support
        var depthFormat = FindSupportedDepthFormat();
        Console.WriteLine($"Using depth format: {depthFormat}");

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = depthFormat,
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
            Format = depthFormat,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.DepthBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        if (_vulkanContext.VulkanApi.CreateImageView(_vulkanContext.Device, &viewInfo, null, out _depthImageView) != Result.Success)
            throw new InvalidOperationException("Failed to create depth image view");

        // Set debug names for depth resources
        SetDebugName(_depthImage, ObjectType.Image, "DepthImage");
        SetDebugName(_depthImageView, ObjectType.ImageView, "DepthImageView");
    }
    
    private void CreateDefaultSampler()
    {
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            AnisotropyEnable = false,
            MaxAnisotropy = 1.0f,
            BorderColor = BorderColor.IntOpaqueBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MipmapMode = SamplerMipmapMode.Linear,
            MipLodBias = 0.0f,
            MinLod = 0.0f,
            MaxLod = 0.0f
        };

        if (_vulkanContext!.VulkanApi.CreateSampler(_vulkanContext.Device, &samplerInfo, null, out _defaultSampler) != Result.Success)
            throw new InvalidOperationException("Failed to create default sampler");

        SetDebugName(_defaultSampler, ObjectType.Sampler, "DefaultSampler");
        Console.WriteLine("✓ Default sampler created");
    }

    private void SetupRenderGraph()
    {
        if (_vulkanContext == null || _meshPipeline == null || _bindlessHeap == null)
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
        var rasterPass = new DynamicMeshPass(
            _vulkanContext!.VulkanApi,
            _extMeshShader!,
            _meshPipeline!,
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

        var storageUsage = BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit;

        // Allocate initial buffers using configurable sizes
        _sceneObjectBufferHandle = _bufferManager.AllocateBuffer(
            _bufferSizes.ObjectBufferSize,
            storageUsage,
            MemoryUsage.AutoPreferDevice);
        _sceneObjectBuffer = _bufferManager.GetBuffer(_sceneObjectBufferHandle);

        _sceneMaterialBufferHandle = _bufferManager.AllocateBuffer(
            _bufferSizes.MaterialBufferSize,
            storageUsage,
            MemoryUsage.AutoPreferDevice);
        _sceneMaterialBuffer = _bufferManager.GetBuffer(_sceneMaterialBufferHandle);

        _sceneMeshBufferHandle = _bufferManager.AllocateBuffer(
            _bufferSizes.MeshBufferSize,
            storageUsage,
            MemoryUsage.AutoPreferDevice);
        _sceneMeshBuffer = _bufferManager.GetBuffer(_sceneMeshBufferHandle);

        // Allocate double-buffered instance and meshlet draw buffers
        for (uint i = 0; i < MaxFramesInFlight; i++)
        {
            _instanceBufferHandles[i] = _bufferManager.AllocateBuffer(
                _bufferSizes.InstanceBufferSize, storageUsage, MemoryUsage.AutoPreferDevice);
            _instanceBuffers[i] = _bufferManager.GetBuffer(_instanceBufferHandles[i]);

            _meshletDrawBufferHandles[i] = _bufferManager.AllocateBuffer(
                _bufferSizes.MeshletDrawBufferSize, storageUsage, MemoryUsage.AutoPreferDevice);
            _meshletDrawBuffers[i] = _bufferManager.GetBuffer(_meshletDrawBufferHandles[i]);
        }
    }


    /// <summary>
    ///     Update logic. Called once per frame before rendering.
    /// </summary>
    public void Update(double deltaTime)
    {
        // Rotate the test cube
        // if (_renderObjects.TryGetValue("test_cube", out var cube))
        // {
        //     var rotation = Matrix4x4.CreateRotationY((float)deltaTime);
        //     cube.Transform = rotation * cube.Transform;
        // }


    }

    /// <summary>
    ///     Render logic. Called once per frame.
    /// </summary>
    public void Draw()
    {
        if (_vulkanContext == null || _swapchainManager == null ||
            _commandBufferManager == null || _synchronizationManager == null ||
            _meshPipeline == null || _meshManager == null || _sceneBuilder == null)
            return;

        // Drain scene payload queue from CPU thread
        while (_scenePayloadQueue.TryDequeue(out var payload))
        {
            IntegratePayload(payload);
        }

        // Flush deletion queue for completed frames
        FlushDeletionQueue();

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

        // Wait for this frame-in-flight's previous transfer to finish before
        // reusing its FrameUploadRing staging slot and transferFinishedSemaphore.
        var transferFence = _synchronizationManager.TransferFences[frameIndex];
        vk.WaitForFences(device, 1, &transferFence, true, ulong.MaxValue);
        vk.ResetFences(device, 1, &transferFence);

        // Acquire next image - signals imageAvailableSemaphore when done
        uint imageIndex = 0;
        var result = _khrSwapchain!.AcquireNextImage(
            device,
            _swapchainManager.Swapchain,
            ulong.MaxValue,
            imageAvailableSemaphore, // This semaphore will be signaled
            default,
            &imageIndex);

        if (result == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapchain();
            return;
        }

        if (result != Result.Success && result != Result.SuboptimalKhr)
            throw new Exception("Failed to acquire next image");

        // Get the render finished semaphore for THIS IMAGE (per-image)
        var renderFinishedSemaphore = _synchronizationManager.RenderFinishedSemaphores[imageIndex];

        _sceneBuilder.BeginFrame();
        foreach (var kvp in _renderObjects)
        {
            var obj = kvp.Value;
            if (obj != null && obj.Visible) _sceneBuilder.AddObject(obj);
        }

        var transferCommandBuffer = _commandBufferManager.TransferCommandBuffers[frameIndex];
        _commandBufferManager.ResetCommandBuffer(transferCommandBuffer);
        _commandBufferManager.BeginRecording(transferCommandBuffer);

            // Upload mesh data once (no-op for already uploaded meshes)
            foreach (var renderObject in _renderObjects.Values)
                if (renderObject?.Mesh != null)
                    _meshManager.UploadMeshToGPU(renderObject.Mesh, transferCommandBuffer, _frameUploadRing);

            // Dynamically resize buffers if needed
            EnsureBufferCapacityForScene();
            EnsureBufferCapacityForFrame(frameIndex);

            // Write CPU scene data to staging buffer and record copy commands
            // Use QFOT (Queue Family Ownership Transfer) for proper texture synchronization
            _sceneBuilder.UploadToGPU(
                _vulkanContext.VulkanApi,
                transferCommandBuffer,
                _vulkanContext.TransferQueueFamily,
                _vulkanContext.GraphicsQueueFamily,
                _frameUploadRing,
                _sceneObjectBuffer,
                _sceneMaterialBuffer,
                _sceneMeshBuffer);
            
            _sceneBuilder.UploadInstanceAndDrawData(
                _vulkanContext.VulkanApi,
                transferCommandBuffer,
                _frameUploadRing,
                _instanceBuffers[frameIndex],
                _meshletDrawBuffers[frameIndex]);

            // Phase 3.5: Upload lights
            _lightManagerImpl?.UploadToGPU(transferCommandBuffer, _frameUploadRing);

        _commandBufferManager.EndRecording(transferCommandBuffer);

        // Submit transfer work (asynchronously on transfer queue)
        SubmitTransferCommandBuffer(transferCommandBuffer, transferFinishedSemaphore);


        // Record commands
        var commandBuffer = _commandBufferManager.CommandBuffers[frameIndex];
        _commandBufferManager.ResetCommandBuffer(commandBuffer);
        _commandBufferManager.BeginRecording(commandBuffer);

        // Record acquire barriers for QFOT on graphics command buffer
        _sceneBuilder.RecordAcquireBarriers(_vulkanContext.VulkanApi, commandBuffer);

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
            ViewProjection = _camera.GetViewMatrix() * _camera.GetProjectionMatrix(),
            View = _camera.GetViewMatrix(),
            Projection = _camera.GetProjectionMatrix(),
            CameraPosition = _camera.GetPosition(),
            BindlessHeap = _bindlessHeap,
            MeshManager = _meshManager,
            SceneDataBuilder = _sceneBuilder,

            LightCount = LightManager?.LightCount ?? 0,
            LightBufferIndex = LightManager?.LightBufferBindlessIndex ?? 0,
            TiledLightHeaderBufferIndex = _tiledLightCullingPass?.TiledLightHeaderBufferIndex ?? 0,
            TiledLightIndicesBufferIndex = _tiledLightCullingPass?.TiledLightIndicesBufferIndex ?? 0,

            // Static mesh buffers (never change, use constants directly)
            VertexBufferIndex = VertexBuffer,
            MeshletBufferIndex = MeshletBuffer,
            MeshletVertexIndexBufferIndex = MeshletVertexIndexBuffer,
            MeshletTriangleIndexBufferIndex = MeshletTriangleIndexBuffer,

            // Per-frame buffers — base indices; DB_BO in shaders adds FrameIndex
            InstanceBufferIndex = InstanceBufferBase,
            MeshletDrawBufferIndex = MeshletDrawBufferBase,
            MeshletDrawCount = _sceneBuilder.MeshletDrawCount
        };

        if (_depthImage.Handle != 0 && _depthImageLayout != ImageLayout.DepthAttachmentOptimal)
        {
            TransitionImageLayout(commandBuffer, _depthImage,
                _depthImageLayout, ImageLayout.DepthAttachmentOptimal, ImageAspectFlags.DepthBit);
            _depthImageLayout = ImageLayout.DepthAttachmentOptimal;
        }

        _renderGraph?.Execute(commandBuffer, rgContext);

        // Transition: COLOR_ATTACHMENT_OPTIMAL -> PRESENT_SRC_KHR before presenting
        TransitionImageLayout(commandBuffer, swapchainImage,
            ImageLayout.ColorAttachmentOptimal, ImageLayout.PresentSrcKhr);
        _swapchainImageLayouts![imageIndex] = ImageLayout.PresentSrcKhr;

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
    ///     Add a renderable object to the scene.
    /// </summary>
    public void AddRenderObject(Data.RenderingData.RenderObject obj)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        _renderObjects[obj.Name] = obj;

        if (_meshManager != null)
        {
            // var mesh = obj.Mesh;
            // var vertexBytes = MemoryMarshal.AsBytes<Data.RenderingData.Vertex>(mesh.Vertices);
            _meshManager.RegisterMesh(obj.Mesh);
        }

        Console.WriteLine($"Added render object: {obj.Name}");
    }

    /// <summary>
    /// Queue a buffer handle for fence-based deletion.
    /// Buffers are deleted when the associated fence signals, ensuring GPU is done with them.
    /// </summary>
    private void QueueBufferDeletion(BufferHandle? handle, Fence fence)
    {
        if (!handle.HasValue || !handle.Value.IsValid) return;
        _bufferDeleter?.Track(handle.Value, fence);
    }

    /// <summary>
    /// Flush the deletion queue for frames that have completed.
    /// Called each frame to clean up resources that are no longer in use by the GPU.
    /// Uses fence-based resource lifecycle management for proper synchronization.
    /// </summary>
    private void FlushDeletionQueue()
    {
        // Clean up buffers whose fences have signaled
        _bufferDeleter?.Cleanup();
        
        // Check if we need to update bindless heap after mesh buffer re-finalization
        // With fence-based tracking, we check if all old buffer fences have signaled
        if (_meshManager != null && _meshManager.HasOldBuffersPendingDeletion)
        {
            if (_meshManager.OldBufferFencesAllSignaled(_vulkanContext!.VulkanApi, _vulkanContext.Device))
            {
                // All fences for old mesh buffers have signaled - safe to update bindless heap
                RegisterMeshBuffersInBindlessHeap();
                _meshManager.ClearOldBufferHandles();
                Console.WriteLine("✓ Bindless heap updated with new mesh buffers (fence-based)");
            }
        }
    }

    /// <summary>
    ///     Re-finalizes the mesh manager. Bindless heap update is deferred until old buffers are safe.
    ///     Call this after adding new render objects post-Load().
    ///     Uses fence-based resource lifecycle management for proper synchronization.
    /// </summary>
    public void FinalizeAndUpdateMeshBuffers()
    {
        if (_meshManager == null || _vulkanContext == null || _bindlessHeap == null || _synchronizationManager == null) return;
        
        bool wasAlreadyFinalized = _meshManager.IsFinalized;
        
        // Check if we're re-finalizing (old buffers exist)
        if (wasAlreadyFinalized)
        {
            // Queue old buffers for fence-based deletion
            // They'll be deleted when the in-flight fence signals
            var oldHandles = _meshManager.OldBufferHandles;
            var frameIndex = _currentFrameIndex % MaxFramesInFlight;
            var inFlightFence = _synchronizationManager.InFlightFences[frameIndex];
            
            QueueBufferDeletion(oldHandles.Vertex, inFlightFence);
            QueueBufferDeletion(oldHandles.Index, inFlightFence);
            QueueBufferDeletion(oldHandles.Meshlet, inFlightFence);
            QueueBufferDeletion(oldHandles.MeshletVertexIndices, inFlightFence);
            QueueBufferDeletion(oldHandles.MeshletTriangleIndices, inFlightFence);
            
            // Track the fence in mesh manager for bindless heap update timing
            _meshManager.SetOldBufferFence(inFlightFence);
        }
        
        _meshManager.FinalizeOrReFinalize();
        
        // Industry standard: Register buffers immediately after finalization
        // This ensures mesh buffers are available for rendering without requiring user intervention
        if (!wasAlreadyFinalized || !_meshManager.HasOldBuffersPendingDeletion)
        {
            RegisterMeshBuffersInBindlessHeap();
            Console.WriteLine("✓ Mesh buffers finalized and registered in bindless heap");
        }
        else
        {
            Console.WriteLine("✓ Mesh buffers re-finalized (bindless heap update deferred until fence signals)");
        }
    }

    /// <summary>
    ///     Register mesh buffers in the bindless descriptor heap at fixed indices.
    ///     Uses centralized constants from BindlessBufferIndices.
    ///     Buffers are allocated during Finalize() to ensure valid handles exist.
    ///     If called before finalization, registers with minimal 1-byte buffers.
    /// </summary>
    private void RegisterMeshBuffersInBindlessHeap()
    {
        if (_meshManager == null || _bindlessHeap == null || _bufferManager == null) return;

        // Get all buffer handles
        var allHandles = _meshManager.GetAllMeshBufferHandles();

        // Check if mesh manager has been finalized (buffers allocated)
        bool isFinalized = _meshManager.IsFinalized;
        
        if (isFinalized)
        {
            // Validate that all required handles are valid
            if (!allHandles.VertexHandle.IsValid || !allHandles.IndexHandle.IsValid ||
                !allHandles.MeshletHandle.IsValid || !allHandles.MeshletVertexIndicesHandle.IsValid ||
                !allHandles.MeshletTriangleIndicesHandle.IsValid)
            {
                // Log which buffers are missing for debugging
                if (!allHandles.VertexHandle.IsValid) Console.WriteLine("[ERROR] Vertex buffer handle is invalid");
                if (!allHandles.IndexHandle.IsValid) Console.WriteLine("[ERROR] Index buffer handle is invalid");
                if (!allHandles.MeshletHandle.IsValid) Console.WriteLine("[ERROR] Meshlet buffer handle is invalid");
                if (!allHandles.MeshletVertexIndicesHandle.IsValid) Console.WriteLine("[ERROR] Meshlet vertex indices buffer handle is invalid");
                if (!allHandles.MeshletTriangleIndicesHandle.IsValid) Console.WriteLine("[ERROR] Meshlet triangle indices buffer handle is invalid");
                return;
            }

            // Get mesh buffers
            var (vertexBuffer, indexBuffer) = _meshManager.GetMeshBuffers();
            var (meshletBuffer, meshletVertexIndicesBuffer, meshletTriangleIndicesBuffer) =
                _meshManager.GetMeshletBuffers();

            // Get buffer sizes from BufferManager
            var vertexSize = _bufferManager.GetBufferSize(allHandles.VertexHandle);
            var indexSize = _bufferManager.GetBufferSize(allHandles.IndexHandle);
            var meshletSize = _bufferManager.GetBufferSize(allHandles.MeshletHandle);
            var meshletVertexIndexSize = _bufferManager.GetBufferSize(allHandles.MeshletVertexIndicesHandle);
            var meshletTriangleIndexSize = _bufferManager.GetBufferSize(allHandles.MeshletTriangleIndicesHandle);

            // Register with bindless heap at fixed indices defined in BindlessBufferIndices
            _bindlessHeap.UpdateBuffer(VertexBuffer, vertexBuffer, vertexSize);
            _bindlessHeap.UpdateBuffer(IndexBuffer, indexBuffer, indexSize);
            _bindlessHeap.UpdateBuffer(MeshletBuffer, meshletBuffer, meshletSize);
            _bindlessHeap.UpdateBuffer(MeshletVertexIndexBuffer, meshletVertexIndicesBuffer, meshletVertexIndexSize);
            _bindlessHeap.UpdateBuffer(MeshletTriangleIndexBuffer, meshletTriangleIndicesBuffer, meshletTriangleIndexSize);

            Console.WriteLine("✓ Mesh buffers registered in bindless heap");
        }
        else
        {
            // Pre-registration before finalization: create minimal 1-byte buffers to reserve bindless indices
            // This prevents zero-initialized data in bindless heap slots
            const ulong minimalSize = 1;
            var minimalVertexHandle = _bufferManager.AllocateBuffer(minimalSize, BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit, MemoryUsage.AutoPreferDevice);
            var minimalIndexHandle = _bufferManager.AllocateBuffer(minimalSize, BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit, MemoryUsage.AutoPreferDevice);
            var minimalMeshletHandle = _bufferManager.AllocateBuffer(minimalSize, BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit, MemoryUsage.AutoPreferDevice);
            var minimalMeshletVertexIndexHandle = _bufferManager.AllocateBuffer(minimalSize, BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit, MemoryUsage.AutoPreferDevice);
            var minimalMeshletTriangleIndexHandle = _bufferManager.AllocateBuffer(minimalSize, BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit, MemoryUsage.AutoPreferDevice);

            var minimalVertexBuffer = _bufferManager.GetBuffer(minimalVertexHandle);
            var minimalIndexBuffer = _bufferManager.GetBuffer(minimalIndexHandle);
            var minimalMeshletBuffer = _bufferManager.GetBuffer(minimalMeshletHandle);
            var minimalMeshletVertexIndexBuffer = _bufferManager.GetBuffer(minimalMeshletVertexIndexHandle);
            var minimalMeshletTriangleIndexBuffer = _bufferManager.GetBuffer(minimalMeshletTriangleIndexHandle);

            _bindlessHeap.UpdateBuffer(VertexBuffer, minimalVertexBuffer, minimalSize);
            _bindlessHeap.UpdateBuffer(IndexBuffer, minimalIndexBuffer, minimalSize);
            _bindlessHeap.UpdateBuffer(MeshletBuffer, minimalMeshletBuffer, minimalSize);
            _bindlessHeap.UpdateBuffer(MeshletVertexIndexBuffer, minimalMeshletVertexIndexBuffer, minimalSize);
            _bindlessHeap.UpdateBuffer(MeshletTriangleIndexBuffer, minimalMeshletTriangleIndexBuffer, minimalSize);

            Console.WriteLine("✓ Mesh buffers pre-registered in bindless heap (will be updated after finalization)");
        }
    }

    /// <summary>
    ///     Remove a renderable object from the scene.
    /// </summary>
    public void RemoveRenderObject(string name)
    {
        if (_renderObjects.Remove(name)) Console.WriteLine($"Removed render object: {name}");
    }

    /// <summary>
    ///     Get a render object by name.
    /// </summary>
    public Data.RenderingData.RenderObject? GetRenderObject(string name)
    {
        _renderObjects.TryGetValue(name, out var obj);
        return obj;
    }

    private SurfaceKHR CreateSurface()
    {
        var surface = new SurfaceKHR();

        if (_vulkanContext != null)
            surface = _window!.VkSurface!.Create<AllocationCallbacks>(_vulkanContext.Instance.ToHandle(), null)
                .ToSurface();

        if (surface.Handle == 0)
            throw new Exception("Failed to create Vulkan surface");

        return surface;
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

        var frameIndex = _currentFrameIndex % MaxFramesInFlight;
        var transferFence = _synchronizationManager!.TransferFences[frameIndex];

        var result = vk.QueueSubmit(transferQueue, 1, &submitInfo, default);
        if (result != Result.Success)
            throw new Exception($"Transfer queue submit failed: {result}");
    }

    private void SubmitCommandBuffer(Queue queue, CommandBuffer commandBuffer,
        Semaphore waitImageReadySemaphore, Semaphore waitTransferFinishedSemaphore, Semaphore signalRenderSemaphore,
        Fence fence)
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
            throw new Exception("Failed to submit command buffer");
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
            RecreateSwapchain();
        else if (result != Result.Success) throw new Exception("Failed to present frame");
    }

    private void RecreateSwapchain()
    {
        var vk = _vulkanContext!.VulkanApi;
        vk.DeviceWaitIdle(_vulkanContext.Device);
        
        _swapchainManager?.Dispose();

        _swapchainManager = new SwapchainManager(
            vk,
            _vulkanContext.Instance,
            _vulkanContext.PhysicalDevice,
            _vulkanContext.Device,
            _surface,
            _vulkanContext.GraphicsQueue,
            (uint)_window.Size.X,
            (uint)_window.Size.Y);
        _swapchainImageLayouts = Enumerable.Repeat(ImageLayout.Undefined, (int)_swapchainManager.SwapchainImageCount)
            .ToArray();
        
        // Industry standard: Recreate pipeline when swapchain changes to ensure compatibility
        RecreateMeshPipeline();
    }

    /// <summary>
    /// Recreates the mesh pipeline. Must be called when push constant struct changes or swapchain is recreated.
    /// Industry standard: Ensures pipeline layout matches current shader and push constant requirements.
    /// </summary>
    private void RecreateMeshPipeline()
    {
        if (_vulkanContext == null || _descriptorSetLayouts == null || _swapchainManager == null) return;
        
        // Wait for GPU to finish using the old pipeline
        _vulkanContext.VulkanApi.DeviceWaitIdle(_vulkanContext.Device);
        
        _meshPipeline?.Dispose();
        _meshPipeline = new MeshPipeline(
            _vulkanContext.VulkanApi,
            _vulkanContext.Device,
            new[]
            {
                _descriptorSetLayouts.BufferHeapLayout,
                _descriptorSetLayouts.TextureHeapLayout
            },
            _swapchainManager.SwapchainImageFormat);
        
        Console.WriteLine("✓ Mesh pipeline recreated (push constant size: " + Data.RenderingData.PushConstants.SizeInBytes + " bytes)");
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
    

    public Task InitializeAsync()
    {
        // Initialize Vulkan renderer
        return Task.CompletedTask;
    }

    public Task RenderFrameAsync()
    {
        // Render a frame using Vulkan
        Draw();
        return Task.CompletedTask;
    }

    public void Resize(int width, int height)
    {
        // Handle window resize
    }

    /// <summary>
    ///     Creates a SceneDataBuilder using the renderer's initialised Vulkan resources.
    ///     Must be called after <see cref="Load"/> has completed.
    /// </summary>
    public SceneDataBuilder CreateSceneDataBuilder()
    {
        if (_meshManager == null || _textureManager == null || _bindlessHeap == null)
            throw new InvalidOperationException(
                "CreateSceneDataBuilder() must be called after Load() has completed.");

        return new SceneDataBuilder(_meshManager, _textureManager, _bindlessHeap);
    }

    /// <summary>
    /// Builds CPU-only payload for a model. Callable from any thread.
    /// </summary>
    public ScenePayload BuildCpuPayload(IModel model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var payload = new ScenePayload
        {
            ModelName = model.Name,
            ModelTransform = (model as NjulfFramework.Assets.Models.FrameworkModel)?.RootNode?.Transform ?? Matrix4x4.Identity
        };

        var meshes = model.Meshes.ToList();
        var materials = model.Materials.ToList();

        for (int i = 0; i < meshes.Count; i++)
        {
            var iMesh = meshes[i];
            var iMat = i < materials.Count ? materials[i] : null;

            var rdVertices = new Data.RenderingData.Vertex[iMesh.Vertices.Length];
            for (int v = 0; v < iMesh.Vertices.Length; v++)
                rdVertices[v] = new Data.RenderingData.Vertex(
                    iMesh.Vertices[v].Position,
                    iMesh.Vertices[v].Normal,
                    iMesh.Vertices[v].TexCoord);

            var rdMesh = new Data.RenderingData.Mesh(
                iMesh.Name,
                rdVertices,
                iMesh.Indices,
                iMesh.BoundingBoxMin,
                iMesh.BoundingBoxMax);

            var rdMat = new Data.RenderingData.Material(
                iMat?.Name ?? "default",
                "Shaders/test_vert.spv",
                iMat?.BaseColorTexturePath ?? string.Empty);

            if (iMat != null)
            {
                rdMat.BaseColorFactor = iMat.BaseColorFactor;
                rdMat.MetallicFactor = iMat.MetallicFactor;
                rdMat.RoughnessFactor = iMat.RoughnessFactor;
                rdMat.NormalTexturePath = iMat.NormalTexturePath;
                rdMat.EmissiveFactor = iMat.EmissiveFactor;
            }

            var renderObjName = $"model_{model.Name}_{i}_{iMesh.Name}";
            payload.RenderObjects.Add(new Data.RenderingData.RenderObject(
                renderObjName, rdMesh, rdMat, payload.ModelTransform));
            payload.RenderObjectNames.Add(renderObjName);
        }

        return payload;
    }

    /// <summary>
    /// Integrates a CPU-built payload into the scene. Must be called from the render thread.
    /// </summary>
    private void IntegratePayload(ScenePayload payload)
    {
        foreach (var renderObj in payload.RenderObjects)
        {
            _renderObjects[renderObj.Name] = renderObj;
            if (_meshManager != null)
            {
                _meshManager.RegisterMesh(renderObj.Mesh);
            }
        }

        _modelToRenderObjectNames[payload.ModelName] = payload.RenderObjectNames;

        FinalizeAndUpdateMeshBuffers();
        Console.WriteLine($"✓ Model '{payload.ModelName}' loaded: {payload.RenderObjects.Count} mesh(es) added to scene");
    }

    public void LoadModelIntoScene(IModel model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        // Build CPU payload (thread-safe, no GPU operations)
        var payload = BuildCpuPayload(model);

        // Process immediately for synchronous loading (industry standard)
        // This ensures mesh buffers are finalized and registered before any diagnostics run
        IntegratePayload(payload);
        
        // For re-finalization scenarios, ensure bindless heap is updated if old buffers were pending
        if (_meshManager != null && _meshManager.HasOldBuffersPendingDeletion &&
            _meshManager.OldBufferFencesAllSignaled(_vulkanContext!.VulkanApi, _vulkanContext.Device))
        {
            RegisterMeshBuffersInBindlessHeap();
            _meshManager.ClearOldBufferHandles();
        }
    }

    public void UpdateModelTransform(IModel model, Matrix4x4 transform)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        if (_modelToRenderObjectNames.TryGetValue(model.Name, out var renderObjectNames))
        {
            foreach (var objName in renderObjectNames)
            {
                if (_renderObjects.TryGetValue(objName, out var renderObj))
                {
                    renderObj.Transform = transform;
                }
            }
            // Also update the model's own transform if it's a FrameworkModel
            if (model is NjulfFramework.Assets.Models.FrameworkModel frameworkModel && frameworkModel.RootNode != null)
            {
                frameworkModel.RootNode.Transform = transform;
            }
        }
    }
    
    
}
