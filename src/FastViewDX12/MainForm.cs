using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FastViewDX12;

/// <summary>
/// Interactive WinForms shell for FastView. Rendering remains in <see cref="Dx12Renderer"/>.
/// </summary>
public sealed partial class MainForm : Form
{
    private readonly Panel _renderPanel;

    private readonly MenuStrip _menuStrip;

    private readonly string? _startupModelPath;

    private readonly Dx12Renderer _renderer;

    private readonly SceneDocument _sceneDocument = new();

    private bool _isRotatingLight;

    private readonly string _stateFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastViewDX12");

    private string LastModelPathFile => Path.Combine(_stateFolder, "lastModel.txt");

    private static string DefaultEnvironmentMapPath =>
    Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Environment",
        "default.exr");

    /// <summary>
    /// Creates the interactive FastView window, render surface, menus, input routing, and idle render loop.
    /// </summary>
    /// <param name="startupModelPath">Optional model path supplied on the command line.</param>
    public MainForm(string? startupModelPath = null)
    {
        LoadViewerSettings();

        Text = "FastViewDX12";
        ClientSize = new Size(1600, 900);
        StartPosition = FormStartPosition.CenterScreen;
        _startupModelPath = startupModelPath;
        Icon? executableIcon =
    Icon.ExtractAssociatedIcon(
        Application.ExecutablePath);

        if (executableIcon != null)
        {
            Icon =
                executableIcon;
        }

        ShowIcon =
            true;

        _renderPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor =
                GetConfiguredBackgroundColor(),

            AllowDrop = true
        };



        _renderer = new Dx12Renderer(_renderPanel);

        _menuStrip =
            CreateMainMenu();

        InitializeSceneSidebar();

        var contentPanel =
            new Panel
            {
                Dock =
                    DockStyle.Fill
            };

        contentPanel.Controls.Add(
            _renderPanel);

        contentPanel.Controls.Add(
            _sceneSidebar);

        _renderPanel.Controls.Add(
            _sceneSidebarToggleButton);

        Controls.Add(
            contentPanel);

        Controls.Add(
            _menuStrip);

        MainMenuStrip =
            _menuStrip;

        _renderPanel.HandleCreated +=
            (_, _) =>
            {
                _renderer.Initialize();
                TryLoadConfiguredEnvironmentMap();
                ApplyViewerSettingsToRenderer();
                TryLoadStartupModel();
                ApplySavedCameraView();
                BeginViewerSettingsTracking();
            };
        _renderPanel.Resize += (_, _) =>
        {
            _renderer.Resize();
            PositionSceneSidebarToggle();
        };

        // Layout is raised during the initial form arrangement as well as when the
        // sidebar changes the available render width. This keeps the overlay button
        // visible before the user manually resizes the window.
        _renderPanel.Layout +=
            (_, _) =>
            {
                PositionSceneSidebarToggle();
            };
        _renderPanel.DragEnter += RenderPanel_DragEnter;
        _renderPanel.DragDrop += RenderPanel_DragDrop;

        _renderPanel.MouseDown += RenderPanel_MouseDown;
        _renderPanel.MouseMove += RenderPanel_MouseMove;
        _renderPanel.MouseUp += RenderPanel_MouseUp;
        _renderPanel.MouseWheel += RenderPanel_MouseWheel;
        _renderPanel.MouseLeave += RenderPanel_MouseLeave;

        KeyPreview = true; KeyDown += MainForm_KeyDown;

        Shown +=
            (_, _) =>
            {
                PositionSceneSidebarToggle();
                _sceneSidebarToggleButton.BringToFront();
            };

        Application.Idle += OnApplicationIdle;
    }

    /// <summary>
    /// Loads the configured EXR environment map when available, otherwise the
    /// bundled default map. The renderer fallback remains active if both fail.
    /// </summary>
    private void TryLoadConfiguredEnvironmentMap()
    {
        string? configuredPath =
            _viewerSettings.EnvironmentMapPath;

        string path =
            !string.IsNullOrWhiteSpace(configuredPath) &&
            File.Exists(configuredPath)
                ? configuredPath
                : DefaultEnvironmentMapPath;

        if (!File.Exists(path))
        {
            Debug.WriteLine(
                $"Environment map not found: {path}");

            return;
        }

        try
        {
            _renderer.LoadEnvironmentMap(path);

            Debug.WriteLine(
                $"Environment map loaded: {path}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Could not load environment map: {ex}");
        }
    }

    /// <summary>
    /// Renders continuously while the Windows message queue is empty.
    /// </summary>
    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        while (AppStillIdle)
        {
            _renderer.Render();
            UpdateMoveGizmoOverlay();
            PollViewerSettingsChanges();
        }
    }

    private static bool AppStillIdle
    {
        get
        {
            NativeMethods.PeekMessage(out var msg, IntPtr.Zero, 0, 0, 0);
            return msg.message == 0;
        }
    }

    /// <summary>
    /// Stops idle rendering and disposes the Direct3D renderer and WinForms components.
    /// </summary>
    protected override void Dispose(
    bool disposing)
    {
        if (disposing)
        {
            SaveViewerSettingsNow();
            DisposeMoveGizmoOverlay();

            Application.Idle -=
                OnApplicationIdle;

            _renderer.Dispose();
        }

        base.Dispose(
            disposing);
    }

}
