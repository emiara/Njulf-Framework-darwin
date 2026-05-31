using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using NjulfFramework.Core.Interfaces.Rendering;
using NjulfFramework.Rendering.Core;
using Silk.NET.Windowing;

namespace NjulfFramework.Rendering.DependencyInjection;

/// <summary>
/// Extension methods for setting up NjulfFramework.Rendering services in an IServiceCollection.
/// </summary>
public static class RenderingServiceCollectionExtensions
{
    /// <summary>
    /// Adds NjulfFramework.Rendering services to the specified IServiceCollection.
    /// </summary>
    public static IServiceCollection AddNjulfFrameworkRendering(this IServiceCollection services)
    {
        services.AddSingleton<ICamera>(sp =>
        {
            // Default camera at (0, 0, 5) looking toward origin (0,0,0)
            // Rotation: 180° around Y-axis flips forward direction from +Z to -Z
            return new Camera(
                position: new Vector3(0, 0, 5),
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI),
                fovY: MathF.PI / 4f, // 45° FOV
                aspectRatio: 1.0f // Will be updated to match window in GameFramework.OnWindowLoad()
            );
        });

        services.AddSingleton<VulkanRenderer>(sp =>
        {
            var window = sp.GetRequiredService<IWindow>();
            var camera = sp.GetRequiredService<ICamera>();
            return new VulkanRenderer(window, camera);
        });
        services.AddSingleton<IRenderer>(sp => sp.GetRequiredService<VulkanRenderer>());
        services.AddSingleton<ISceneLoader>(sp => sp.GetRequiredService<VulkanRenderer>());

        return services;
    }
}