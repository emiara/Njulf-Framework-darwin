using Microsoft.Extensions.DependencyInjection;
using NjulfFramework.Core.Interfaces.Assets;
using NjulfFramework.Core.Interfaces.Conversion;

namespace NjulfFramework.Assets.DependencyInjection
{
    /// <summary>
    /// Extension methods for setting up NjulfFramework.Assets services in an IServiceCollection
    /// </summary>
    public static class AssetsServiceCollectionExtensions
    {
        /// <summary>
        /// Adds NjulfFramework.Assets services to the specified IServiceCollection
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to</param>
        /// <returns>The IServiceCollection so that additional calls can be chained</returns>
        public static IServiceCollection AddNjulfFrameworkAssets(this IServiceCollection services)
        {
            // Register assets services
            services.AddSingleton<IAssetLoader, AssetLoader>();
            services.AddSingleton<AssimpImporter>();
            services.AddSingleton<ModelProcessor>();
            services.AddSingleton<AssetCache>();
            services.AddSingleton<IModelConverter, MeshConverter>();
            
            return services;
        }
    }
}