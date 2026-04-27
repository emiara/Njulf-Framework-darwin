using Microsoft.Extensions.DependencyInjection;
using NjulfFramework.Core.DependencyInjection;
using NjulfFramework.Rendering.DependencyInjection;
using NjulfFramework.Assets.DependencyInjection;
using NjulfFramework.Input.DependencyInjection;
using NjulfFramework.Core.Interfaces.Rendering;
using NjulfFramework.Rendering;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace NjulfFramework;

internal static class RendererExample
{
    private static IServiceProvider? _serviceProvider;
    private static IRenderer? _renderer;

    private static void Main(string[] args)
    {
        var options = WindowOptions.DefaultVulkan with
        {
            Title = "YourFramework - Vulkan Triangle",
            Size = new Vector2D<int>(1920, 1080),
            VSync = false,
            WindowBorder = WindowBorder.Fixed
        };

        using var window = Window.Create(options);

        if (window is null)
        {
            Console.WriteLine("Failed to create window");
            return;
        }
        
        // Set up dependency injection
        var services = new ServiceCollection();
        
        services.AddSingleton<IWindow>(window);
        
        // Add all framework modules
        services.AddNjulfFrameworkCore()
            .AddNjulfFrameworkRendering()
            .AddNjulfFrameworkAssets()
            .AddNjulfFrameworkInput();

        _serviceProvider = services.BuildServiceProvider();
        
        window.Load += () =>
        {
            try
            {
                // Resolve renderer from DI container
                _renderer = _serviceProvider.GetRequiredService<IRenderer>();
                //_renderer.InitializeAsync();
                
                _renderer.Load();
                
                Console.WriteLine("✓ Renderer initialized");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to initialize renderer: {ex.Message}");
                if (ex.InnerException != null) Console.WriteLine($"  Inner: {ex.InnerException.Message}");
                window.Close();
            }
        };

        window.Update += OnUpdate;
        window.Render += OnRender;
        window.Closing += OnClosing;

        try
        {
            window.Run();
            Console.WriteLine("✓ Application started successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to run: {ex.Message}");
            if (ex.InnerException != null) Console.WriteLine($"  Inner exception: {ex.InnerException.Message}");
        }
    }

    private static void OnUpdate(double deltaTime)
    {
        if (_renderer is VulkanRenderer vulkanRenderer)
            vulkanRenderer.Update(deltaTime);
    }

    private static void OnRender(double deltaTime)
    {
        try
        {
            _renderer?.RenderFrameAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Rendering error: {ex.Message}");
        }
    }

    private static void OnClosing()
    {
        try
        {
            _renderer?.Dispose();
            Console.WriteLine("\n✓ Renderer cleaned up");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error during cleanup: {ex.Message}");
        }
    }
}