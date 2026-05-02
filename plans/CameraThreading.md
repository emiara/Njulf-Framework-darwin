# Camera Threading Model

## Overview
The `Camera` class is designed for **thread-safe read operations** with **external synchronization for mutations**. This document clarifies the threading guarantees and requirements.

---

## Thread Safety Guarantees

### Safe for Concurrent Reads
- **All getter methods** (`GetPosition`, `GetRotation`, `GetFovY`, `GetAspectRatio`, `GetViewMatrix`, `GetProjectionMatrix`) are thread-safe.
- **Matrix caching** is read-only after initialization and safe for concurrent access.
- **No locks** are used for read operations to maximize performance.

### Mutations Require External Synchronization
- **All setter methods** (`SetPosition`, `SetRotation`, `SetRotationEuler`, `SetFovY`, `SetAspectRatio`) modify internal state.
- **Callers must synchronize** mutations if the `Camera` is accessed from multiple threads.

---

## Example: Thread-Safe Usage

### Single-Threaded Context (No Synchronization Needed)
```csharp
// Safe: Single-threaded access
var camera = new Camera(...);
camera.SetPosition(new Vector3(1, 2, 3));  // No lock needed
Matrix4x4 view = camera.GetViewMatrix();     // No lock needed
```

### Multi-Threaded Context (Explicit Synchronization)
```csharp
private readonly object _cameraLock = new object();

// Thread 1: Updating camera
lock (_cameraLock)
{
    camera.SetPosition(newPosition);
    camera.SetRotation(newRotation);
}

// Thread 2: Reading camera (safe without lock)
Matrix4x4 view = camera.GetViewMatrix();
Matrix4x4 projection = camera.GetProjectionMatrix();
```

---

## Why This Design?

### Performance
- **No locks on reads**: Maximizes throughput for rendering threads.
- **Lazy evaluation**: Matrices are recalculated only when needed, reducing overhead.

### Simplicity
- **Clear ownership**: Caller manages synchronization, avoiding hidden locks or complex internal state.
- **Predictable behavior**: No race conditions if mutations are properly synchronized.

### Compatibility
- **Works with existing patterns**: Matches the synchronization model used in game engines (e.g., Unity’s `Camera`).
- **Easy to integrate**: Can be used with any threading model (e.g., ECS, job systems).

---

## Anti-Patterns

### ❌ Unsynchronized Mutations
```csharp
// UNSAFE: Race condition if another thread reads/writes concurrently
camera.SetPosition(newPosition);
```

### ❌ Over-Synchronizing Reads
```csharp
// UNNECESSARY: Locks hurt performance for read-only operations
lock (_cameraLock)
{
    Matrix4x4 view = camera.GetViewMatrix(); // Safe without lock
}
```

---

## Recommendations

1. **Dedicate a Thread for Mutations**:
   - Use a single thread (e.g., "main thread" or "input thread") for all camera mutations.
   - Example: Input systems update the camera, while rendering threads read it.

2. **Batch Mutations**:
   - Group multiple setters (e.g., position + rotation) in a single lock to minimize contention.

3. **Use Double Buffering for Advanced Scenarios**:
   - For frame synchronization (e.g., rendering last frame’s camera while updating the next), use a double-buffered pattern:
   ```csharp
   class DoubleBufferedCamera
   {
       private Camera _current;
       private Camera _next;
       private object _lock = new object();
       
       public void Update(Action<Camera> updater)
       {
           lock (_lock)
           {
               updater(_next);
               Swap(ref _current, ref _next);
           }
       }
       
       public Camera Current => _current;
   }
   ```

---

## Validation in Multi-Threaded Scenarios
- **Debug Builds**: Assertions in setters will catch NaN/infinite values even in multi-threaded contexts.
- **Release Builds**: Clamping/sanitization ensures no corrupt state, but synchronization is still required for correctness.

---

## Integration with NjulfFramework
- **Rendering Pipeline**: Safe to call `GetViewMatrix()`/`GetProjectionMatrix()` from any thread (e.g., Vulkan command buffer recording).
- **Input Systems**: Must synchronize mutations (e.g., via `lock` or single-threaded updates).
- **Scene Systems**: Read-only access is thread-safe; no synchronization needed for culling or transforms.

---

## FAQ

### Q: Why not use `readonly` or immutable structs?
A: The `Camera` is designed for **performance-critical rendering**. Immutable structs would require creating new instances for every change (e.g., per-frame updates), leading to allocations and cache misses. The current design balances mutability with thread safety.

### Q: Can I use `Camera` with async/await?
A: Yes, but ensure mutations are synchronized. Example:
```csharp
await Task.Run(() =>
{
    lock (_cameraLock)
    {
        camera.SetPosition(newPosition);
    }
});
```

### Q: How do I debug threading issues?
A: Enable debug assertions and wrap mutations in locks. If you suspect a race condition:
1. Add logging to setters/getters.
2. Use `System.Threading.Monitor` to detect lock contention.
3. Verify all mutations are synchronized.

---

## Summary Table
| Operation               | Thread-Safe? | Synchronization Required? |
|-------------------------|---------------|----------------------------|
| `GetPosition()`         | ✅ Yes         | ❌ No                      |
| `GetRotation()`         | ✅ Yes         | ❌ No                      |
| `GetFovY()`             | ✅ Yes         | ❌ No                      |
| `GetAspectRatio()`      | ✅ Yes         | ❌ No                      |
| `GetViewMatrix()`       | ✅ Yes         | ❌ No                      |
| `GetProjectionMatrix()` | ✅ Yes         | ❌ No                      |
| `SetPosition()`         | ❌ No          | ✅ Yes                     |
| `SetRotation()`         | ❌ No          | ✅ Yes                     |
| `SetFovY()`             | ❌ No          | ✅ Yes                     |
| `SetAspectRatio()`      | ❌ No          | ✅ Yes                     |