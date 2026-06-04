using Microsoft.Extensions.DependencyInjection;
using NjulfFramework.Assets.DependencyInjection;
using NjulfFramework.Core;
using NjulfFramework.Core.DependencyInjection;
using NjulfFramework.Core.Interfaces.Assets;
using NjulfFramework.Core.Interfaces.Rendering;
using NjulfFramework.Input;
using NjulfFramework.Input.DependencyInjection;
using NjulfFramework.Input.Enums;
using NjulfFramework.Input.Interfaces;
using NjulfFramework.Rendering;
using NjulfFramework.Rendering.DependencyInjection;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using System.Numerics;
using System.Reflection;

namespace NjulfFramework;

internal sealed class RendererExample : GameFramework
{
    private const string GltfModelPath = "vintage_video_camera_2k.gltf";
    private const float CameraMoveSpeed = 5f;
    private const float MouseLookSensitivity = 0.01f;
    
    private NjulfFramework.Assets.Models.FrameworkModel? _model;
    private IInputManager? _inputManager;
    private bool _isMouseLookActive = false;
    private float _yaw = 0f;
    private float _pitch = 0f;
    private float _diagnosticTimer = 0f;
    private const float DiagnosticInterval = 1.0f; // Check mesh buffer status once per second

    private static void Main(string[] args) => new RendererExample().Run();

    protected override WindowOptions ConfigureWindow() =>
        WindowOptions.DefaultVulkan with
        {
            Title = "YourFramework - Vulkan Triangle",
            Size = new Vector2D<int>(1920, 1080),
            VSync = false,
            WindowBorder = WindowBorder.Fixed
        };

    protected override void RegisterFrameworkServices(IServiceCollection services)
    {
        services.AddNjulfFrameworkCore()
                .AddNjulfFrameworkRendering()
                .AddNjulfFrameworkAssets()
                .AddNjulfFrameworkInput();
    }

    public override void Load()
    {
        // Load the model - it's automatically added to the scene
        _model = Content?.Load<IModel>(GltfModelPath) as NjulfFramework.Assets.Models.FrameworkModel;
        
        // Debug: Validate model loading
        if (_model == null)
        {
            Console.WriteLine("[CRITICAL] Model failed to load! Check path: " + GltfModelPath);
        }
        else
        {
            Console.WriteLine($"[OK] Model '{_model.Name}' loaded: {_model.Meshes.Count} mesh(es)");
        }
        
        _model?.SetPosition(new Vector3(0, 0, 0));

        // Directly access the camera from the framework
        Camera?.SetPosition(new Vector3(0, 0, -5));

        // Get input manager from the service provider using reflection
        // (since _serviceProvider is private in GameFramework)
        var serviceProviderField = typeof(GameFramework).GetField("_serviceProvider", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        if (serviceProviderField?.GetValue(this) is IServiceProvider serviceProvider)
        {
            _inputManager = serviceProvider.GetService<IInputManager>();
        }
        
        if (_inputManager != null)
        {
            // Register movement actions
            _inputManager.RegisterAction(InputActionBuilder.Create("MoveForward", InputActionType.Continuous)
                .AddKeyboardBinding((int)Key.W)
                .Build());
            
            _inputManager.RegisterAction(InputActionBuilder.Create("MoveBack", InputActionType.Continuous)
                .AddKeyboardBinding((int)Key.S)
                .Build());
            
            _inputManager.RegisterAction(InputActionBuilder.Create("MoveLeft", InputActionType.Continuous)
                .AddKeyboardBinding((int)Key.A)
                .Build());
            
            _inputManager.RegisterAction(InputActionBuilder.Create("MoveRight", InputActionType.Continuous)
                .AddKeyboardBinding((int)Key.D)
                .Build());
            
            _inputManager.RegisterAction(InputActionBuilder.Create("MoveUp", InputActionType.Continuous)
                .AddKeyboardBinding((int)Key.E)
                .Build());
            
            _inputManager.RegisterAction(InputActionBuilder.Create("MoveDown", InputActionType.Continuous)
                .AddKeyboardBinding((int)Key.Q)
                .Build());

            // Register mouse look actions (continuous axis input)
            _inputManager.RegisterAction(InputActionBuilder.Create("LookX", InputActionType.Continuous)
                .AddMouseXBinding(MouseLookSensitivity)
                .Build());

            _inputManager.RegisterAction(InputActionBuilder.Create("LookY", InputActionType.Continuous)
                .AddMouseYBinding(MouseLookSensitivity)
                .Build());

            // Register mouse look toggle (right mouse button)
            _inputManager.RegisterAction(InputActionBuilder.Create("ToggleMouseLook", InputActionType.Immediate)
                .AddMouseBinding((int)MouseButton.Right)
                .Build());
        }

        // Optional: Adjust camera settings
        // Camera?.SetFovY(MathF.PI / 3f); // 60° FOV
        // Camera?.SetRotationEuler(new Vector3(0.2f, 0, 0)); // Tilt down slightly

        // Add lights to the scene
        if (Renderer?.LightManager != null)
        {
            Renderer.LightManager.AddPointLight(new Vector3(5, 5, 5), 10, new Vector3(10, 1, 1), 1.0f);
            Renderer.LightManager.AddPointLight(new Vector3(-5, 5, -5), 10, new Vector3(1, 4.5f, 0), 0.8f);
        }
    }

    public override void Update(float deltaTime)
    {
        VulkanRenderer? vulkanRenderer = Renderer as VulkanRenderer;
        
        // Debug: Validate mesh buffer status (check once per second)
        _diagnosticTimer += deltaTime;
        if (_diagnosticTimer >= DiagnosticInterval && vulkanRenderer != null)
        {
            ValidateMeshBufferStatus(vulkanRenderer);
            _diagnosticTimer = 0f;
        }

        // Update input manager
        _inputManager?.Update();

        // Handle camera movement and look based on input
        if (_inputManager != null && Camera != null)
        {
            var currentPosition = Camera.GetPosition();
            var moveDirection = Vector3.Zero;
            
            // Get camera direction vectors
            var forward = Camera.GetForward();
            var right = Camera.GetRight();
            var up = Camera.GetUp();
            
            // Handle WASD movement (horizontal plane)
            if (_inputManager.IsActionActive("MoveForward"))
                moveDirection += forward;
            if (_inputManager.IsActionActive("MoveBack"))
                moveDirection -= forward;
            if (_inputManager.IsActionActive("MoveLeft"))
                moveDirection -= right;
            if (_inputManager.IsActionActive("MoveRight"))
                moveDirection += right;
            
            // Handle QE movement (vertical)
            if (_inputManager.IsActionActive("MoveUp"))
                moveDirection -= up;
            if (_inputManager.IsActionActive("MoveDown"))
                moveDirection += up;
            
            // Normalize and apply movement
            if (moveDirection != Vector3.Zero)
            {
                moveDirection = Vector3.Normalize(moveDirection);
                var moveAmount = CameraMoveSpeed * deltaTime;
                Camera.SetPosition(currentPosition + moveDirection * moveAmount);
            }

            // Handle mouse look
            HandleMouseLook(deltaTime);
        }

        vulkanRenderer?.Update(deltaTime);
        
        // Mark first frame as completed to prevent false positives in diagnostic
        _firstFrameCompleted = true;
    }

    private bool _firstFrameCompleted = false;

    private void ValidateMeshBufferStatus(VulkanRenderer renderer)
    {
        // Industry-standard diagnostic: Check meshlet draw count and buffer registration
        // This helps identify the grey screen root cause (empty bindless heap buffers)
        try
        {
            var sceneBuilderField = typeof(VulkanRenderer).GetField("_sceneBuilder", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            var meshManagerField = typeof(VulkanRenderer).GetField("_meshManager", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (sceneBuilderField?.GetValue(renderer) is object sceneBuilder)
            {
                var meshletDrawCountProp = sceneBuilder.GetType().GetProperty("MeshletDrawCount");
                if (meshletDrawCountProp != null)
                {
                    var drawCount = (uint)meshletDrawCountProp.GetValue(sceneBuilder);
                    Console.WriteLine($"[DIAG] MeshletDrawCount: {drawCount}");
                    
                    // Only warn about 0 draw count after first frame to avoid false positives
                    // from diagnostic running before first Draw()
                    if (drawCount == 0 && _firstFrameCompleted)
                    {
                        Console.WriteLine("[WARNING] MeshletDrawCount = 0 => No draw calls issued!");
                        Console.WriteLine("  Root Cause: Meshlet data not generated OR mesh buffers not uploaded");
                    }
                }
            }
            
            if (meshManagerField?.GetValue(renderer) is NjulfFramework.Rendering.Resources.MeshManager meshManager)
            {
                var isFinalized = meshManager.IsFinalized;
                var handlesValid = meshManager.AreAllBufferHandlesValid();
                
                Console.WriteLine($"[DIAG] MeshManager.Finalized: {isFinalized}");
                Console.WriteLine($"[DIAG] All Buffer Handles Valid: {handlesValid}");
                
                if (!isFinalized)
                {
                    Console.WriteLine("[CRITICAL] MeshManager NOT finalized => Buffers not allocated!");
                    Console.WriteLine("  Action: Call FinalizeAndUpdateMeshBuffers() after loading meshes");
                }
                else if (!handlesValid)
                {
                    var handles = meshManager.GetAllMeshBufferHandles();
                    Console.WriteLine("[DIAG] Buffer Handles:");
                    Console.WriteLine($"  VertexBuffer (index 3): {handles.VertexHandle.IsValid}");
                    Console.WriteLine($"  IndexBuffer: {handles.IndexHandle.IsValid}");
                    Console.WriteLine($"  MeshletBuffer (index 5): {handles.MeshletHandle.IsValid}");
                    Console.WriteLine($"  MeshletVertexIndexBuffer (index 6): {handles.MeshletVertexIndicesHandle.IsValid}");
                    Console.WriteLine($"  MeshletTriangleIndexBuffer (index 7): {handles.MeshletTriangleIndicesHandle.IsValid}");
                    Console.WriteLine("[CRITICAL] One or more mesh buffers are Invalid!");
                    Console.WriteLine("  Grey screen cause: Buffers at bindless indices 3,5,6,7 contain zero-initialized data");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIAG ERROR] {ex.Message}");
        }
    }

    private void HandleMouseLook(float deltaTime)
    {
        if (_inputManager == null || Camera == null)
            return;

        // Toggle mouse look mode with right mouse button
        if (_inputManager.WasActionTriggered("ToggleMouseLook"))
        {
            _isMouseLookActive = !_isMouseLookActive;
            // TODO: Consider cursor locking here for proper FPS-style mouse look
            Console.WriteLine("TOGGLE");
        }

        // Only apply mouse look when active
        if (_isMouseLookActive)
        {
            // Get mouse delta values from the look actions (already scaled by sensitivity)
            var lookX = _inputManager.GetAxis("LookX");
            var lookY = _inputManager.GetAxis("LookY");

            // Invert Y axis for standard FPS camera (mouse up = look up)
            lookY *= -1f;

            // Accumulate rotation (frame-rate independent)
            _yaw += -lookX;
            _pitch += -lookY;

            // Clamp pitch to avoid over-rotation (prevents camera from going upside down)
            _pitch = Math.Clamp(_pitch, -MathF.PI / 2f + 0.01f, MathF.PI / 2f - 0.01f);
            
            Console.WriteLine($"Yaw: {_yaw}, Pitch: {_pitch}");

            // Apply rotation to camera using Euler angles
            // Camera.SetRotationEuler expects Vector3(pitch, yaw, roll)
            // because it uses Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll)
            Camera.SetRotationEuler(new Vector3(_pitch, _yaw, 0));
        }
    }

    public override void Draw() { }
}