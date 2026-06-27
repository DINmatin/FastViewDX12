using System;
using System.Drawing;
using System.Numerics;

namespace FastViewDX12;

/// <summary>
/// Maintains an orbit camera around a scene target and translates WinForms mouse input into orbit, pan, and zoom operations.
/// </summary>
public sealed class CameraController
{
    private Vector3 _target = Vector3.Zero;

    private float _distance = 5.0f;
    private float _yaw;
    private float _pitch;

    private int _viewportWidth = 1;
    private int _viewportHeight = 1;

    private bool _isOrbiting;
    private bool _isPanning;

    // Raw orbit angles remain continuous while the displayed camera is
    // temporarily snapped with Shift. This prevents snapping from destroying
    // the underlying mouse-drag position.
    private float _orbitRawYaw;
    private float _orbitRawPitch;
    private bool _orbitSnapActive;

    private Point _lastMouse;

    /// <summary>
    /// Updates the aspect ratio used by camera fitting and projection calculations.
    /// </summary>
    public void SetViewport(int width, int height)
    {
        _viewportWidth = Math.Max(1, width);
        _viewportHeight = Math.Max(1, height);
    }

    

    /// <summary>
    /// Calculates combined mesh bounds and adjusts target and distance while preserving the current viewing angle.
    /// </summary>
    public void FitToScene(SceneData scene)
    {
        if (scene == null ||
            scene.Meshes.Count == 0)
        {
            return;
        }

        bool hasPositions = false;

        Vector3 min = new Vector3(float.MaxValue);
        Vector3 max = new Vector3(float.MinValue);

        foreach (MeshData mesh in scene.Meshes)
        {
            if (mesh.Positions == null ||
                mesh.Positions.Length == 0)
            {
                continue;
            }

            for (int i = 0; i < mesh.Positions.Length; i++)
            {
                min = Vector3.Min(min, mesh.Positions[i]);
                max = Vector3.Max(max, mesh.Positions[i]);

                hasPositions = true;
            }
        }

        if (!hasPositions)
        {
            return;
        }

        FitToBounds(min, max);
    }

    /// <summary>
    /// Fits the current camera target and distance to an axis-aligned bounding box.
    /// </summary>
    public void FitToBounds(Vector3 min, Vector3 max)
    {
        _target = (min + max) * 0.5f;

        Vector3 size = max - min;

        float radius = size.Length() * 0.5f;

        if (radius < 0.001f)
        {
            radius = 1.0f;
        }

        float aspect =
            _viewportWidth /
            (float)_viewportHeight;

        const float verticalFieldOfView =
            MathF.PI / 4.0f;

        float horizontalFieldOfView =
            2.0f * MathF.Atan(
                MathF.Tan(verticalFieldOfView * 0.5f) *
                aspect);

        float limitingFieldOfView =
            MathF.Min(
                verticalFieldOfView,
                horizontalFieldOfView);

        float requiredDistance =
            radius /
            MathF.Sin(limitingFieldOfView * 0.5f);

        _distance = requiredDistance * 1.08f;

        _distance = Math.Clamp(
            _distance,
            0.05f,
            100000.0f);

        // Preserve yaw and pitch so the Fit command keeps the current viewing direction.
        // Only the orbit target and distance change.
    }

    /// <summary>
    /// Sets yaw and pitch directly. Exact top and bottom views are supported,
    /// so 90-degree Shift snapping can reach every primary camera direction.
    /// </summary>
    public void SetOrbitAngles(
        float yawRadians,
        float pitchRadians)
    {
        float pitchLimit =
            MathF.PI * 0.5f;

        _yaw =
            yawRadians;

        _pitch =
            Math.Clamp(
                pitchRadians,
                -pitchLimit,
                pitchLimit);

        _orbitRawYaw =
            _yaw;

        _orbitRawPitch =
            _pitch;
    }

    /// <summary>
    /// Returns normalized forward, right, and up vectors for the current orbit camera.
    /// </summary>
    public void GetViewBasis(
        out Vector3 forward,
        out Vector3 right,
        out Vector3 up)
    {
        Vector3 eye =
            GetEyePosition();

        forward =
            Vector3.Normalize(
                _target -
                eye);

        // Derive right from yaw instead of crossing with world-up. This
        // stays stable at exact top/bottom views where forward is parallel to Y.
        right =
            new Vector3(
                MathF.Cos(_yaw),
                0.0f,
                -MathF.Sin(_yaw));

        up =
            Vector3.Normalize(
                Vector3.Cross(
                    right,
                    forward));
    }

    /// <summary>
    /// Begins an orbit drag at the supplied mouse position.
    /// </summary>
    public void BeginOrbit(Point mousePosition)
    {
        _isOrbiting = true;
        _isPanning = false;
        _orbitRawYaw = _yaw;
        _orbitRawPitch = _pitch;
        _orbitSnapActive = false;
        _lastMouse = mousePosition;
    }

    /// <summary>
    /// Begins a screen-plane pan drag at the supplied mouse position.
    /// </summary>
    public void BeginPan(Point mousePosition)
    {
        _isPanning = true;
        _isOrbiting = false;
        _orbitSnapActive = false;
        _lastMouse = mousePosition;
    }

    /// <summary>
    /// Ends any active camera drag operation.
    /// </summary>
    public void EndInteraction()
    {
        _isOrbiting = false;
        _isPanning = false;
        _orbitSnapActive = false;
    }

    /// <summary>
    /// Applies an orbit or pan delta when a matching interaction is active.
    /// Shift snaps camera yaw and pitch to 90-degree increments.
    /// </summary>
    public void OnMouseMove(
        Point mousePosition,
        bool snapOrbitToRightAngles)
    {
        int dx =
            mousePosition.X -
            _lastMouse.X;

        int dy =
            mousePosition.Y -
            _lastMouse.Y;

        _lastMouse = mousePosition;

        if (_isOrbiting)
        {
            const float orbitSpeed = 0.01f;

            // Rebase the continuous drag whenever Shift is pressed or released.
            // That avoids a jump back to an old unsnapped angle.
            if (_orbitSnapActive !=
                snapOrbitToRightAngles)
            {
                _orbitRawYaw =
                    _yaw;

                _orbitRawPitch =
                    _pitch;

                _orbitSnapActive =
                    snapOrbitToRightAngles;
            }

            // Invert horizontal drag so the model appears to follow the mouse.
            _orbitRawYaw -=
                dx * orbitSpeed;

            _orbitRawPitch +=
                dy * orbitSpeed;

            float pitchLimit =
                MathF.PI * 0.5f;

            _orbitRawPitch =
                Math.Clamp(
                    _orbitRawPitch,
                    -pitchLimit,
                    pitchLimit);

            if (snapOrbitToRightAngles)
            {
                _yaw =
                    SnapToRightAngle(
                        _orbitRawYaw);

                _pitch =
                    Math.Clamp(
                        SnapToRightAngle(
                            _orbitRawPitch),
                        -pitchLimit,
                        pitchLimit);
            }
            else
            {
                _yaw =
                    _orbitRawYaw;

                _pitch =
                    _orbitRawPitch;
            }
        }
        else if (_isPanning)
        {
            GetViewBasis(
                out _,
                out Vector3 right,
                out Vector3 up);

            float panScale =
                _distance * 0.0025f;

            _target +=
                (-right * dx + up * dy) *
                panScale;
        }
    }

    /// <summary>
    /// Moves the camera closer or farther from its target using multiplicative zoom.
    /// </summary>
    public void OnMouseWheel(int delta)
    {
        if (delta > 0)
        {
            _distance *= 0.9f;
        }
        else if (delta < 0)
        {
            _distance *= 1.1f;
        }

        _distance = Math.Clamp(
            _distance,
            0.05f,
            100000.0f);
    }

    /// <summary>
    /// Builds the right-handed view-projection matrix used by the mesh shader.
    /// </summary>
    public Matrix4x4 GetViewProjectionMatrix()
    {
        float aspect =
            _viewportWidth /
            (float)_viewportHeight;

        GetViewBasis(
            out _,
            out _,
            out Vector3 cameraUp);

        Matrix4x4 view =
            Matrix4x4.CreateLookAt(
                GetEyePosition(),
                _target,
                cameraUp);

        Matrix4x4 projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 4.0f,
                aspect,
                0.01f,
                100000.0f);

        return view * projection;
    }
    private static float SnapToRightAngle(
        float angleRadians)
    {
        float rightAngle =
            MathF.PI * 0.5f;

        return
            MathF.Round(
                angleRadians /
                rightAngle,
                MidpointRounding.AwayFromZero) *
            rightAngle;
    }

    /// <summary>
    /// Returns the current world-space eye position.
    /// </summary>
    public Vector3 GetCameraPosition()
    {
        return GetEyePosition();
    }
    /// <summary>
    /// Converts orbit distance, yaw, and pitch into a world-space eye position.
    /// </summary>
    private Vector3 GetEyePosition()
    {
        float cosPitch =
            MathF.Cos(_pitch);

        float sinPitch =
            MathF.Sin(_pitch);

        float cosYaw =
            MathF.Cos(_yaw);

        float sinYaw =
            MathF.Sin(_yaw);

        return _target + new Vector3(
            sinYaw * cosPitch * _distance,
            sinPitch * _distance,
            cosYaw * cosPitch * _distance);
    }
}

