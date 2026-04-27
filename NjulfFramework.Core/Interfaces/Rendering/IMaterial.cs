namespace NjulfFramework.Core.Interfaces.Rendering
{
    /// <summary>
    /// Interface for material definitions
    /// </summary>
    public interface IMaterial
    {
        /// <summary>
        /// Name of the material
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Path to the shader used by this material
        /// </summary>
        string ShaderPath { get; }

        /// <summary>
        /// Common material properties
        /// </summary>
        // Additional common properties can be added here
    }
}