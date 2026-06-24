using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FastViewDX12;

/// <summary>
/// Minimal off-screen WinForms host used only to provide a valid native window and message loop for thumbnail rendering.
/// </summary>
public sealed class ThumbnailRenderForm : Form
{
    private readonly Panel _renderPanel;
    private readonly Dx12Renderer _renderer;

    private readonly string _inputPath;
    private readonly string _outputPath;

    private readonly int _outputWidth;
    private readonly int _outputHeight;

    private bool _renderStarted;

    private static string DefaultEnvironmentMapPath =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Environment",
            "default.exr");

    /// <summary>
    /// Creates the hidden render surface and configures the renderer for transparent three-quarter previews.
    /// </summary>
    public ThumbnailRenderForm(
        string inputPath,
        string outputPath,
        int outputWidth,
        int outputHeight)
    {
        _inputPath =
            inputPath;

        _outputPath =
            outputPath;

        _outputWidth =
            outputWidth;

        _outputHeight =
            outputHeight;

        float renderScale =
            Math.Clamp(
                1024.0f /
                Math.Max(
                    outputWidth,
                    outputHeight),
                1.0f,
                2.0f);

        int renderWidth =
            Math.Clamp(
                (int)MathF.Round(
                    outputWidth *
                    renderScale),
                64,
                2048);

        int renderHeight =
            Math.Clamp(
                (int)MathF.Round(
                    outputHeight *
                    renderScale),
                64,
                2048);

        Text =
            "FastView Thumbnail Renderer";

        AutoScaleMode =
            AutoScaleMode.None;

        ClientSize =
            new Size(
                renderWidth,
                renderHeight);

        FormBorderStyle =
            FormBorderStyle.None;

        ShowInTaskbar =
            false;

        StartPosition =
            FormStartPosition.Manual;

        Location =
            new Point(
                SystemInformation.VirtualScreen.Left -
                renderWidth -
                200,

                SystemInformation.VirtualScreen.Top -
                renderHeight -
                200);

        _renderPanel =
            new Panel
            {
                Dock =
                    DockStyle.Fill,

                BackColor =
                    Color.Black
            };

        Controls.Add(
            _renderPanel);

        _renderer =
            new Dx12Renderer(
                _renderPanel);

        _renderPanel.HandleCreated +=
            (_, _) =>
            {
                _renderer.Initialize();
            };
    }

    protected override bool ShowWithoutActivation =>
        true;

    /// <summary>
    /// Defers rendering until WinForms has created native handles for the form and render panel.
    /// </summary>
    protected override void OnShown(
        EventArgs e)
    {
        base.OnShown(
            e);

        if (_renderStarted)
        {
            return;
        }

        _renderStarted =
            true;

        BeginInvoke(
            new Action(
                RenderThumbnailAndClose));
    }

    /// <summary>
    /// Initializes Direct3D, loads the model, fits and rotates the camera, writes the PNG, and closes the message loop.
    /// </summary>
    private void RenderThumbnailAndClose()
    {
        try
        {
            _renderer.Initialize();

            if (File.Exists(
                DefaultEnvironmentMapPath))
            {
                _renderer.LoadEnvironmentMap(
                    DefaultEnvironmentMapPath);
            }

            SceneData scene =
                GltfSceneLoader.LoadFromFile(
                    _inputPath);

            _renderer.LoadScene(
                scene);

            // Thumbnail previews use a transparent background and a
            // three-quarter view instead of the often uninformative
            // straight-on front view.
            _renderer.SetTransparentBackground(
                true);

            _renderer.SetCameraOrbitDegrees(
                45.0f);

            _renderer.FitCameraToScene();

            _renderer.SavePreviewPng(
                _outputPath,
                _outputWidth,
                _outputHeight);

            DeleteOldErrorLog();

            Environment.ExitCode =
                0;

            Debug.WriteLine(
                $"Thumbnail created: {_outputPath}");
        }
        catch (Exception ex)
        {
            Environment.ExitCode =
                1;

            Debug.WriteLine(
                $"Thumbnail creation failed: {ex}");

            TryWriteErrorLog(
                ex);
        }
        finally
        {
            Close();
        }
    }

    /// <summary>
    /// Removes a stale error file before beginning a new thumbnail attempt.
    /// </summary>
    private void DeleteOldErrorLog()
    {
        string errorPath =
            _outputPath +
            ".error.txt";

        try
        {
            if (File.Exists(
                errorPath))
            {
                File.Delete(
                    errorPath);
            }
        }
        catch
        {
            // A stale error file is non-critical; the next write can still replace it.
        }
    }

    /// <summary>
    /// Writes a best-effort error report next to the requested PNG.
    /// </summary>
    private void TryWriteErrorLog(
        Exception exception)
    {
        try
        {
            string? directory =
                Path.GetDirectoryName(
                    _outputPath);

            if (!string.IsNullOrWhiteSpace(
                directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            File.WriteAllText(
                _outputPath +
                ".error.txt",
                exception.ToString());
        }
        catch
        {
            // Diagnostic logging must never crash the thumbnail process
            // while it is already handling another failure.
        }
    }

    /// <summary>
    /// Disposes renderer resources and owned controls.
    /// </summary>
    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            _renderer.Dispose();
        }

        base.Dispose(
            disposing);
    }
}