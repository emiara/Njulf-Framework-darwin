using NjulfFramework.Input.Enums;

namespace NjulfFramework.Input;

/// <summary>
/// Represents a configurable input action that can be bound to multiple input sources.
/// </summary>
public class InputAction
{
    /// <summary>
    /// The name of the action.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The type of the action.
    /// </summary>
    public InputActionType Type { get; set; }

    /// <summary>
    /// The list of bindings for this action.
    /// </summary>
    public List<InputBinding> Bindings { get; set; } = new();

    /// <summary>
    /// The threshold value for triggering the action.
    /// </summary>
    public float Threshold { get; set; } = 0.5f;

    /// <summary>
    /// The current value of the action (for continuous actions).
    /// </summary>
    public float CurrentValue { get; internal set; }

    /// <summary>
    /// Whether the action was triggered this frame.
    /// </summary>
    public bool WasTriggered { get; internal set; }

    /// <summary>
    /// Whether the action is currently active.
    /// </summary>
    public bool IsActive { get; internal set; }

    /// <summary>
    /// Adds a keyboard binding to this action.
    /// </summary>
    public InputAction AddKeyboardBinding(int keyCode, float scale = 1.0f)
    {
        Bindings.Add(InputBinding.ForKeyboard(keyCode, scale));
        return this;
    }

    /// <summary>
    /// Adds a mouse binding to this action.
    /// </summary>
    public InputAction AddMouseBinding(int button, float scale = 1.0f)
    {
        Bindings.Add(InputBinding.ForMouse(button, scale));
        return this;
    }
}