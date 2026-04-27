namespace NjulfFramework.Input.Interfaces
{
    /// <summary>
    ///     Handles keyboard input including key states and modifiers.
    /// </summary>
    public interface IKeyboardDevice
    {
        /// <summary>
        ///     Gets whether a key is currently pressed.
        /// </summary>
        /// <param name="keyCode">The key code</param>
        /// <returns>True if the key is pressed</returns>
        bool IsKeyPressed(int keyCode);

        /// <summary>
        ///     Gets whether a key was just pressed this frame.
        /// </summary>
        /// <param name="keyCode">The key code</param>
        /// <returns>True if the key was just pressed</returns>
        bool WasKeyPressed(int keyCode);

        /// <summary>
        ///     Gets whether a key was just released this frame.
        /// </summary>
        /// <param name="keyCode">The key code</param>
        /// <returns>True if the key was just released</returns>
        bool WasKeyReleased(int keyCode);

        /// <summary>
        ///     Updates the key states from the underlying keyboard device.
        /// </summary>
        void Update();
    }
}