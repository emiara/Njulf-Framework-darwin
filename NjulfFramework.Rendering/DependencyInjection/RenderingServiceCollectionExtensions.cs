using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using NjulfFramework.Core.Interfaces.Rendering;
using NjulfFramework.Rendering.Core;

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
        services.AddSingleton<VulkanRenderer>();
        services.AddSingleton<IRenderer>(sp => sp.GetRequiredService<VulkanRenderer>());
        services.AddSingleton<ISceneLoader>(sp => sp.GetRequiredService<VulkanRenderer>());
        services.AddSingleton<Camera>(sp =>
        {
            // Default camera matching the original hardcoded values
            return new Camera(
                position: new Vector3(0, 2, 3),
                rotation: Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 4f)),
                fovY: MathF.PI / 4f,
                aspectRatio: 1.0f // Will be updated by VulkanRenderer.Load()
            );
        });

        return services;
    }
}