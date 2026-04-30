using Microsoft.Extensions.DependencyInjection;
using NjulfFramework.Core.Interfaces.Assets;
using NjulfFramework.Core.Interfaces.Conversion;
using NjulfFramework.Core.Interfaces.Rendering;

namespace NjulfFramework.Assets.DependencyInjection
{
    public static class AssetsServiceCollectionExtensions
    {
        public static IServiceCollection AddNjulfFrameworkAssets(this IServiceCollection services)
        {
            services.AddSingleton<AssimpImporter>();
            services.AddSingleton<MaterialConverter>();
            services.AddSingleton<MeshConverter>();
            services.AddSingleton<IModelConverter>(sp => sp.GetRequiredService<MeshConverter>());
            services.AddSingleton<ModelProcessor>();
            services.AddSingleton<AssetCache>();
            services.AddSingleton<IAssetLoader, AssetLoader>();

            // ContentManager needs ISceneLoader — resolved from the renderer registered elsewhere
            services.AddSingleton<IContentManager>(sp =>
            {
                var assetLoader = sp.GetRequiredService<IAssetLoader>();
                var sceneLoader = sp.GetRequiredService<ISceneLoader>();
                return new ContentManager(assetLoader, sceneLoader);
            });

            return services;
        }
    }
}