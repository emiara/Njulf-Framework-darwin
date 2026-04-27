using Silk.NET.Maths;

namespace NjulfFramework.Input.Interfaces
{
    /// <summary>
    ///     Handles mouse input including position, buttons, and wheel.
    /// </summary>
    public interface IMouseDevice
    {
        /// <summary>
        ///     Gets the current mouse position.
        /// </summary>
        Vector2D<float> Position { get; }

        /// <summary>
        ///     Gets the X coordinate of the mouse position.
        /// </summary>
        float X { get; }

        /// <summary>
        ///     Gets the Y coordinate of the mouse position.
        /// </summary>
        float Y { get; }

        /// <summary>
        ///     Gets the current wheel value.
        /// </summary>
        float Wheel { get; }

        /// <summary>
        ///     Gets whether a mouse button is currently pressed.
        /// </summary>
        /// <param name="button">The button index</param>
        /// <returns>True if the button is pressed</returns>
        bool IsButtonPressed(int button);

        /// <summary>
        ///     Gets whether a mouse button was just pressed this frame.
        /// </summary>
        /// <param name="button">The button index</param>
        /// <returns>True if the button was just pressed</returns>
        bool WasButtonPressed(int button);

        /// <summary>
        ///     Gets whether a mouse button was just released this frame.
        /// </summary>
        /// <param name="button">The button index</param>
        /// <returns>True if the button was just released</returns>
        bool WasButtonReleased(int button);

        /// <summary>
        ///     Updates the mouse state from the underlying device.
        /// </summary>
        void Update();
    }
}