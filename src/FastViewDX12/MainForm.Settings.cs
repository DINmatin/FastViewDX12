using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace FastViewDX12;

// Persistent viewer preferences. Scene content is deliberately not stored here;
// this file only restores the way FastView itself looks and behaves.
public sealed partial class MainForm
{
    private const int ViewerSettingsVersion =
        3;

    private const int ViewerSettingsPollMilliseconds =
        250;

    private const int ViewerSettingsSaveDelayMilliseconds =
        600;

    private ViewerSettings _viewerSettings =
        new();

    private bool _viewerSettingsTracking;

    private bool _viewerSettingsDirty;

    private long _nextViewerSettingsPollTick;

    private long _viewerSettingsSaveDueTick;

    private string ViewerSettingsFile =>
        Path.Combine(
            _stateFolder,
            "settings.json");

    private sealed class ViewerSettings
    {
        public ViewerSettings()
        {
        }

        public int Version { get; set; } =
            ViewerSettingsVersion;

        public int BackgroundColorArgb { get; set; } =
            Color.FromArgb(
                20,
                20,
                26)
            .ToArgb();

        public ViewerBackgroundMode BackgroundMode { get; set; } =
            ViewerBackgroundMode.SolidColor;

        public string? EnvironmentMapPath { get; set; }

        public float EnvironmentBackgroundOpacity { get; set; } =
            1.0f;

        public bool EnvironmentLightingEnabled { get; set; } =
            true;

        public bool DirectLightEnabled { get; set; } =
            true;

        public float EnvironmentIntensity { get; set; } =
            1.0f;

        public float DirectLightIntensity { get; set; } =
            1.0f;

        public bool BloomEnabled { get; set; }

        public float BloomThreshold { get; set; } =
            0.78f;

        public float BloomIntensity { get; set; } =
            0.8f;

        public float BloomRadius { get; set; } =
            1.0f;

        public bool GridVisible { get; set; }

        public int TransformMode { get; set; }

        public int TransformOrientation { get; set; } =
            (int)TransformGizmoOrientation.Local;

        public bool HasCameraView { get; set; }

        public float CameraTargetX { get; set; }

        public float CameraTargetY { get; set; }

        public float CameraTargetZ { get; set; }

        public float CameraDistance { get; set; } =
            5.0f;

        public float CameraYawRadians { get; set; }

        public float CameraPitchRadians { get; set; }
    }

    /// <summary>
    /// Loads settings before controls and the renderer are created, then applies
    /// state that does not depend on a native Direct3D device.
    /// </summary>
    private void LoadViewerSettings()
    {
        try
        {
            if (File.Exists(
                    ViewerSettingsFile))
            {
                string json =
                    File.ReadAllText(
                        ViewerSettingsFile);

                ViewerSettings? loaded =
                    JsonSerializer.Deserialize<ViewerSettings>(
                        json);

                if (loaded != null)
                {
                    _viewerSettings =
                        loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Viewer settings could not be loaded: {ex}");

            _viewerSettings =
                new ViewerSettings();
        }

        NormalizeViewerSettings();
        ApplyViewerSettingsToEditorState();
    }

    private void NormalizeViewerSettings()
    {
        int loadedVersion =
            _viewerSettings.Version;

        // Version 2 changes the editor default from Global to Local. Existing
        // version-1 settings are migrated once; later user choices still persist.
        if (loadedVersion < 2)
        {
            _viewerSettings.TransformOrientation =
                (int)TransformGizmoOrientation.Local;
        }

        _viewerSettings.Version =
            ViewerSettingsVersion;

        if (!Enum.IsDefined(
                _viewerSettings.BackgroundMode))
        {
            _viewerSettings.BackgroundMode =
                ViewerBackgroundMode.SolidColor;
        }

        _viewerSettings.EnvironmentMapPath =
            string.IsNullOrWhiteSpace(
                _viewerSettings.EnvironmentMapPath)
                ? null
                : _viewerSettings.EnvironmentMapPath;

        _viewerSettings.EnvironmentBackgroundOpacity =
            ClampFinite(
                _viewerSettings.EnvironmentBackgroundOpacity,
                0.0f,
                1.0f,
                1.0f);

        _viewerSettings.EnvironmentIntensity =
            ClampFinite(
                _viewerSettings.EnvironmentIntensity,
                0.0f,
                3.0f,
                1.0f);

        _viewerSettings.DirectLightIntensity =
            ClampFinite(
                _viewerSettings.DirectLightIntensity,
                0.0f,
                3.0f,
                1.0f);

        _viewerSettings.BloomThreshold =
            ClampFinite(
                _viewerSettings.BloomThreshold,
                0.0f,
                1.0f,
                0.78f);

        _viewerSettings.BloomIntensity =
            ClampFinite(
                _viewerSettings.BloomIntensity,
                0.0f,
                3.0f,
                0.8f);

        _viewerSettings.BloomRadius =
            ClampFinite(
                _viewerSettings.BloomRadius,
                0.0f,
                4.0f,
                1.0f);

        _viewerSettings.TransformMode =
            Math.Clamp(
                _viewerSettings.TransformMode,
                (int)TransformGizmoMode.Move,
                (int)TransformGizmoMode.Scale);

        _viewerSettings.TransformOrientation =
            Math.Clamp(
                _viewerSettings.TransformOrientation,
                (int)TransformGizmoOrientation.Global,
                (int)TransformGizmoOrientation.Local);

        bool validCamera =
            float.IsFinite(
                _viewerSettings.CameraTargetX) &&
            float.IsFinite(
                _viewerSettings.CameraTargetY) &&
            float.IsFinite(
                _viewerSettings.CameraTargetZ) &&
            float.IsFinite(
                _viewerSettings.CameraDistance) &&
            float.IsFinite(
                _viewerSettings.CameraYawRadians) &&
            float.IsFinite(
                _viewerSettings.CameraPitchRadians);

        if (!validCamera)
        {
            _viewerSettings.HasCameraView =
                false;
        }

        _viewerSettings.CameraDistance =
            ClampFinite(
                _viewerSettings.CameraDistance,
                0.05f,
                100000.0f,
                5.0f);
    }

    private static float ClampFinite(
        float value,
        float minimum,
        float maximum,
        float fallback)
    {
        return float.IsFinite(value)
            ? Math.Clamp(
                value,
                minimum,
                maximum)
            : fallback;
    }

    /// <summary>
    /// Returns the saved solid background color for initial WinForms painting.
    /// </summary>
    private Color GetConfiguredBackgroundColor()
    {
        Color saved =
            Color.FromArgb(
                _viewerSettings.BackgroundColorArgb);

        return Color.FromArgb(
            saved.R,
            saved.G,
            saved.B);
    }

    private void ApplyViewerSettingsToEditorState()
    {
        _transformGizmoMode =
            (TransformGizmoMode)
            _viewerSettings.TransformMode;

        _transformGizmoOrientation =
            (TransformGizmoOrientation)
            _viewerSettings.TransformOrientation;

        _gridVisible =
            _viewerSettings.GridVisible;

        Vector4 gridColor =
            _gridMaterial.BaseColorFactor;

        _gridMaterial.BaseColorFactor =
            new Vector4(
                gridColor.X,
                gridColor.Y,
                gridColor.Z,
                _gridVisible
                    ? VisibleGridAlpha
                    : 0.0f);
    }

    /// <summary>
    /// Applies all renderer-owned visual settings after Direct3D initialization.
    /// </summary>
    private void ApplyViewerSettingsToRenderer()
    {
        Color backgroundColor =
            GetConfiguredBackgroundColor();

        _renderPanel.BackColor =
            backgroundColor;

        _renderer.SetBackgroundColor(
            backgroundColor);

        _renderer.SetBackgroundMode(
            _viewerSettings.BackgroundMode);

        _renderer.SetEnvironmentBackgroundOpacity(
            _viewerSettings.EnvironmentBackgroundOpacity);

        _renderer.SetEnvironmentLightingEnabled(
            _viewerSettings.EnvironmentLightingEnabled);

        _renderer.SetDirectLightEnabled(
            _viewerSettings.DirectLightEnabled);

        _renderer.SetEnvironmentIntensity(
            _viewerSettings.EnvironmentIntensity);

        _renderer.SetDirectLightIntensity(
            _viewerSettings.DirectLightIntensity);

        _renderer.SetBloomEnabled(
            _viewerSettings.BloomEnabled);

        _renderer.SetBloomThreshold(
            _viewerSettings.BloomThreshold);

        _renderer.SetBloomIntensity(
            _viewerSettings.BloomIntensity);

        _renderer.SetBloomRadius(
            _viewerSettings.BloomRadius);
    }

    /// <summary>
    /// Restores the camera only after startup model loading has finished, so an
    /// automatic FitToScene cannot immediately overwrite the saved view.
    /// </summary>
    private void ApplySavedCameraView()
    {
        if (!_viewerSettings.HasCameraView)
        {
            return;
        }

        _renderer.SetCameraViewState(
            new Vector3(
                _viewerSettings.CameraTargetX,
                _viewerSettings.CameraTargetY,
                _viewerSettings.CameraTargetZ),
            _viewerSettings.CameraDistance,
            _viewerSettings.CameraYawRadians,
            _viewerSettings.CameraPitchRadians);
    }

    private void BeginViewerSettingsTracking()
    {
        _viewerSettingsTracking =
            true;

        CaptureRuntimeViewerSettings();

        _viewerSettingsDirty =
            false;

        _nextViewerSettingsPollTick =
            Environment.TickCount64 +
            ViewerSettingsPollMilliseconds;
    }

    /// <summary>
    /// Polls only four times per second and saves after interaction has settled.
    /// This catches orbit, pan, zoom, view buttons, grid and gizmo mode changes
    /// without writing settings for every mouse-move message.
    /// </summary>
    private void PollViewerSettingsChanges()
    {
        if (!_viewerSettingsTracking)
        {
            return;
        }

        long now =
            Environment.TickCount64;

        if (now >=
            _nextViewerSettingsPollTick)
        {
            _nextViewerSettingsPollTick =
                now +
                ViewerSettingsPollMilliseconds;

            if (CaptureRuntimeViewerSettings())
            {
                ScheduleViewerSettingsSave();
            }
        }

        if (_viewerSettingsDirty &&
            now >=
            _viewerSettingsSaveDueTick)
        {
            SaveViewerSettingsNow();
        }
    }

    private bool CaptureRuntimeViewerSettings()
    {
        bool changed =
            false;

        int transformMode =
            (int)_transformGizmoMode;

        if (_viewerSettings.TransformMode !=
            transformMode)
        {
            _viewerSettings.TransformMode =
                transformMode;

            changed =
                true;
        }

        int transformOrientation =
            (int)_transformGizmoOrientation;

        if (_viewerSettings.TransformOrientation !=
            transformOrientation)
        {
            _viewerSettings.TransformOrientation =
                transformOrientation;

            changed =
                true;
        }

        if (_viewerSettings.GridVisible !=
            _gridVisible)
        {
            _viewerSettings.GridVisible =
                _gridVisible;

            changed =
                true;
        }

        _renderer.GetCameraViewState(
            out Vector3 cameraTarget,
            out float cameraDistance,
            out float cameraYaw,
            out float cameraPitch);

        if (!_viewerSettings.HasCameraView ||
            !NearlyEqual(
                _viewerSettings.CameraTargetX,
                cameraTarget.X) ||
            !NearlyEqual(
                _viewerSettings.CameraTargetY,
                cameraTarget.Y) ||
            !NearlyEqual(
                _viewerSettings.CameraTargetZ,
                cameraTarget.Z) ||
            !NearlyEqual(
                _viewerSettings.CameraDistance,
                cameraDistance) ||
            !NearlyEqual(
                _viewerSettings.CameraYawRadians,
                cameraYaw) ||
            !NearlyEqual(
                _viewerSettings.CameraPitchRadians,
                cameraPitch))
        {
            _viewerSettings.HasCameraView =
                true;

            _viewerSettings.CameraTargetX =
                cameraTarget.X;

            _viewerSettings.CameraTargetY =
                cameraTarget.Y;

            _viewerSettings.CameraTargetZ =
                cameraTarget.Z;

            _viewerSettings.CameraDistance =
                cameraDistance;

            _viewerSettings.CameraYawRadians =
                cameraYaw;

            _viewerSettings.CameraPitchRadians =
                cameraPitch;

            changed =
                true;
        }

        return changed;
    }

    private static bool NearlyEqual(
        float first,
        float second)
    {
        float scale =
            MathF.Max(
                1.0f,
                MathF.Max(
                    MathF.Abs(first),
                    MathF.Abs(second)));

        return MathF.Abs(
            first -
            second) <=
            scale *
            0.00001f;
    }

    private void ScheduleViewerSettingsSave()
    {
        _viewerSettingsDirty =
            true;

        _viewerSettingsSaveDueTick =
            Environment.TickCount64 +
            ViewerSettingsSaveDelayMilliseconds;
    }

    private void SaveViewerSettingsNow()
    {
        if (_viewerSettingsTracking)
        {
            CaptureRuntimeViewerSettings();
        }

        try
        {
            Directory.CreateDirectory(
                _stateFolder);

            _viewerSettings.Version =
                ViewerSettingsVersion;

            string json =
                JsonSerializer.Serialize(
                    _viewerSettings,
                    new JsonSerializerOptions
                    {
                        WriteIndented =
                            true
                    });

            string temporaryPath =
                ViewerSettingsFile +
                ".tmp";

            File.WriteAllText(
                temporaryPath,
                json);

            File.Move(
                temporaryPath,
                ViewerSettingsFile,
                overwrite: true);

            _viewerSettingsDirty =
                false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Viewer settings could not be saved: {ex}");
        }
    }
}
