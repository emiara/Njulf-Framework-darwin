using Microsoft.Extensions.DependencyInjection;

namespace NjulfFramework.Core.DependencyInjection
{
    /// <summary>
    /// Extension methods for setting up NjulfFramework.Core services in an IServiceCollection
    /// </summary>
    public static class CoreServiceCollectionExtensions
    {
        /// <summary>
        /// Adds NjulfFramework.Core services to the specified IServiceCollection
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to</param>
        /// <returns>The IServiceCollection so that additional calls can be chained</returns>
        public static IServiceCollection AddNjulfFrameworkCore(this IServiceCollection services)
        {
            return services;
        }
    }
}