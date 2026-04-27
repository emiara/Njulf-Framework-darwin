using NjulfFramework.Input.Interfaces;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace NjulfFramework.Input.Devices;

/// <summary>
///     Handles mouse input including position, buttons, and wheel.
/// </summary>
public class MouseDevice : IMouseDevice
{
    private readonly Dictionary<int, bool> _buttonStates = new();
    private readonly IMouse _mouse;
    private readonly Dictionary<int, bool> _previousButtonStates = new();
    private Vector2D<float> _position;
    private float _previousWheel;

    /// <summary>
    ///     Creates a new MouseDevice instance.
    /// </summary>
    public MouseDevice(IMouse mouse)
    {
        _mouse = mouse;
    }

    /// <summary>
    ///     Gets the current mouse position.
    /// </summary>
    public Vector2D<float> Position => _position;

    /// <summary>
    ///     Gets the X coordinate of the mouse position.
    /// </summary>
    public float X => _position.X;

    /// <summary>
    ///     Gets the Y coordinate of the mouse position.
    /// </summary>
    public float Y => _position.Y;

    /// <summary>
    ///     Gets the current wheel value.
    /// </summary>
    public float Wheel { get; private set; }

    /// <summary>
    ///     Gets whether a mouse button is currently pressed.
    /// </summary>
    public bool IsButtonPressed(int button)
    {
        return _buttonStates.TryGetValue(button, out var state) && state;
    }

    /// <summary>
    ///     Gets whether a mouse button was just pressed this frame.
    /// </summary>
    public bool WasButtonPressed(int button)
    {
        var currentState = _buttonStates.TryGetValue(button, out var state) && state;
        var previousState = _previousButtonStates.TryGetValue(button, out var prevState) && prevState;
        return currentState && !previousState;
    }

    /// <summary>
    ///     Gets whether a mouse button was just released this frame.
    /// </summary>
    public bool WasButtonReleased(int button)
    {
        var currentState = _buttonStates.TryGetValue(button, out var state) && state;
        var previousState = _previousButtonStates.TryGetValue(button, out var prevState) && prevState;
        return !currentState && previousState;
    }

    /// <summary>
    ///     Updates the mouse state from the underlying device.
    /// </summary>
    public void Update()
    {
        // Save current button states as previous
        _previousButtonStates.Clear();
        foreach (var kvp in _buttonStates) _previousButtonStates[kvp.Key] = kvp.Value;

        // Get current state
        _position = new Vector2D<float>(_mouse.Position.X, _mouse.Position.Y);
        _previousWheel = Wheel;
        Wheel = _mouse.ScrollWheels.Count > 0 ? _mouse.ScrollWheels[0].Y : 0f;

        // Update button states (buttons 0-7 should be sufficient for most mice)
        for (var i = 0; i < 8; i++) _buttonStates[i] = _mouse.IsButtonPressed((MouseButton)i);
    }
}