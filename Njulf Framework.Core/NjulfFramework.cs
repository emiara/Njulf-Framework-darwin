using Silk.NET.Windowing;
using Silk.NET.Maths;
using BepuPhysics;

namespace Njulf_Framework.Core;

public class NjulfFramework
{
    /// <summary>
    /// Core framework base class that games will inherit from.
    /// Provides the main game loop and lifecycle hooks.
    /// </summary>
    public abstract class GameFramework
    {
        protected IWindow? Window { get; private set; }
        protected Simulation? PhysicsSimulation { get; private set; }
        protected float DeltaTime { get; private set; }
        
        private const uint WindowWidth = 1280;
        private const uint WindowHeight = 720;
        private const string WindowTitle = "Njulf Framework Game";

        /// <summary>
        /// Initializes and runs the game framework.
        /// </summary>
        public void Run()
        {
            InitializeWindow();
            InitializePhysics();
            
            if (Window == null)
                throw new InvalidOperationException("Window failed to initialize");

            Load();

            Window.Update += OnUpdate;
            Window.Render += OnRender;
            Window.Closing += OnClosing;

            Window.Run();

            Cleanup();
        }

        /// <summary>
        /// Initializes the Silk.NET window with Vulkan rendering context.
        /// </summary>
        private void InitializeWindow()
        {
            var options = WindowOptions.Default with
            {
                Title = WindowTitle,
                Size = new Vector2D<int>((int)WindowWidth, (int)WindowHeight),
                VSync = true,
                API = new GraphicsAPI()
                {
                    API = ContextAPI.Vulkan,
                    Profile = ContextProfile.Core,
                    Flags = ContextFlags.Default
                }
            };

            Window = Silk.NET.Windowing.Window.Create(options);
        }

        /// <summary>
        /// Initializes the BepiPhysics2 simulation.
        /// </summary>
        private void InitializePhysics()
        {
            // Physics simulation will be configured here
            // Using BepiPhysics2 for 3D rigid body physics
            // TODO: Create simulation with proper callbacks and settings
        }

        /// <summary>
        /// Called when the window closes.
        /// </summary>
        private void OnClosing()
        {
            Window?.Close();
        }

        /// <summary>
        /// Called once per frame for update logic.
        /// </summary>
        private void OnUpdate(double deltaTimeSeconds)
        {
            DeltaTime = (float)deltaTimeSeconds;
            
            // Update physics simulation
            UpdatePhysics(DeltaTime);
            
            // Call the game's update method
            Update(DeltaTime);
        }

        /// <summary>
        /// Called once per frame for rendering.
        /// </summary>
        private void OnRender(double deltaTimeSeconds)
        {
            // Clear the rendering context
            // TODO: Implement Vulkan clear operations
            
            // Call the game's draw method
            Draw();
            
            // Present the rendered frame
            // TODO: Implement Vulkan presentation
        }

        /// <summary>
        /// Updates the physics simulation.
        /// </summary>
        private void UpdatePhysics(float deltaTime)
        {
            if (PhysicsSimulation == null)
                return;

            // Step the physics simulation
            // TODO: Implement BepiPhysics2 timestep
        }

        /// <summary>
        /// Abstract method called once when the game first loads.
        /// Override this to initialize your game resources.
        /// </summary>
        public abstract void Load();

        /// <summary>
        /// Abstract method called every frame for game logic updates.
        /// </summary>
        public abstract void Update(float deltaTime);

        /// <summary>
        /// Abstract method called every frame for rendering.
        /// </summary>
        public abstract void Draw();

        /// <summary>
        /// Called when the framework is shutting down.
        /// Override to cleanup resources.
        /// </summary>
        public virtual void Cleanup()
        {
            PhysicsSimulation?.Dispose();
            Window?.Dispose();
        }
    }
}