using System.Collections.Generic;
using Silk.NET.Input;

namespace NjulfFramework.Input.Interfaces
{
    /// <summary>
    ///     Central hub for input handling and state management.
    /// </summary>
    public interface IInputManager
    {
        /// <summary>
        ///     Gets the keyboard device.
        /// </summary>
        IKeyboardDevice? Keyboard { get; }

        /// <summary>
        ///     Gets the mouse device.
        /// </summary>
        IMouseDevice? Mouse { get; }

        /// <summary>
        ///     Gets the names of all registered actions.
        /// </summary>
        IEnumerable<string> ActionNames { get; }

        /// <summary>
        ///     Initializes the input system with keyboard and mouse devices.
        /// </summary>
        /// <param name="keyboard">The keyboard device</param>
        /// <param name="mouse">The mouse device</param>
        void Initialize(IKeyboard keyboard, IMouse mouse);

        /// <summary>
        ///     Updates all input devices and evaluates all registered actions.
        /// </summary>
        void Update();

        /// <summary>
        ///     Registers an input action.
        /// </summary>
        /// <param name="action">The action to register</param>
        void RegisterAction(InputAction action);

        /// <summary>
        ///     Gets whether an action was triggered this frame.
        /// </summary>
        /// <param name="actionName">Name of the action</param>
        /// <returns>True if the action was triggered</returns>
        bool WasActionTriggered(string actionName);

        /// <summary>
        ///     Gets whether an action is currently active.
        /// </summary>
        /// <param name="actionName">Name of the action</param>
        /// <returns>True if the action is active</returns>
        bool IsActionActive(string actionName);

        /// <summary>
        ///     Gets the current value of a continuous action.
        /// </summary>
        /// <param name="actionName">Name of the action</param>
        /// <returns>The current action value</returns>
        float GetAxis(string actionName);
    }
}