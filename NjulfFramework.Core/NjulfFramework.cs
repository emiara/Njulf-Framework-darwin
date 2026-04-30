// SPDX-License-Identifier: MPL-2.0

// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using NjulfFramework.Core.Interfaces.Assets;
using NjulfFramework.Core.Interfaces.Rendering;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace NjulfFramework.Core;

/// <summary>
///     Core framework base class that games will inherit from.
///     Provides the main game loop and lifecycle hooks.
/// </summary>
public abstract class GameFramework
{
    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 720;

    private IServiceProvider? _serviceProvider;

    protected IWindow? Window { get; private set; }
    protected IRenderer? Renderer { get; private set; }
    protected IContentManager? Content { get; private set; }   // ← replaces raw AssetLoader
    protected float DeltaTime { get; private set; }

    /// <summary>
    ///     Override to customise the window before it is created.
    /// </summary>
    protected virtual WindowOptions ConfigureWindow() =>
        WindowOptions.DefaultVulkan with
        {
            Title = "Njulf Framework Game",
            Size = new Vector2D<int>(DefaultWindowWidth, DefaultWindowHeight),
            VSync = true,
            WindowBorder = WindowBorder.Fixed
        };

    /// <summary>
    ///     Override to register additional services into the DI container.
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>
    ///     Initializes and runs the game framework.
    /// </summary>
    public void Run()
    {
        Window = Silk.NET.Windowing.Window.Create(ConfigureWindow());

        if (Window is null)
            throw new InvalidOperationException("Window failed to initialize.");

        // Build DI container
        var services = new ServiceCollection();
        services.AddSingleton<IWindow>(Window);
        RegisterFrameworkServices(services);
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Wire window events
        Window.Load    += OnWindowLoad;
        Window.Update  += OnUpdate;
        Window.Render  += OnRender;
        Window.Closing += OnClosing;

        Window.Run();

        Cleanup();
    }

    /// <summary>
    ///     Registers the built-in framework services (rendering, assets, input, …).
    ///     Override and call base to add or replace registrations.
    /// </summary>
    protected virtual void RegisterFrameworkServices(IServiceCollection services)
    {
        // Subclasses (or the concrete game project) call the standard extension
        // methods here, e.g.:
        //   services.AddNjulfFrameworkRendering()
        //           .AddNjulfFrameworkAssets()
        //           .AddNjulfFrameworkInput();
        //
        // They are not called from this base class so that the framework library
        // itself does not carry a hard dependency on every module.
    }

    // -------------------------------------------------------------------------
    // Private event handlers
    // -------------------------------------------------------------------------

    private void OnWindowLoad()
    {
        try
        {
            Renderer = _serviceProvider!.GetService<IRenderer>();
            Content  = _serviceProvider!.GetService<IContentManager>();   // ← resolve ContentManager

            Renderer?.Load();

            Load();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Load failed: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"  Inner: {ex.InnerException.Message}");
            Window?.Close();
        }
    }

    private void OnUpdate(double deltaTimeSeconds)
    {
        DeltaTime = (float)deltaTimeSeconds;
        Update(DeltaTime);
    }

    private void OnRender(double deltaTimeSeconds)
    {
        try
        {
            Renderer?.RenderFrameAsync();
            Draw();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Rendering error: {ex.Message}");
        }
    }

    private void OnClosing()
    {
        // Handled in Cleanup()
    }

    // -------------------------------------------------------------------------
    // Abstract / virtual lifecycle hooks for subclasses
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Called once after the window and renderer are ready.
    ///     Override to load your game resources.
    /// </summary>
    public abstract void Load();

    /// <summary>
    ///     Called every frame for game logic updates.
    /// </summary>
    public abstract void Update(float deltaTime);

    /// <summary>
    ///     Called every frame for custom rendering logic.
    /// </summary>
    public abstract void Draw();

    /// <summary>
    ///     Called when the framework is shutting down.
    ///     Override to release additional resources; always call base.
    /// </summary>
    public virtual void Cleanup()
    {
        try
        {
            Renderer?.Dispose();
            Console.WriteLine("\n✓ Renderer cleaned up");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error during cleanup: {ex.Message}");
        }

        Window?.Dispose();

        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }
}