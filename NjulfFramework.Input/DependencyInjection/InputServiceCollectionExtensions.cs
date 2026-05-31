using Microsoft.Extensions.DependencyInjection;
using NjulfFramework.Input.Interfaces;

namespace NjulfFramework.Input.DependencyInjection
{
    public static class InputServiceCollectionExtensions
    {
        public static IServiceCollection AddNjulfFrameworkInput(this IServiceCollection services)
        {
            services.AddSingleton<IInputManager, InputManager>();
            return services;
        }
    }
}