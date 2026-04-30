using Microsoft.Extensions.DependencyInjection;
using NjulfFramework.Assets.DependencyInjection;
using NjulfFramework.Core;
using NjulfFramework.Core.DependencyInjection;
using NjulfFramework.Core.Interfaces.Assets;
using NjulfFramework.Input.DependencyInjection;
using NjulfFramework.Rendering;
using NjulfFramework.Rendering.DependencyInjection;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using System.Numerics;

namespace NjulfFramework;

internal sealed class RendererExample : GameFramework
{
    private const string GltfModelPath = "vintage_video_camera_2k.gltf";

    private static void Main(string[] args) => new RendererExample().Run();

    protected override WindowOptions ConfigureWindow() =>
        WindowOptions.DefaultVulkan with
        {
            Title = "YourFramework - Vulkan Triangle",
            Size = new Vector2D<int>(1920, 1080),
            VSync = false,
            WindowBorder = WindowBorder.Fixed
        };

    protected override void RegisterFrameworkServices(IServiceCollection services)
    {
        services.AddNjulfFrameworkCore()
                .AddNjulfFrameworkRendering()
                .AddNjulfFrameworkAssets()
                .AddNjulfFrameworkInput();
    }

    public override void Load()
    {
        Console.WriteLine("✓ Renderer initialized");

        // One line — identical feel to MonoGame's Content.Load<T>()
        Content?.Load<IModel>(GltfModelPath);
    }

    public override void Update(float deltaTime)
    {
        if (Renderer is VulkanRenderer vulkanRenderer)
            vulkanRenderer.Update(deltaTime);
    }

    public override void Draw() { }
}