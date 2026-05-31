// SPDX-License-Identifier: MPL-2.0

using System.Numerics;

namespace NjulfFramework.Core.Interfaces.Rendering;

/// <summary>
///     Interface for camera control. Allows game code to manipulate camera
///     position, rotation, and lens properties without coupling to a specific
///     camera implementation.
/// </summary>
public interface ICamera
{
    /// <summary>Gets the current world-space position.</summary>
    Vector3 GetPosition();

    /// <summary>Sets the world-space position.</summary>
    void SetPosition(Vector3 position);

    /// <summary>Gets the current rotation as a normalized quaternion.</summary>
    Quaternion GetRotation();

    /// <summary>Gets the forward direction vector (world-space).</summary>
    Vector3 GetForward();

    /// <summary>Gets the right direction vector (world-space).</summary>
    Vector3 GetRight();

    /// <summary>Gets the up direction vector (world-space).</summary>
    Vector3 GetUp();

    /// <summary>Sets the rotation using a quaternion (automatically normalized).</summary>
    void SetRotation(Quaternion rotation);

    /// <summary>Sets the rotation using Euler angles in radians (Yaw, Pitch, Roll).</summary>
    void SetRotationEuler(Vector3 eulerAngles);

    /// <summary>Gets the vertical field of view in radians.</summary>
    float GetFovY();

    /// <summary>Sets the vertical field of view in radians (clamped to [0.1, π/2]).</summary>
    void SetFovY(float fovY);

    /// <summary>Gets the current aspect ratio (width/height).</summary>
    float GetAspectRatio();

    /// <summary>Sets the aspect ratio (must be > 0).</summary>
    void SetAspectRatio(float aspectRatio);

    /// <summary>Gets the view matrix.</summary>
    Matrix4x4 GetViewMatrix();

    /// <summary>Gets the projection matrix.</summary>
    Matrix4x4 GetProjectionMatrix();

    /// <summary>Gets the fixed near plane distance.</summary>
    float GetNearPlane();

    /// <summary>Gets the fixed far plane distance.</summary>
    float GetFarPlane();
}
