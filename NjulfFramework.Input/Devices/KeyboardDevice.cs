using Silk.NET.Input;

namespace NjulfFramework.Input.Devices;

/// <summary>
///     Handles keyboard input including key states and modifiers.
/// </summary>
public class KeyboardDevice
{
    private readonly IKeyboard _keyboard;
    private readonly Dictionary<int, bool> _keyStates = new();
    private readonly Dictionary<int, bool> _previousKeyStates = new();

    /// <summary>
    ///     Creates a new KeyboardDevice instance.
    /// </summary>
    public KeyboardDevice(IKeyboard keyboard)
    {
        _keyboard = keyboard;
    }

    /// <summary>
    ///     Gets whether a key is currently pressed.
    /// </summary>
    public bool IsKeyPressed(int keyCode)
    {
        return _keyStates.TryGetValue(keyCode, out var state) && state;
    }

    /// <summary>
    ///     Gets whether a key was just pressed this frame.
    /// </summary>
    public bool WasKeyPressed(int keyCode)
    {
        var currentState = _keyStates.TryGetValue(keyCode, out var state) && state;
        var previousState = _previousKeyStates.TryGetValue(keyCode, out var prevState) && prevState;
        return currentState && !previousState;
    }

    /// <summary>
    ///     Gets whether a key was just released this frame.
    /// </summary>
    public bool WasKeyReleased(int keyCode)
    {
        var currentState = _keyStates.TryGetValue(keyCode, out var state) && state;
        var previousState = _previousKeyStates.TryGetValue(keyCode, out var prevState) && prevState;
        return !currentState && previousState;
    }

    /// <summary>
    ///     Updates the key states from the underlying keyboard device.
    /// </summary>
    public void Update()
    {
        // Save current states as previous
        _previousKeyStates.Clear();
        foreach (var kvp in _keyStates) _previousKeyStates[kvp.Key] = kvp.Value;

        // Get current states
        for (var i = 0; i <= 255; i++)
        {
            if (!Enum.IsDefined(typeof(Key), i))
            {
                _keyStates[i] = false;
                continue;
            }

            var isPressed = _keyboard.IsKeyPressed((Key)i);
            _keyStates[i] = isPressed;
        }
    }
}