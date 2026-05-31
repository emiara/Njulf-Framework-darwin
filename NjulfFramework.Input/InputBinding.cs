using NjulfFramework.Input.Enums;

namespace NjulfFramework.Input;

/// <summary>
///     Represents a binding between an input action and a specific input source.
/// </summary>
public class InputBinding
{
    /// <summary>
    ///     The type of device this binding is for.
    /// </summary>
    public InputDeviceType Device { get; set; }

    /// <summary>
    ///     The type of binding (key, mouse button, mouse axis).
    /// </summary>
    public InputBindingType BindingType { get; set; }

    /// <summary>
    ///     The key code for keyboard bindings.
    /// </summary>
    public int KeyCode { get; set; }

    /// <summary>
    ///     The button code for mouse button bindings.
    /// </summary>
    public int Button { get; set; }

    /// <summary>
    ///     The scale factor to apply to the input value.
    /// </summary>
    public float Scale { get; set; } = 1.0f;

    /// <summary>
    ///     Creates a keyboard binding.
    /// </summary>
    public static InputBinding ForKeyboard(int keyCode, float scale = 1.0f)
    {
        return new InputBinding
        {
            Device = InputDeviceType.Keyboard,
            BindingType = InputBindingType.Key,
            KeyCode = keyCode,
            Scale = scale
        };
    }

    /// <summary>
    ///     Creates a mouse button binding.
    /// </summary>
    public static InputBinding ForMouse(int button, float scale = 1.0f)
    {
        return new InputBinding
        {
            Device = InputDeviceType.Mouse,
            BindingType = InputBindingType.MouseButton,
            Button = button,
            Scale = scale
        };
    }

    /// <summary>
    ///     Creates a mouse X axis delta binding.
    /// </summary>
    public static InputBinding ForMouseXAxis(float scale = 1.0f)
    {
        return new InputBinding
        {
            Device = InputDeviceType.Mouse,
            BindingType = InputBindingType.MouseXAxis,
            Scale = scale
        };
    }

    /// <summary>
    ///     Creates a mouse Y axis delta binding.
    /// </summary>
    public static InputBinding ForMouseYAxis(float scale = 1.0f)
    {
        return new InputBinding
        {
            Device = InputDeviceType.Mouse,
            BindingType = InputBindingType.MouseYAxis,
            Scale = scale
        };
    }
}