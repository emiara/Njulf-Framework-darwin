using NjulfFramework.Input.Enums;

namespace NjulfFramework.Input;

/// <summary>
///     Builder for creating InputAction instances with a fluent API.
/// </summary>
public class InputActionBuilder
{
    private InputAction _action = new();

    /// <summary>
    ///     Creates a new builder for an input action.
    /// </summary>
    public static InputActionBuilder Create(string name, InputActionType type)
    {
        return new InputActionBuilder
        {
            _action = new InputAction
            {
                Name = name,
                Type = type
            }
        };
    }

    /// <summary>
    ///     Sets the threshold for triggering the action.
    /// </summary>
    public InputActionBuilder SetThreshold(float threshold)
    {
        _action.Threshold = threshold;
        return this;
    }

    /// <summary>
    ///     Adds a keyboard binding to the action.
    /// </summary>
    public InputActionBuilder AddKeyboardBinding(int keyCode, float scale = 1.0f)
    {
        _action.Bindings.Add(InputBinding.ForKeyboard(keyCode, scale));
        return this;
    }

    /// <summary>
    ///     Adds a mouse binding to the action.
    /// </summary>
    public InputActionBuilder AddMouseBinding(int button, float scale = 1.0f)
    {
        _action.Bindings.Add(InputBinding.ForMouse(button, scale));
        return this;
    }

    /// <summary>
    ///     Builds and returns the configured InputAction.
    /// </summary>
    public InputAction Build()
    {
        return _action;
    }
}