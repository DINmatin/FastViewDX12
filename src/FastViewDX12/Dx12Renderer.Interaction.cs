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
    /// Forwards mouse movement to the active camera interaction.
    /// </summary>
    public void OnCameraMouseMove(
        System.Drawing.Point mousePosition)
    {
        _camera.OnMouseMove(
            mousePosition);
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

}
