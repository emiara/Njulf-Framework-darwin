# NjulfFramework.Rendering.Core

## Camera System

### Overview
The **Camera** system provides a minimalist, high-performance camera implementation for the rendering pipeline. It adheres to strict requirements for encapsulation, immutability, and thread safety.

---

### Features
- **Immutable by Default**: All properties require explicit setters (e.g., `SetPosition()`).
- **Lazy Evaluation**: Matrices are recalculated only when accessed after mutation.
- **Thread-Safe Reads**: Safe for concurrent access to getters and matrices.
- **Validation**: Debug assertions for NaN/infinite values; release builds clamp silently.
- **No Allocations**: Zero dynamic allocations post-initialization.

---

### Public API

#### Construction
```csharp
var camera = new Camera(
    position: new Vector3(0, 0, -5),
    rotation: Quaternion.Identity,
    fovY: MathF.PI / 4f,  // 45° vertical FOV (radians)
    aspectRatio: 16f / 9f  // Defaults to 1.0f if omitted
);
```

#### Properties
| Method                     | Description                                                                                     |
|----------------------------|-------------------------------------------------------------------------------------------------|
| `GetPosition()`            | Returns the current world-space position.                                                    |
| `SetPosition(Vector3)`     | Sets the world-space position (marks view matrix as dirty).                                   |
| `GetRotation()`            | Returns the current rotation as a normalized quaternion.                                      |
| `SetRotation(Quaternion)`  | Sets the rotation using a quaternion (automatically normalized).                              |
| `SetRotationEuler(Vector3)`| Sets the rotation using Euler angles in **radians** (converted to quaternion).               |
| `GetFovY()`                | Returns the vertical field of view in radians.                                               |
| `SetFovY(float)`           | Sets the vertical FOV (clamped to `[0.1, π/2]`).                                              |
| `GetAspectRatio()`         | Returns the aspect ratio (width/height).                                                      |
| `SetAspectRatio(float)`    | Sets the aspect ratio (must be > 0).                                                         |
| `GetNearPlane()`           | Returns the fixed near plane distance (`0.1f`).                                              |
| `GetFarPlane()`            | Returns the fixed far plane distance (`1000.0f`).                                            |

#### Matrices
| Method                     | Description                                                                                     |
|----------------------------|-------------------------------------------------------------------------------------------------|
| `GetViewMatrix()`          | Returns the view matrix (lazy-evaluated and cached).                                          |
| `GetProjectionMatrix()`   | Returns the projection matrix (lazy-evaluated and cached). Supports perspective only.        |

---

### Threading Model

#### Safe Operations (No Synchronization Needed)
- All getter methods (`GetPosition`, `GetRotation`, `GetFovY`, etc.).
- Matrix accessors (`GetViewMatrix`, `GetProjectionMatrix`).

#### Unsafe Operations (Require External Synchronization)
- All setter methods (`SetPosition`, `SetRotation`, etc.).

**Example**:
```csharp
// Thread-safe read (no lock needed)
Matrix4x4 view = camera.GetViewMatrix();

// Thread-safe mutation (requires lock)
lock (cameraLock)
{
    camera.SetPosition(newPosition);
    camera.SetRotation(newRotation);
}
```

---

### Usage Example
```csharp
// Initialize
var camera = new Camera(
    position: new Vector3(0, 2, 3),
    rotation: Quaternion.CreateLookAt(new Vector3(0, 2, 3), Vector3.Zero, Vector3.UnitY),
    fovY: MathF.PI / 4f,
    aspectRatio: 1920f / 1080f
);

// Update (e.g., in input handler)
camera.SetPosition(new Vector3(1, 2, 3));
camera.SetRotationEuler(new Vector3(0, MathF.PI / 2, 0)); // 90° yaw

// Render (e.g., in Vulkan command buffer recording)
Matrix4x4 view = camera.GetViewMatrix();
Matrix4x4 projection = camera.GetProjectionMatrix();
```

---

### Validation and Clamping
| Scenario               | Debug Build          | Release Build                |
|------------------------|-----------------------|------------------------------|
| NaN/Infinite Input     | Throws `ArgumentException` | Silent no-op (ignored)      |
| Invalid FOV (< 0.1)    | Throws `ArgumentException` | Clamped to `0.1f`            |
| Invalid FOV (> π/2)    | Throws `ArgumentException` | Clamped to `π/2`             |
| Zero Aspect Ratio       | Throws `ArgumentException` | Clamped to `0.001f`          |

---

### Performance Notes
- **Matrix Caching**: View/projection matrices are recalculated only when:
  - `GetViewMatrix()` is called after position/rotation changes.
  - `GetProjectionMatrix()` is called after FOV/aspect changes.
- **SIMD Optimization**: Uses `System.Numerics` for vector/quaternion math.
- **Zero Allocations**: No heap allocations after construction.

---

### Integration Points
- **Dependency Injection**: Registered as a singleton in `AddNjulfFrameworkRendering()`.
- **Rendering Pipeline**: Used in `VulkanRenderer` to provide view/projection matrices to shaders.
- **Input Systems**: Compose with a `CameraController` for user-driven movement (not included).

---

### Design Rationale
1. **No Inheritance**: Uses composition over inheritance for extensibility.
2. **No Virtual Methods**: Avoids runtime dispatch overhead.
3. **No Serialization**: Storage/loading is the caller’s responsibility.
4. **Fixed Planes**: Near/far planes are immutable to simplify culling logic.

---

### Extensibility
To add features (e.g., orthographic projection, camera controllers):
1. **Compose**: Create a separate class (e.g., `CameraController`) that holds a `Camera` instance.
2. **Wrap**: Implement additional logic while delegating to the core `Camera`.

**Example**:
```csharp
public class FirstPersonCameraController
{
    private readonly Camera _camera;
    private float _moveSpeed = 5.0f;
    private float _lookSpeed = 2.0f;

    public FirstPersonCameraController(Camera camera) => _camera = camera;

    public void Update(InputState input, float deltaTime)
    {
        // Handle input and update _camera.SetPosition/SetRotation
    }
}
```

---

### FAQ

#### Q: Why not use properties with get/set?
A: Explicit getters/setters enforce immutability by default and make mutations explicit in caller code.

#### Q: Why are near/far planes fixed?
A: Simplifies culling logic and avoids per-frame checks. For dynamic planes, compose with a wrapper class.

#### Q: How do I handle window resizing?
A: Call `SetAspectRatio(newWidth / (float)newHeight)` when the window size changes.

#### Q: Can I use this with ECS?
A: Yes. Treat the `Camera` as a component and synchronize mutations via your ECS framework.

---

### See Also
- [Threading Model](CameraThreading.md)
- [Design Document](CameraDesign.md)