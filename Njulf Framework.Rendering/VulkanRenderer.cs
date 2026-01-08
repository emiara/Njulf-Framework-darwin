using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Silk.NET.Core.Contexts;
using Njulf_Framework.Rendering.Core;
using Njulf_Framework.Rendering.Resources;
using Njulf_Framework.Rendering.Pipeline;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Njulf_Framework.Rendering;

public class VulkanRenderer : IDisposable
{
    private readonly IWindow _window;
    private VulkanContext? _vulkanContext;
    private SwapchainManager? _swapchainManager;
    private CommandBufferManager? _commandBufferManager;
    private SynchronizationManager? _synchronizationManager;
    private RenderPassManager? _renderPassManager;
    private FramebufferManager? _framebufferManager;
    private GraphicsPipeline? _graphicsPipeline;
    
    private KhrSwapchain? _khrSwapchain;
    private KhrSurface? _khrSurface;
    
    private SurfaceKHR _surface;
    private uint _currentFrameIndex = 0;
    private const uint MaxFramesInFlight = 2;

    public VulkanContext VulkanContext => _vulkanContext!;
    public SwapchainManager SwapchainManager => _swapchainManager!;

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
            
            _swapchainManager = new SwapchainManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Instance,
                _vulkanContext.PhysicalDevice,
                _vulkanContext.Device,
                _surface,
                _vulkanContext.GraphicsQueue,
                (uint)_window.Size.X,
                (uint)_window.Size.Y);

            _commandBufferManager = new CommandBufferManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _vulkanContext.GraphicsQueueFamily,
                MaxFramesInFlight);

            _synchronizationManager = new SynchronizationManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                MaxFramesInFlight);

            // Create render pass
            _renderPassManager = new RenderPassManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _swapchainManager.SwapchainImageFormat);

            // Create framebuffers
            _framebufferManager = new FramebufferManager(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _renderPassManager.RenderPass,
                _swapchainManager.SwapchainImageViews,
                _swapchainManager.SwapchainExtent);

            // Create graphics pipeline
            _graphicsPipeline = new GraphicsPipeline(
                _vulkanContext.VulkanApi,
                _vulkanContext.Device,
                _renderPassManager.RenderPass,
                _swapchainManager.SwapchainExtent,
                "Shaders/triangle.vert",
                "Shaders/triangle.frag");

            System.Diagnostics.Debug.WriteLine("Vulkan renderer initialized successfully");
            
            Console.WriteLine("Vulkan renderer initialized successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize renderer: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Update logic. Called once per frame before rendering.
    /// </summary>
    public void Update(double deltaTime)
    {
        // Game logic updates will go here
    }

    /// <summary>
    /// Render logic. Called once per frame.
    /// </summary>
    public unsafe void Draw()
    {
        if (_vulkanContext == null || _swapchainManager == null || 
            _commandBufferManager == null || _synchronizationManager == null ||
            _renderPassManager == null || _framebufferManager == null ||
            _graphicsPipeline == null)
        {
            return;
        }

        var vk = _vulkanContext.VulkanApi;
        var device = _vulkanContext.Device;
        var graphicsQueue = _vulkanContext.GraphicsQueue;

        // Wait for frame fence
        var inFlightFence = _synchronizationManager.InFlightFences[_currentFrameIndex];
        vk.WaitForFences(device, 1, &inFlightFence, true, ulong.MaxValue);

        // Acquire next image
        uint imageIndex = 0;
        var imageAvailableSemaphore = _synchronizationManager.ImageAvailableSemaphores[_currentFrameIndex];
        var result = _khrSwapchain!.AcquireNextImage(
            device,
            _swapchainManager.Swapchain,
            ulong.MaxValue,
            imageAvailableSemaphore,
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

        vk.ResetFences(device, 1, &inFlightFence);

        // Record command buffer
        var commandBuffer = _commandBufferManager.CommandBuffers[_currentFrameIndex];
        _commandBufferManager.ResetCommandBuffer(commandBuffer);
        _commandBufferManager.BeginRecording(commandBuffer);

        RecordRenderCommands(commandBuffer, imageIndex);

        _commandBufferManager.EndRecording(commandBuffer);

        // Submit command buffer
        var renderFinishedSemaphore = _synchronizationManager.RenderFinishedSemaphores[_currentFrameIndex];
        SubmitCommandBuffer(graphicsQueue, commandBuffer, imageAvailableSemaphore, 
            renderFinishedSemaphore, inFlightFence);

        // Present
        PresentFrame(imageIndex);

        _currentFrameIndex = (_currentFrameIndex + 1) % MaxFramesInFlight;
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

    private unsafe void RecordRenderCommands(CommandBuffer commandBuffer, uint imageIndex)
    {
        var vk = _vulkanContext!.VulkanApi;
        var swapchainImageView = _swapchainManager!.SwapchainImageViews[imageIndex];
        var framebuffer = _framebufferManager!.Framebuffers[imageIndex];

        // Begin render pass
        var clearValue = new ClearValue { Color = new ClearColorValue(0.1f, 0.1f, 0.1f, 1.0f) };

        var renderPassBeginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPassManager!.RenderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D
            {
                Offset = new Offset2D { X = 0, Y = 0 },
                Extent = _swapchainManager.SwapchainExtent
            },
            ClearValueCount = 1,
            PClearValues = &clearValue
        };

        vk.CmdBeginRenderPass(commandBuffer, &renderPassBeginInfo, SubpassContents.Inline);

        // Bind pipeline
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _graphicsPipeline!.Pipeline);

        // Draw fullscreen triangle (3 vertices, no instance buffer)
        vk.CmdDraw(commandBuffer, 3, 1, 0, 0);

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

    private unsafe void PresentFrame(uint imageIndex)
    {
        var vk = _vulkanContext!.VulkanApi;
        var renderFinishedSemaphore = _synchronizationManager!.RenderFinishedSemaphores[_currentFrameIndex];
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

        _swapchainManager = new SwapchainManager(
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