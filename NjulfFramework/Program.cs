using NjulfFramework.Rendering;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace NjulfFramework;

internal static class RendererExample
{
    private static VulkanRenderer? _renderer;

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

        _renderer = new VulkanRenderer(window);

        // Lifecycle callbacks
        window.Load += OnLoad;
        window.Update += OnUpdate;
        window.Render += OnRender;
        window.Closing += OnClosing;

        window.Run();
    }

    private static void OnLoad()
    {
        try
        {
            _renderer?.Load();
            Console.WriteLine("✓ Renderer loaded successfully");
            Console.WriteLine("✓ Vulkan instance created");
            Console.WriteLine("✓ Swapchain initialized");
            Console.WriteLine("✓ Render pass created");
            Console.WriteLine("✓ Graphics pipeline compiled");
            Console.WriteLine("\nRendering started!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to load renderer: {ex.Message}");
            if (ex.InnerException != null) Console.WriteLine($"  Inner exception: {ex.InnerException.Message}");
        }
    }

    private static void OnUpdate(double deltaTime)
    {
        _renderer?.Update(deltaTime);
    }

    private static void OnRender(double deltaTime)
    {
        try
        {
            _renderer?.Draw();
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