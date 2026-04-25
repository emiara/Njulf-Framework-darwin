using NjulfFramework.Input.Devices;
using NjulfFramework.Input.Enums;

namespace NjulfFramework.Input;

/// <summary>
/// Central hub for input handling and state management.
/// </summary>
public class InputManager
{
    private KeyboardDevice? _keyboard;
    private MouseDevice? _mouse;
    private readonly Dictionary<string, InputAction> _actions = new();

    /// <summary>
    /// Gets the keyboard device.
    /// </summary>
    public KeyboardDevice? Keyboard => _keyboard;

    /// <summary>
    /// Gets the mouse device.
    /// </summary>
    public MouseDevice? Mouse => _mouse;

    /// <summary>
    /// Initializes the input system with keyboard and mouse devices.
    /// </summary>
    public void Initialize(Silk.NET.Input.IKeyboard keyboard, Silk.NET.Input.IMouse mouse)
    {
        _keyboard = new KeyboardDevice(keyboard);
        _mouse = new MouseDevice(mouse);
    }

    /// <summary>
    /// Updates all input devices and evaluates all registered actions.
    /// </summary>
    public void Update()
    {
        // Update device states
        _keyboard?.Update();
        _mouse?.Update();

        // Reset action states before evaluation
        foreach (var action in _actions.Values)
        {
            action.CurrentValue = 0f;
            action.WasTriggered = false;
            action.IsActive = false;
        }

        // Evaluate all actions
        foreach (var action in _actions.Values)
        {
            EvaluateAction(action);
        }
    }

    /// <summary>
    /// Registers an input action.
    /// </summary>
    public void RegisterAction(InputAction action)
    {
        _actions[action.Name] = action;
    }

    /// <summary>
    /// Unregisters an input action.
    /// </summary>
    public void UnregisterAction(string actionName)
    {
        _actions.Remove(actionName);
    }

    /// <summary>
    /// Gets whether an action was triggered this frame.
    /// </summary>
    public bool GetActionState(string actionName)
    {
        if (_actions.TryGetValue(actionName, out var action))
        {
            return action.WasTriggered;
        }
        return false;
    }

    /// <summary>
    /// Gets whether an action is currently active.
    /// </summary>
    public bool IsActionActive(string actionName)
    {
        if (_actions.TryGetValue(actionName, out var action))
        {
            return action.IsActive;
        }
        return false;
    }

    /// <summary>
    /// Gets the current value of a continuous action.
    /// </summary>
    public float GetAxis(string actionName)
    {
        if (_actions.TryGetValue(actionName, out var action))
        {
            return action.CurrentValue;
        }
        return 0f;
    }

    /// <summary>
    /// Gets all registered action names.
    /// </summary>
    public IEnumerable<string> ActionNames => _actions.Keys;

    private void EvaluateAction(InputAction action)
    {
        float totalValue = 0f;

        foreach (var binding in action.Bindings)
        {
            float inputValue = 0f;

            if (binding.Device == InputDeviceType.Keyboard && _keyboard != null)
            {
                if (_keyboard.IsKeyPressed(binding.KeyCode))
                {
                    inputValue = 1f;
                }
            }
            else if (binding.Device == InputDeviceType.Mouse && _mouse != null)
            {
                if (_mouse.IsButtonPressed(binding.Button))
                {
                    inputValue = 1f;
                }
            }

            totalValue += inputValue * binding.Scale;
        }

        // Apply threshold
        bool wasActive = action.IsActive;
        action.IsActive = totalValue >= action.Threshold;
        action.CurrentValue = totalValue;

        // For immediate actions, trigger if value exceeds threshold and wasn't active before
        if (action.Type == Enums.InputActionType.Immediate)
        {
            action.WasTriggered = action.IsActive && !wasActive;
        }
        else
        {
            // For continuous actions, always trigger while active
            action.WasTriggered = action.IsActive;
        }
    }
}
