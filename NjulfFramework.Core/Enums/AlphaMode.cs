namespace NjulfFramework.Core.Enums
{
    /// <summary>
    /// Enum for alpha blending modes
    /// </summary>
    public enum AlphaMode
    {
        /// <summary>
        /// Opaque mode (no transparency)
        /// </summary>
        Opaque = 0,

        /// <summary>
        /// Mask mode (cutout transparency)
        /// </summary>
        Mask = 1,

        /// <summary>
        /// Blend mode (semi-transparent)
        /// </summary>
        Blend = 2
    }
}