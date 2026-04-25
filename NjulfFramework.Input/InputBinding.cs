using NjulfFramework.Input.Enums;

namespace NjulfFramework.Input;

/// <summary>
/// Represents a binding between an input action and a specific input source.
/// </summary>
public class InputBinding
{
    /// <summary>
    /// The type of device this binding is for.
    /// </summary>
    public InputDeviceType Device { get; set; }

    /// <summary>
    /// The key code for keyboard bindings.
    /// </summary>
    public int KeyCode { get; set; }

    /// <summary>
    /// The button code for mouse bindings.
    /// </summary>
    public int Button { get; set; }

    /// <summary>
    /// The scale factor to apply to the input value.
    /// </summary>
    public float Scale { get; set; } = 1.0f;

    /// <summary>
    /// Creates a keyboard binding.
    /// </summary>
    public static InputBinding ForKeyboard(int keyCode, float scale = 1.0f)
    {
        return new InputBinding
        {
            Device = InputDeviceType.Keyboard,
            KeyCode = keyCode,
            Scale = scale
        };
    }

    /// <summary>
    /// Creates a mouse binding.
    /// </summary>
    public static InputBinding ForMouse(int button, float scale = 1.0f)
    {
        return new InputBinding
        {
            Device = InputDeviceType.Mouse,
            Button = button,
            Scale = scale
        };
    }
}
