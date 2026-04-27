using Microsoft.Extensions.DependencyInjection;
using NjulfFramework.Input.Devices;
using NjulfFramework.Input.Interfaces;

namespace NjulfFramework.Input.DependencyInjection
{
    /// <summary>
    /// Extension methods for setting up NjulfFramework.Input services in an IServiceCollection
    /// </summary>
    public static class InputServiceCollectionExtensions
    {
        /// <summary>
        /// Adds NjulfFramework.Input services to the specified IServiceCollection
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to</param>
        /// <returns>The IServiceCollection so that additional calls can be chained</returns>
        public static IServiceCollection AddNjulfFrameworkInput(this IServiceCollection services)
        {
            // Register input services
            services.AddSingleton<IInputManager, InputManager>();
            services.AddSingleton<IKeyboardDevice, KeyboardDevice>();
            services.AddSingleton<IMouseDevice, MouseDevice>();
            
            return services;
        }
    }
}