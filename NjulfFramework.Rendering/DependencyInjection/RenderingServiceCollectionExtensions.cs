using Microsoft.Extensions.DependencyInjection;
using NjulfFramework.Core.Interfaces.Rendering;

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

        return services;
    }
}