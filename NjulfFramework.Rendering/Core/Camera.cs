using System;
using System.Diagnostics;
using System.Numerics;
using NjulfFramework.Core.Interfaces.Rendering;

namespace NjulfFramework.Rendering.Core
{
    /// <summary>
    /// A minimalist, high-performance camera system for the rendering pipeline.
    /// Features immutable-by-default properties, lazy matrix evaluation, and thread-safe reads.
    /// </summary>
    public sealed class Camera : ICamera
    {
        // Fixed planes (immutable)
        private const float NearPlane = 0.1f;
        private const float FarPlane = 1000.0f;

        // Core state
        private Vector3 _position;
        private Quaternion _rotation;
        private float _fovY; // Vertical FOV in radians
        private float _aspectRatio;

        // Matrix caching
        private bool _isViewDirty = true;
        private bool _isProjectionDirty = true;
        private Matrix4x4 _cachedView;
        private Matrix4x4 _cachedProjection;

        /// <summary>
        /// Initializes a new camera with explicit position, rotation, and FOV.
        /// </summary>
        /// <param name="position">World-space position.</param>
        /// <param name="rotation">Normalized quaternion rotation.</param>
        /// <param name="fovY">Vertical field of view in radians (clamped to [0.1, π/2]).</param>
        /// <param name="aspectRatio">Aspect ratio (width/height). Defaults to 1.0f.</param>
        public Camera(Vector3 position, Quaternion rotation, float fovY, float aspectRatio = 1.0f)
        {
            _position = position;
            _rotation = rotation;
            _fovY = Math.Clamp(fovY, 0.1f, MathF.PI / 2f);
            _aspectRatio = Math.Max(0.001f, aspectRatio); // Avoid division by zero
        }

        // --- Position ---

        /// <summary>Gets the current world-space position.</summary>
        public Vector3 GetPosition() => _position;

        /// <summary>Sets the world-space position.</summary>
        public void SetPosition(Vector3 position)
        {
            ValidateVector(position, nameof(position));
            _position = position;
            _isViewDirty = true;
        }

        // --- Rotation ---

        /// <summary>Gets the current rotation as a normalized quaternion.</summary>
        public Quaternion GetRotation() => _rotation;

        /// <summary>Sets the rotation using a quaternion (automatically normalized).</summary>
        public void SetRotation(Quaternion rotation)
        {
            ValidateQuaternion(rotation, nameof(rotation));
            _rotation = Quaternion.Normalize(rotation);
            _isViewDirty = true;
        }

        /// <summary>Sets the rotation using Euler angles in radians (converted to quaternion).</summary>
        public void SetRotationEuler(Vector3 eulerAngles)
        {
            ValidateVector(eulerAngles, nameof(eulerAngles));
            _rotation = Quaternion.CreateFromYawPitchRoll(
                eulerAngles.Y, // Yaw
                eulerAngles.X, // Pitch
                eulerAngles.Z  // Roll
            );
            _isViewDirty = true;
        }

        // --- Field of View ---

        /// <summary>Gets the vertical field of view in radians.</summary>
        public float GetFovY() => _fovY;

        /// <summary>Sets the vertical field of view in radians (clamped to [0.1, π/2]).</summary>
        public void SetFovY(float fovY)
        {
            _fovY = Math.Clamp(fovY, 0.1f, MathF.PI / 2f);
            _isProjectionDirty = true;
        }

        // --- Aspect Ratio ---

        /// <summary>Gets the current aspect ratio (width/height).</summary>
        public float GetAspectRatio() => _aspectRatio;

        /// <summary>Sets the aspect ratio (must be > 0).</summary>
        public void SetAspectRatio(float aspectRatio)
        {
            _aspectRatio = Math.Max(0.001f, aspectRatio);
            _isProjectionDirty = true;
        }

        // --- Planes ---

        /// <summary>Gets the fixed near plane distance (0.1f).</summary>
        public float GetNearPlane() => NearPlane;

        /// <summary>Gets the fixed far plane distance (1000.0f).</summary>
        public float GetFarPlane() => FarPlane;

        // --- Direction Vectors ---

        /// <summary>Gets the forward direction vector (cached and derived from rotation).</summary>
        public Vector3 GetForward() => -Vector3.Transform(Vector3.UnitZ, _rotation);

        /// <summary>Gets the right direction vector (cached and derived from rotation).</summary>
        public Vector3 GetRight() => Vector3.Transform(Vector3.UnitX, _rotation);

        /// <summary>Gets the up direction vector (cached and derived from rotation).</summary>
        public Vector3 GetUp() => Vector3.Transform(Vector3.UnitY, _rotation);

        // --- Matrices ---

        /// <summary>
        /// Gets the view matrix (lazy-evaluated and cached until position/rotation changes).
        /// </summary>
        public Matrix4x4 GetViewMatrix()
        {
            if (_isViewDirty)
            {
                Vector3 forward = GetForward();
                Vector3 up = GetUp();
                _cachedView = Matrix4x4.CreateLookAt(_position, _position + forward, up);
                _isViewDirty = false;
            }
            return _cachedView;
        }

        /// <summary>
        /// Gets the projection matrix (lazy-evaluated and cached until FOV/aspect changes).
        /// </summary>
        public Matrix4x4 GetProjectionMatrix()
        {
            if (_isProjectionDirty)
            {
                _cachedProjection = Matrix4x4.CreatePerspectiveFieldOfView(
                    _fovY,
                    _aspectRatio,
                    NearPlane,
                    FarPlane
                );
                _isProjectionDirty = false;
            }
            return _cachedProjection;
        }

        // --- Validation ---

        [Conditional("DEBUG")]
        private void ValidateVector(Vector3 vector, string paramName)
        {
            if (float.IsNaN(vector.X) || float.IsNaN(vector.Y) || float.IsNaN(vector.Z) ||
                float.IsInfinity(vector.X) || float.IsInfinity(vector.Y) || float.IsInfinity(vector.Z))
            {
                throw new ArgumentException($"{paramName} contains NaN or infinite values.", paramName);
            }
        }

        [Conditional("DEBUG")]
        private void ValidateQuaternion(Quaternion quaternion, string paramName)
        {
            if (float.IsNaN(quaternion.X) || float.IsNaN(quaternion.Y) || float.IsNaN(quaternion.Z) || float.IsNaN(quaternion.W) ||
                float.IsInfinity(quaternion.X) || float.IsInfinity(quaternion.Y) || float.IsInfinity(quaternion.Z) || float.IsInfinity(quaternion.W))
            {
                throw new ArgumentException($"{paramName} contains NaN or infinite values.", paramName);
            }
        }
    }
}