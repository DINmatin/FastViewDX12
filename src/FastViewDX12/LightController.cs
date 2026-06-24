using System;
using System.Drawing;
using System.Numerics;

namespace FastViewDX12;

/// <summary>
/// Stores direct-light orientation and environment rotation and updates them from mouse drags.
/// </summary>
public sealed class LightController
{
    private float _yaw;
    private float _pitch;

    private bool _isRotating;
    private Point _lastMousePosition;

    private float _environmentYaw;

    /// <summary>
    /// Initializes the light to a useful three-quarter direction.
    /// </summary>
    public LightController()
    {
        SetDirection(
            new Vector3(
                -0.45f,
                1.0f,
                -0.35f));
    }
    /// <summary>
    /// Returns the current environment yaw used by lighting and background sampling.
    /// </summary>
    public float GetEnvironmentRotationRadians()
    {
        return _environmentYaw;
    }
    /// <summary>
    /// Converts yaw and pitch into a normalized world-space direction toward the light.
    /// </summary>
    public Vector3 GetDirectionToLight()
    {
        float cosPitch =
            MathF.Cos(_pitch);

        float sinPitch =
            MathF.Sin(_pitch);

        float cosYaw =
            MathF.Cos(_yaw);

        float sinYaw =
            MathF.Sin(_yaw);

        Vector3 direction =
            new Vector3(
                sinYaw * cosPitch,
                sinPitch,
                cosYaw * cosPitch);

        return Vector3.Normalize(direction);
    }

    /// <summary>
    /// Converts a world-space light direction back into yaw and pitch.
    /// </summary>
    public void SetDirection(Vector3 direction)
    {
        if (direction.LengthSquared() <
            0.000001f)
        {
            direction =
                Vector3.UnitY;
        }
        else
        {
            direction =
                Vector3.Normalize(direction);
        }

        _pitch =
            MathF.Asin(
                Math.Clamp(
                    direction.Y,
                    -1.0f,
                    1.0f));

        _yaw =
            MathF.Atan2(
                direction.X,
                direction.Z);

        ClampPitch();
    }

    /// <summary>
    /// Begins a light-rotation drag.
    /// </summary>
    public void BeginRotate(Point mousePosition)
    {
        _isRotating = true;
        _lastMousePosition = mousePosition;
    }

    /// <summary>
    /// Ends the active light-rotation drag.
    /// </summary>
    public void EndRotate()
    {
        _isRotating = false;
    }

    /// <summary>
    /// Rotates both the direct light and the environment around the model.
    /// </summary>
    public void OnMouseMove(Point mousePosition)
    {
        if (!_isRotating)
        {
            return;
        }

        int deltaX =
            mousePosition.X -
            _lastMousePosition.X;

        int deltaY =
            mousePosition.Y -
            _lastMousePosition.Y;

        _lastMousePosition =
            mousePosition;

        const float rotationSpeed =
            0.01f;

        float yawDelta =
     -deltaX *
     rotationSpeed;

        _yaw +=
            yawDelta;

        _environmentYaw +=
            yawDelta;

        _pitch -=
            deltaY *
            rotationSpeed;

        _environmentYaw =
            NormalizeAngle(
                _environmentYaw);

        ClampPitch();
    }
    /// <summary>
    /// Wraps an angle to the minus-pi through pi interval.
    /// </summary>
    private static float NormalizeAngle(
    float angle)
    {
        while (angle > MathF.PI)
        {
            angle -=
                MathF.PI * 2.0f;
        }

        while (angle < -MathF.PI)
        {
            angle +=
                MathF.PI * 2.0f;
        }

        return angle;
    }
    /// <summary>
    /// Prevents the direct light from reaching unstable vertical poles.
    /// </summary>
    private void ClampPitch()
    {
        const float minimumPitch =
            0.03f;

        const float maximumPitch =
            MathF.PI * 0.49f;

        _pitch =
            Math.Clamp(
                _pitch,
                minimumPitch,
                maximumPitch);
    }
}