namespace NjulfFramework.Rendering.Core
{
    /// <summary>
    /// Configurable buffer sizes for the renderer.
    /// All sizes are in bytes.
    /// </summary>
    public struct BufferSizes
    {
        public ulong ObjectBufferSize { get; set; } // Default: 16 MB
        public ulong MaterialBufferSize { get; set; } // Default: 4 MB
        public ulong MeshBufferSize { get; set; } // Default: 8 MB
        public ulong InstanceBufferSize { get; set; } // Default: 16 MB
        public ulong MeshletDrawBufferSize { get; set; } // Default: 32 MB

        public static BufferSizes Default => new BufferSizes
        {
            ObjectBufferSize = 16 * 1024 * 1024,
            MaterialBufferSize = 4 * 1024 * 1024,
            MeshBufferSize = 8 * 1024 * 1204,
            InstanceBufferSize = 16 * 1024 * 1024,
            MeshletDrawBufferSize = 32 * 1024 * 1024
        };
    }
}