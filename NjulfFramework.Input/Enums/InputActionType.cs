namespace NjulfFramework.Input.Enums;

/// <summary>
///     Represents the type of input action.
/// </summary>
public enum InputActionType
{
    /// <summary>
    ///     Action triggers instantly when input is detected.
    /// </summary>
    Immediate,

    /// <summary>
    ///     Action provides ongoing values while input is active.
    /// </summary>
    Continuous
}