namespace FastViewDX12;

/// <summary>
/// Camera and light-controller API exposed to the WinForms front end.
/// </summary>
public sealed partial class Dx12Renderer
{
    /// <summary>
    /// Fits the orbit camera to the bounds of every loaded mesh.
    /// </summary>
    public void FitCameraToScene()
    {
        if (_scene != null &&
            _scene.Meshes.Count > 0)
        {
            _camera.FitToScene(
                _scene);
        }
    }

    /// <summary>
    /// Fits the orbit camera to one world-space axis-aligned bounding box.
    /// </summary>
    public void FitCameraToBounds(
        System.Numerics.Vector3 min,
        System.Numerics.Vector3 max)
    {
        _camera.FitToBounds(
            min,
            max);
    }

    /// <summary>
    /// Creates a world-space picking ray through one render-control pixel.
    /// Direct3D normalized depth uses zero at the near plane and one at the far plane.
    /// </summary>
    public bool TryCreateWorldRay(
        System.Drawing.Point screenPosition,
        out System.Numerics.Vector3 rayOrigin,
        out System.Numerics.Vector3 rayDirection)
    {
        rayOrigin =
            default;

        rayDirection =
            default;

        int width =
            _host.ClientSize.Width;

        int height =
            _host.ClientSize.Height;

        if (width <= 0 ||
            height <= 0)
        {
            return false;
        }

        float normalizedX =
            screenPosition.X /
            (float)width *
            2.0f -
            1.0f;

        float normalizedY =
            1.0f -
            screenPosition.Y /
            (float)height *
            2.0f;

        if (!System.Numerics.Matrix4x4.Invert(
                _camera.GetViewProjectionMatrix(),
                out System.Numerics.Matrix4x4 inverseViewProjection))
        {
            return false;
        }

        System.Numerics.Vector4 nearClip =
            new(
                normalizedX,
                normalizedY,
                0.0f,
                1.0f);

        System.Numerics.Vector4 farClip =
            new(
                normalizedX,
                normalizedY,
                1.0f,
                1.0f);

        System.Numerics.Vector4 nearWorld =
            System.Numerics.Vector4.Transform(
                nearClip,
                inverseViewProjection);

        System.Numerics.Vector4 farWorld =
            System.Numerics.Vector4.Transform(
                farClip,
                inverseViewProjection);

        if (MathF.Abs(
                nearWorld.W) <= 0.000001f ||
            MathF.Abs(
                farWorld.W) <= 0.000001f)
        {
            return false;
        }

        nearWorld /=
            nearWorld.W;

        farWorld /=
            farWorld.W;

        rayOrigin =
            _camera.GetCameraPosition();

        System.Numerics.Vector3 farPoint =
            new(
                farWorld.X,
                farWorld.Y,
                farWorld.Z);

        System.Numerics.Vector3 direction =
            farPoint -
            rayOrigin;

        if (direction.LengthSquared() <=
            0.000000001f)
        {
            return false;
        }

        rayDirection =
            System.Numerics.Vector3.Normalize(
                direction);

        return true;
    }

    /// <summary>
    /// Sets the camera yaw and pitch without changing the fitted target or distance.
    /// </summary>
    /// <param name="yawDegrees">Horizontal orbit angle in degrees.</param>
    /// <param name="pitchDegrees">Vertical orbit angle in degrees.</param>
    public void SetCameraOrbitDegrees(
        float yawDegrees,
        float pitchDegrees = 0.0f)
    {
        const float degreesToRadians =
            MathF.PI / 180.0f;

        _camera.SetOrbitAngles(
            yawDegrees *
            degreesToRadians,
            pitchDegrees *
            degreesToRadians);
    }

    /// <summary>
    /// Starts orbit-camera interaction at a mouse position.
    /// </summary>
    public void BeginOrbit(
        System.Drawing.Point mousePosition)
    {
        _camera.BeginOrbit(
            mousePosition);
    }

    /// <summary>
    /// Starts camera panning at a mouse position.
    /// </summary>
    public void BeginPan(
        System.Drawing.Point mousePosition)
    {
        _camera.BeginPan(
            mousePosition);
    }

    /// <summary>
    /// Stops orbiting or panning.
    /// </summary>
    public void EndCameraInteraction()
    {
        _camera.EndInteraction();
    }

    /// <summary>
    /// Forwards mouse movement to the active camera interaction. Holding Shift
    /// snaps orbit yaw and pitch to 90-degree increments.
    /// </summary>
    public void OnCameraMouseMove(
        System.Drawing.Point mousePosition,
        bool snapOrbitToRightAngles)
    {
        _camera.OnMouseMove(
            mousePosition,
            snapOrbitToRightAngles);
    }

    /// <summary>
    /// Forwards wheel input to camera zoom.
    /// </summary>
    public void OnCameraMouseWheel(
        int delta)
    {
        _camera.OnMouseWheel(
            delta);
    }

    /// <summary>
    /// Starts direct-light and environment rotation.
    /// </summary>
    public void BeginLightRotation(
    System.Drawing.Point mousePosition)
    {
        _light.BeginRotate(mousePosition);
    }

    /// <summary>
    /// Stops light rotation.
    /// </summary>
    public void EndLightRotation()
    {
        _light.EndRotate();
    }

    /// <summary>
    /// Forwards mouse movement to the light controller.
    /// </summary>
    public void OnLightMouseMove(
        System.Drawing.Point mousePosition)
    {
        _light.OnMouseMove(mousePosition);
    }

    /// <summary>
    /// Projects one world-space point into the render control's client area.
    /// Editor overlays use this without becoming part of the Direct3D scene or
    /// thumbnail output.
    /// </summary>
    public bool TryProjectWorldToScreen(
        System.Numerics.Vector3 worldPosition,
        out System.Drawing.PointF screenPosition)
    {
        screenPosition =
            default;

        int width =
            _host.ClientSize.Width;

        int height =
            _host.ClientSize.Height;

        if (width <= 0 ||
            height <= 0)
        {
            return false;
        }

        System.Numerics.Vector4 clip =
            System.Numerics.Vector4.Transform(
                new System.Numerics.Vector4(
                    worldPosition,
                    1.0f),
                _camera.GetViewProjectionMatrix());

        if (clip.W <=
            0.0001f)
        {
            return false;
        }

        float inverseW =
            1.0f /
            clip.W;

        float normalizedX =
            clip.X *
            inverseW;

        float normalizedY =
            clip.Y *
            inverseW;

        screenPosition =
            new System.Drawing.PointF(
                (normalizedX * 0.5f + 0.5f) *
                width,
                (-normalizedY * 0.5f + 0.5f) *
                height);

        return true;
    }

}
