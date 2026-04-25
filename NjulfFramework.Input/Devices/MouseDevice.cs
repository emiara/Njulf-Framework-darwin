using Silk.NET.Input;
using Silk.NET.Maths;

namespace NjulfFramework.Input.Devices;

/// <summary>
/// Handles mouse input including position, buttons, and wheel.
/// </summary>
public class MouseDevice
{
    private readonly IMouse _mouse;
    private readonly Dictionary<int, bool> _buttonStates = new();
    private readonly Dictionary<int, bool> _previousButtonStates = new();
    private Vector2D<float> _position;
    private float _wheelDelta;
    private float _previousWheel;

    /// <summary>
    /// Creates a new MouseDevice instance.
    /// </summary>
    public MouseDevice(IMouse mouse)
    {
        _mouse = mouse;
    }

    /// <summary>
    /// Gets the current mouse position.
    /// </summary>
    public Vector2D<float> Position => _position;

    /// <summary>
    /// Gets the X coordinate of the mouse position.
    /// </summary>
    public float X => _position.X;

    /// <summary>
    /// Gets the Y coordinate of the mouse position.
    /// </summary>
    public float Y => _position.Y;

    /// <summary>
    /// Gets the current wheel value.
    /// </summary>
    public float Wheel => _wheelDelta;

    /// <summary>
    /// Gets whether a mouse button is currently pressed.
    /// </summary>
    public bool IsButtonPressed(int button)
    {
        return _buttonStates.TryGetValue(button, out bool state) && state;
    }

    /// <summary>
    /// Gets whether a mouse button was just pressed this frame.
    /// </summary>
    public bool WasButtonPressed(int button)
    {
        bool currentState = _buttonStates.TryGetValue(button, out bool state) && state;
        bool previousState = _previousButtonStates.TryGetValue(button, out bool prevState) && prevState;
        return currentState && !previousState;
    }

    /// <summary>
    /// Gets whether a mouse button was just released this frame.
    /// </summary>
    public bool WasButtonReleased(int button)
    {
        bool currentState = _buttonStates.TryGetValue(button, out bool state) && state;
        bool previousState = _previousButtonStates.TryGetValue(button, out bool prevState) && prevState;
        return !currentState && previousState;
    }

    /// <summary>
    /// Updates the mouse state from the underlying device.
    /// </summary>
    public void Update()
    {
        // Save current button states as previous
        _previousButtonStates.Clear();
        foreach (var kvp in _buttonStates)
        {
            _previousButtonStates[kvp.Key] = kvp.Value;
        }

        // Get current state
        var nativeMouse = _mouse.NativeMouse;
        _position = nativeMouse.Position;
        _previousWheel = _wheelDelta;
        _wheelDelta = nativeMouse.ScrollY;

        // Update button states (buttons 0-7 should be sufficient for most mice)
        for (int i = 0; i < 8; i++)
        {
            _buttonStates[i] = nativeMouse[i];
        }
    }
}
