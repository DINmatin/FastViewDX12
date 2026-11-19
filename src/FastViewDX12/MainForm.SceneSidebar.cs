using System;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;

namespace FastViewDX12;

// Optional scene-management panel. It starts collapsed so FastView still opens as a viewer.
public sealed partial class MainForm
{
    private const int SceneSidebarWidth = 360;

    private Panel _sceneSidebar = null!;

    private Button _sceneSidebarToggleButton = null!;

    private FlowLayoutPanel _sceneModelList = null!;

    private Button _removeSceneModelButton = null!;

    private bool _updatingTransformInspector;

    private bool _transformInspectorExpanded =
        true;

    private bool _viewInspectorExpanded =
        true;

    private bool _renderingInspectorExpanded;

    private bool _renderingDetailsInspectorExpanded;

    private Button? _moveInspectorButton;
    private Button? _rotateInspectorButton;
    private Button? _scaleInspectorButton;
    private Button? _localInspectorButton;
    private Button? _globalInspectorButton;

    private CheckBox? _gridInspectorToggle;
    private CheckBox? _statisticsInspectorToggle;
    private CheckBox? _spaceMouseInspectorToggle;
    private CheckBox? _environmentInspectorToggle;
    private CheckBox? _directLightInspectorToggle;
    private CheckBox? _shadowsInspectorToggle;
    private CheckBox? _bloomInspectorToggle;

    private enum TransformVectorKind
    {
        Position,
        Rotation,
        Scale
    }

    /// <summary>
    /// Creates the hidden scene sidebar and the small overlay button used to reveal it.
    /// </summary>
    private void InitializeSceneSidebar()
    {
        // Keep application shortcuts such as Ctrl+Z available even while a
        // NumericUpDown editor or toolbar button owns keyboard focus.
        KeyPreview =
            true;

        _sceneModelList =
            new FlowLayoutPanel
            {
                Dock =
                    DockStyle.Fill,

                AutoScroll =
                    true,

                BackColor =
                    Color.FromArgb(
                        34,
                        34,
                        40),

                FlowDirection =
                    FlowDirection.TopDown,

                Padding =
                    new Padding(
                        0,
                        4,
                        0,
                        4),

                WrapContents =
                    false
            };

        _sceneModelList.Resize +=
            (_, _) =>
            {
                UpdateSceneModelPanelWidths();
            };

        var addModelButton =
            CreateSceneSidebarButton(
                "Add model...");

        addModelButton.Click +=
            (_, _) =>
            {
                ChooseModelFile(
                    addToScene: true);
            };

        _removeSceneModelButton =
            CreateSceneSidebarButton(
                "Remove");

        _removeSceneModelButton.Click +=
            (_, _) =>
            {
                RemoveSelectedSceneModel();
            };

        var commandPanel =
            new FlowLayoutPanel
            {
                Dock =
                    DockStyle.Bottom,

                Height =
                    46,

                Padding =
                    new Padding(
                        8,
                        7,
                        8,
                        7),

                FlowDirection =
                    FlowDirection.LeftToRight,

                WrapContents =
                    false,

                BackColor =
                    Color.FromArgb(
                        28,
                        28,
                        34)
            };

        commandPanel.Controls.Add(
            addModelButton);

        commandPanel.Controls.Add(
            _removeSceneModelButton);

        var titleLabel =
            new Label
            {
                Dock =
                    DockStyle.Fill,

                Padding =
                    new Padding(
                        10,
                        12,
                        0,
                        0),

                Text =
                    "Inspector",

                ForeColor =
                    Color.White,

                BackColor =
                    Color.FromArgb(
                        28,
                        28,
                        34),

                Font =
                    new Font(
                        SystemFonts.MessageBoxFont ??
                        Control.DefaultFont,
                        FontStyle.Bold)
            };

        var closeInspectorButton =
            new Button
            {
                Dock =
                    DockStyle.Right,

                Width =
                    42,

                Text =
                    "×",

                FlatStyle =
                    FlatStyle.Flat,

                BackColor =
                    Color.FromArgb(
                        28,
                        28,
                        34),

                ForeColor =
                    Color.White,

                TabStop =
                    false,

                AccessibleName =
                    "Hide inspector"
            };

        closeInspectorButton.FlatAppearance.BorderSize =
            0;

        closeInspectorButton.Click +=
            (_, _) =>
            {
                ToggleSceneSidebar();
            };

        var titlePanel =
            new Panel
            {
                Dock =
                    DockStyle.Top,

                Height =
                    42,

                BackColor =
                    Color.FromArgb(
                        28,
                        28,
                        34)
            };

        titlePanel.Controls.Add(
            titleLabel);

        titlePanel.Controls.Add(
            closeInspectorButton);

        _sceneSidebar =
            new Panel
            {
                Dock =
                    DockStyle.Right,

                Width =
                    SceneSidebarWidth,

                Visible =
                    false,

                BackColor =
                    Color.FromArgb(
                        34,
                        34,
                        40),

                Padding =
                    new Padding(
                        1)
            };

        _sceneSidebar.Controls.Add(
            _sceneModelList);

        _sceneSidebar.Controls.Add(
            commandPanel);

        _sceneSidebar.Controls.Add(
            titlePanel);

        _sceneSidebarToggleButton =
            new Button
            {
                Text =
                    "Inspector",

                Size =
                    new Size(
                        82,
                        30),

                FlatStyle =
                    FlatStyle.Flat,

                BackColor =
                    Color.FromArgb(
                        46,
                        46,
                        54),

                ForeColor =
                    Color.White,

                TabStop =
                    false,

                Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Right,

                AccessibleName =
                    "Show inspector"
            };

        _sceneSidebarToggleButton.FlatAppearance.BorderSize =
            0;

        _sceneSidebarToggleButton.Click +=
            (_, _) =>
            {
                ToggleSceneSidebar();
            };

        UpdateSceneSidebarCommands();
    }

    /// <summary>
    /// Shows or hides scene-management controls without changing the default viewer-first layout.
    /// </summary>
    private void ToggleSceneSidebar()
    {
        bool showSidebar =
            !_sceneSidebar.Visible;

        _sceneSidebar.Visible =
            showSidebar;

        _sceneSidebarToggleButton.Visible =
            !showSidebar;

        if (showSidebar)
        {
            RefreshSceneSidebar();
        }
        else
        {
            ClearMoveGizmoHover();
        }

        if (!showSidebar)
        {
            PositionSceneSidebarToggle();
            _sceneSidebarToggleButton.BringToFront();
        }

    }

    /// <summary>
    /// Keeps the overlay toggle in the upper-right corner of the actual render surface.
    /// </summary>
    private void PositionSceneSidebarToggle()
    {
        int x =
            Math.Max(
                8,
                _renderPanel.ClientSize.Width -
                _sceneSidebarToggleButton.Width -
                12);

        _sceneSidebarToggleButton.Location =
            new Point(
                x,
                18);

        PositionTransformToolbar();
    }

    /// <summary>
    /// Rebuilds the compact model list. Only the selected model exposes its Transform inspector.
    /// </summary>
    private void RefreshSceneSidebar()
    {
        _updatingTransformInspector =
            true;

        _sceneModelList.SuspendLayout();

        try
        {
            while (_sceneModelList.Controls.Count > 0)
            {
                Control control =
                    _sceneModelList.Controls[0];

                _sceneModelList.Controls.RemoveAt(
                    0);

                control.Dispose();
            }

            foreach (SceneModel model in
                     _sceneDocument.Models)
            {
                _sceneModelList.Controls.Add(
                    CreateSceneModelPanel(
                        model));
            }

            int contentWidth =
                GetSceneModelContentWidth();

            _sceneModelList.Controls.Add(
                CreateViewInspector(
                    contentWidth));

            _sceneModelList.Controls.Add(
                CreateRenderingInspector(
                    contentWidth));
        }
        finally
        {
            _sceneModelList.ResumeLayout(
                performLayout: true);

            _updatingTransformInspector =
                false;
        }

        UpdateSceneModelPanelWidths();
        UpdateSceneSidebarCommands();
        UpdateInspectorControlState();
    }

    /// <summary>
    /// Creates one model foldout. The selected model contains a Unity-style Transform section.
    /// </summary>
    private Control CreateSceneModelPanel(
        SceneModel model)
    {
        bool fileExpanded =
            ReferenceEquals(
                model,
                _sceneDocument.SelectedModel);

        bool rootSelected =
            fileExpanded &&
            _sceneDocument.SelectedPart == null;

        int contentWidth =
            GetSceneModelContentWidth();

        var modelPanel =
            new FlowLayoutPanel
            {
                AutoSize =
                    true,

                AutoSizeMode =
                    AutoSizeMode.GrowAndShrink,

                BackColor =
                    fileExpanded
                        ? Color.FromArgb(
                            43,
                            43,
                            51)
                        : Color.FromArgb(
                            34,
                            34,
                            40),

                FlowDirection =
                    FlowDirection.TopDown,

                Margin =
                    new Padding(
                        0),

                Padding =
                    new Padding(
                        0),

                Tag =
                    model,

                Width =
                    contentWidth,

                WrapContents =
                    false
            };

        var modelHeader =
            new Button
            {
                Text =
                    fileExpanded
                        ? $"▼  {model.Name}"
                        : $"▶  {model.Name}",

                TextAlign =
                    ContentAlignment.MiddleLeft,

                FlatStyle =
                    FlatStyle.Flat,

                BackColor =
                    rootSelected
                        ? Color.FromArgb(
                            55,
                            55,
                            65)
                        : Color.FromArgb(
                            40,
                            40,
                            47),

                ForeColor =
                    Color.White,

                Height =
                    30,

                Margin =
                    new Padding(
                        0),

                Padding =
                    new Padding(
                        4,
                        0,
                        0,
                        0),

                TabStop =
                    false,

                Width =
                    contentWidth
            };

        modelHeader.FlatAppearance.BorderSize =
            0;

        modelHeader.Click +=
            (_, _) =>
            {
                _sceneDocument.Select(
                    model);

                RefreshSceneSidebar();
            };

        modelPanel.Controls.Add(
            modelHeader);

        if (fileExpanded)
        {
            if (rootSelected)
            {
                modelPanel.Controls.Add(
                    CreateTransformInspector(
                        model,
                        contentWidth));

                if (model.HasAnimations)
                {
                    modelPanel.Controls.Add(
                        CreateAnimationInspector(
                            model,
                            contentWidth));
                }
            }

            foreach (SceneMeshPart part in model.Parts)
            {
                bool partSelected =
                    ReferenceEquals(
                        part,
                        _sceneDocument.SelectedPart);

                var partButton =
                    new Button
                    {
                        BackColor = partSelected
                            ? Color.FromArgb(58, 58, 69)
                            : Color.FromArgb(38, 38, 45),
                        FlatStyle = FlatStyle.Flat,
                        ForeColor = partSelected
                            ? Color.FromArgb(255, 224, 126)
                            : Color.Gainsboro,
                        Height = 27,
                        Margin = Padding.Empty,
                        Padding = new Padding(25, 0, 4, 0),
                        TabStop = false,
                        Text = $"◇  {part.Name}",
                        TextAlign = ContentAlignment.MiddleLeft,
                        Width = contentWidth
                    };

                partButton.FlatAppearance.BorderSize = 0;
                partButton.Click +=
                    (_, _) =>
                    {
                        _sceneDocument.SelectPart(model, part);
                        RefreshSceneSidebar();
                        UpdateMoveGizmoOverlay();
                    };

                modelPanel.Controls.Add(partButton);

                if (partSelected)
                {
                    modelPanel.Controls.Add(
                        CreateTransformInspector(
                            part,
                            contentWidth));
                }
            }
        }

        return modelPanel;
    }

    /// <summary>
    /// Creates the Transform foldout shown beneath the selected model.
    /// </summary>
    private Control CreateTransformInspector(
        ISceneTransformTarget model,
        int width)
    {
        var transformPanel =
            new FlowLayoutPanel
            {
                AutoSize =
                    true,

                AutoSizeMode =
                    AutoSizeMode.GrowAndShrink,

                BackColor =
                    Color.FromArgb(
                        46,
                        46,
                        54),

                FlowDirection =
                    FlowDirection.TopDown,

                Margin =
                    new Padding(
                        0),

                Padding =
                    new Padding(
                        0,
                        0,
                        0,
                        4),

                Width =
                    width,

                WrapContents =
                    false
            };

        var transformHeader =
            new Panel
            {
                BackColor =
                    Color.FromArgb(
                        49,
                        49,
                        57),

                Height =
                    29,

                Margin =
                    new Padding(
                        0),

                Width =
                    width
            };

        var transformToggle =
            new Button
            {
                Text =
                    _transformInspectorExpanded
                        ? "▼  Transform"
                        : "▶  Transform",

                TextAlign =
                    ContentAlignment.MiddleLeft,

                FlatStyle =
                    FlatStyle.Flat,

                BackColor =
                    transformHeader.BackColor,

                ForeColor =
                    Color.Gainsboro,

                Dock =
                    DockStyle.Fill,

                Padding =
                    new Padding(
                        16,
                        0,
                        0,
                        0),

                TabStop =
                    false
            };

        transformToggle.FlatAppearance.BorderSize =
            0;

        transformToggle.Click +=
            (_, _) =>
            {
                _transformInspectorExpanded =
                    !_transformInspectorExpanded;

                RefreshSceneSidebar();
            };

        var resetButton =
            new Button
            {
                Text =
                    "↺",

                AccessibleName =
                    "Reset transform",

                Dock =
                    DockStyle.Right,

                Width =
                    32,

                FlatStyle =
                    FlatStyle.Flat,

                BackColor =
                    transformHeader.BackColor,

                ForeColor =
                    Color.Gainsboro,

                TabStop =
                    false
            };

        resetButton.FlatAppearance.BorderSize =
            0;

        resetButton.Click +=
            (_, _) =>
            {
                BeginTransformUndo(
                    model,
                    "Reset transform");

                model.Position =
                    Vector3.Zero;

                model.RotationDegrees =
                    Vector3.Zero;

                model.Scale =
                    Vector3.One;

                ApplySceneModelTransform();
                CommitTransformUndo();
                RefreshSceneSidebar();
            };

        transformHeader.Controls.Add(
            transformToggle);

        transformHeader.Controls.Add(
            resetButton);

        resetButton.BringToFront();

        transformPanel.Controls.Add(
            transformHeader);

        if (_transformInspectorExpanded)
        {
            transformPanel.Controls.Add(
                CreateTransformModeControls(
                    width));

            transformPanel.Controls.Add(
                CreateTransformVectorRow(
                    "Position",
                    model,
                    TransformVectorKind.Position,
                    width));

            transformPanel.Controls.Add(
                CreateTransformVectorRow(
                    "Rotation",
                    model,
                    TransformVectorKind.Rotation,
                    width));

            transformPanel.Controls.Add(
                CreateTransformVectorRow(
                    "Scale",
                    model,
                    TransformVectorKind.Scale,
                    width));
        }

        return transformPanel;
    }

    private Control CreateTransformVectorRow(
        string labelText,
        ISceneTransformTarget model,
        TransformVectorKind kind,
        int width)
    {
        var row =
            new TableLayoutPanel
            {
                BackColor =
                    Color.FromArgb(
                        46,
                        46,
                        54),

                ColumnCount =
                    8,

                Height =
                    29,

                Margin =
                    new Padding(
                        0),

                Padding =
                    new Padding(
                        7,
                        2,
                        7,
                        2),

                RowCount =
                    1,

                Width =
                    width
            };

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                58));

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                14));

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                33.333f));

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                14));

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                33.333f));

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                14));

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                33.334f));

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                28));

        var label =
            new Label
            {
                Text =
                    labelText,

                Dock =
                    DockStyle.Fill,

                ForeColor =
                    Color.Silver,

                TextAlign =
                    ContentAlignment.MiddleLeft
            };

        Vector3 value =
            kind switch
            {
                TransformVectorKind.Position =>
                    model.Position,

                TransformVectorKind.Rotation =>
                    model.RotationDegrees,

                TransformVectorKind.Scale =>
                    model.Scale,

                _ =>
                    Vector3.Zero
            };

        ScrubbableNumberBox xEditor =
            CreateTransformNumberEditor(
                value.X,
                kind);

        ScrubbableNumberBox yEditor =
            CreateTransformNumberEditor(
                value.Y,
                kind);

        ScrubbableNumberBox zEditor =
            CreateTransformNumberEditor(
                value.Z,
                kind);

        AddAxisEditor(
            row,
            1,
            "X",
            xEditor);

        AddAxisEditor(
            row,
            3,
            "Y",
            yEditor);

        AddAxisEditor(
            row,
            5,
            "Z",
            zEditor);

        row.Controls.Add(
            label,
            0,
            0);

        var resetRowButton =
            new Button
            {
                Text =
                    "↺",

                AccessibleName =
                    $"Reset {labelText}",

                Dock =
                    DockStyle.Fill,

                FlatStyle =
                    FlatStyle.Flat,

                BackColor =
                    row.BackColor,

                ForeColor =
                    Color.Gainsboro,

                Margin =
                    new Padding(
                        2,
                        0,
                        0,
                        0),

                Padding =
                    new Padding(
                        0),

                TabStop =
                    false
            };

        resetRowButton.FlatAppearance.BorderSize =
            0;

        resetRowButton.Click +=
            (_, _) =>
            {
                BeginTransformUndo(
                    model,
                    $"Reset {labelText}");

                switch (kind)
                {
                    case TransformVectorKind.Position:
                        model.Position =
                            Vector3.Zero;
                        break;

                    case TransformVectorKind.Rotation:
                        model.RotationDegrees =
                            Vector3.Zero;
                        break;

                    case TransformVectorKind.Scale:
                        model.Scale =
                            Vector3.One;
                        break;
                }

                ApplySceneModelTransform();
                CommitTransformUndo();
                RefreshSceneSidebar();
            };

        row.Controls.Add(
            resetRowButton,
            7,
            0);

        void HandleValueChanged(
            object? sender,
            EventArgs eventArgs)
        {
            if (_updatingTransformInspector)
            {
                return;
            }

            Vector3 editedValue =
                new(
                    xEditor.Value,
                    yEditor.Value,
                    zEditor.Value);

            switch (kind)
            {
                case TransformVectorKind.Position:
                    model.Position =
                        editedValue;
                    break;

                case TransformVectorKind.Rotation:
                    model.RotationDegrees =
                        editedValue;
                    break;

                case TransformVectorKind.Scale:
                    model.Scale =
                        editedValue;
                    break;
            }

            ApplySceneModelTransform();
        }

        void BeginEditorUndo(
            object? sender,
            EventArgs eventArgs)
        {
            BeginTransformUndo(
                model,
                $"Edit {labelText}");
        }

        void CommitEditorUndo(
            object? sender,
            EventArgs eventArgs)
        {
            CommitTransformUndo();
        }

        xEditor.InteractionStarted +=
            BeginEditorUndo;

        yEditor.InteractionStarted +=
            BeginEditorUndo;

        zEditor.InteractionStarted +=
            BeginEditorUndo;

        xEditor.InteractionCompleted +=
            CommitEditorUndo;

        yEditor.InteractionCompleted +=
            CommitEditorUndo;

        zEditor.InteractionCompleted +=
            CommitEditorUndo;

        xEditor.ValueChanged +=
            HandleValueChanged;

        yEditor.ValueChanged +=
            HandleValueChanged;

        zEditor.ValueChanged +=
            HandleValueChanged;

        return row;
    }

    private Control CreateTransformModeControls(int width)
    {
        var container = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.FromArgb(46, 46, 54),
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            Padding = new Padding(8, 7, 8, 7),
            Width = width,
            WrapContents = false
        };

        var modes = new TableLayoutPanel
        {
            ColumnCount = 3,
            Height = 32,
            Margin = Padding.Empty,
            RowCount = 1,
            Width = width - 16
        };

        for (int index = 0; index < 3; index++)
        {
            modes.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 33.333f));
        }

        _moveInspectorButton = CreateInspectorChoiceButton(
            "Move  W",
            _transformGizmoMode == TransformGizmoMode.Move,
            () => SetTransformGizmoMode(TransformGizmoMode.Move));

        _rotateInspectorButton = CreateInspectorChoiceButton(
            "Rotate  E",
            _transformGizmoMode == TransformGizmoMode.Rotate,
            () => SetTransformGizmoMode(TransformGizmoMode.Rotate));

        _scaleInspectorButton = CreateInspectorChoiceButton(
            "Scale  R",
            _transformGizmoMode == TransformGizmoMode.Scale,
            () => SetTransformGizmoMode(TransformGizmoMode.Scale));

        modes.Controls.Add(_moveInspectorButton, 0, 0);
        modes.Controls.Add(_rotateInspectorButton, 1, 0);
        modes.Controls.Add(_scaleInspectorButton, 2, 0);

        var orientation = new TableLayoutPanel
        {
            BackColor = Color.FromArgb(31, 31, 37),
            ColumnCount = 2,
            Height = 29,
            Margin = new Padding(0, 6, 0, 0),
            Padding = new Padding(3),
            RowCount = 1,
            Width = width - 16
        };

        orientation.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50.0f));
        orientation.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50.0f));

        _localInspectorButton = CreateInspectorChoiceButton(
            "Local",
            _transformGizmoOrientation == TransformGizmoOrientation.Local,
            () => SetTransformGizmoOrientation(TransformGizmoOrientation.Local));

        _globalInspectorButton = CreateInspectorChoiceButton(
            "Global",
            _transformGizmoOrientation == TransformGizmoOrientation.Global,
            () => SetTransformGizmoOrientation(TransformGizmoOrientation.Global));

        orientation.Controls.Add(_localInspectorButton, 0, 0);
        orientation.Controls.Add(_globalInspectorButton, 1, 0);

        container.Controls.Add(modes);
        container.Controls.Add(orientation);
        return container;
    }

    private Control CreateViewInspector(int width)
    {
        var content = CreateInspectorSectionContent(width);

        var cameraLabel = new Label
        {
            AutoSize = false,
            ForeColor = Color.Silver,
            Height = 24,
            Margin = Padding.Empty,
            Text = "Camera views",
            TextAlign = ContentAlignment.MiddleLeft,
            Width = width - 20
        };

        content.Controls.Add(cameraLabel);

        var views = new TableLayoutPanel
        {
            ColumnCount = 3,
            Height = 64,
            Margin = Padding.Empty,
            RowCount = 2,
            Width = width - 20
        };

        for (int index = 0; index < 3; index++)
        {
            views.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 33.333f));
        }

        views.RowStyles.Add(new RowStyle(SizeType.Percent, 50.0f));
        views.RowStyles.Add(new RowStyle(SizeType.Percent, 50.0f));

        AddCameraViewButton(views, "Front", CameraPresetView.Front, 0, 0);
        AddCameraViewButton(views, "Right", CameraPresetView.Right, 1, 0);
        AddCameraViewButton(views, "Left", CameraPresetView.Left, 2, 0);
        AddCameraViewButton(views, "Top", CameraPresetView.Top, 0, 1);
        AddCameraViewButton(views, "Bottom", CameraPresetView.Bottom, 1, 1);
        AddCameraViewButton(views, "Back", CameraPresetView.Back, 2, 1);

        content.Controls.Add(views);

        var focusButton = CreateInspectorActionButton("Frame selection / scene  F");
        focusButton.Click += (_, _) => FocusSelectedModelOrScene();
        content.Controls.Add(focusButton);

        content.Controls.Add(CreateInspectorToggleRow(
            "Ground grid",
            _gridVisible,
            value =>
            {
                if (_gridVisible != value)
                {
                    ToggleGroundGrid();
                }
            },
            out _gridInspectorToggle));

        content.Controls.Add(CreateInspectorToggleRow(
            "Statistics",
            _statisticsVisible,
            value =>
            {
                if (_statisticsMenuItem != null)
                {
                    _statisticsMenuItem.Checked = value;
                }
                else if (_statisticsVisible != value)
                {
                    ToggleStatistics();
                }
            },
            out _statisticsInspectorToggle));

        content.Controls.Add(CreateInspectorToggleRow(
            "SpaceMouse camera",
            _spaceMouseCameraMode,
            value =>
            {
                if (_spaceMouseCameraMenuItem != null)
                {
                    _spaceMouseCameraMenuItem.Checked = value;
                }
                else
                {
                    SetSpaceMouseCameraMode(value);
                }
            },
            out _spaceMouseInspectorToggle));

        return CreateInspectorSection(
            "View",
            width,
            _viewInspectorExpanded,
            () =>
            {
                _viewInspectorExpanded = !_viewInspectorExpanded;
                RefreshSceneSidebar();
            },
            content);
    }

    private Control CreateRenderingInspector(int width)
    {
        var content = CreateInspectorSectionContent(width);

        content.Controls.Add(CreateInspectorToggleRow(
            "Environment lighting",
            _viewerSettings.EnvironmentLightingEnabled,
            value => SetMenuToggle(_environmentLightingMenuItem, value),
            out _environmentInspectorToggle));

        content.Controls.Add(CreateInspectorToggleRow(
            "Direct light",
            _viewerSettings.DirectLightEnabled,
            value => SetMenuToggle(_directLightMenuItem, value),
            out _directLightInspectorToggle));

        content.Controls.Add(CreateInspectorToggleRow(
            "Directional shadows",
            _viewerSettings.ShadowsEnabled,
            value => SetMenuToggle(_shadowsEnabledMenuItem, value),
            out _shadowsInspectorToggle));

        content.Controls.Add(CreateInspectorToggleRow(
            "Bloom",
            _viewerSettings.BloomEnabled,
            value => SetMenuToggle(_bloomEnabledMenuItem, value),
            out _bloomInspectorToggle));

        content.Controls.Add(CreateRenderingDetailsInspector(width - 20));

        return CreateInspectorSection(
            "Rendering",
            width,
            _renderingInspectorExpanded,
            () =>
            {
                _renderingInspectorExpanded = !_renderingInspectorExpanded;
                RefreshSceneSidebar();
            },
            content);
    }

    private Control CreateRenderingDetailsInspector(int width)
    {
        var content = CreateInspectorSectionContent(width);

        content.Controls.Add(CreateInspectorGroupLabel("Lighting", width));
        content.Controls.Add(CreateInspectorSliderRow(
            "Environment intensity",
            _viewerSettings.EnvironmentIntensity,
            3.0f,
            value =>
            {
                _viewerSettings.EnvironmentIntensity = value;
                _renderer.SetEnvironmentIntensity(value);
                ScheduleViewerSettingsSave();
            },
            width));
        content.Controls.Add(CreateInspectorSliderRow(
            "Direct light intensity",
            _viewerSettings.DirectLightIntensity,
            3.0f,
            value =>
            {
                _viewerSettings.DirectLightIntensity = value;
                _renderer.SetDirectLightIntensity(value);
                ScheduleViewerSettingsSave();
            },
            width));
        content.Controls.Add(CreateInspectorSliderRow(
            "EXR background opacity",
            _viewerSettings.EnvironmentBackgroundOpacity,
            1.0f,
            value =>
            {
                _viewerSettings.EnvironmentBackgroundOpacity = value;
                _renderer.SetEnvironmentBackgroundOpacity(value);
                ScheduleViewerSettingsSave();
            },
            width));

        content.Controls.Add(CreateInspectorGroupLabel("Bloom", width));
        content.Controls.Add(CreateInspectorSliderRow(
            "Threshold",
            _viewerSettings.BloomThreshold,
            1.0f,
            value =>
            {
                _viewerSettings.BloomThreshold = value;
                _renderer.SetBloomThreshold(value);
                ScheduleViewerSettingsSave();
            },
            width));
        content.Controls.Add(CreateInspectorSliderRow(
            "Intensity",
            _viewerSettings.BloomIntensity,
            3.0f,
            value =>
            {
                _viewerSettings.BloomIntensity = value;
                _renderer.SetBloomIntensity(value);
                ScheduleViewerSettingsSave();
            },
            width));
        content.Controls.Add(CreateInspectorSliderRow(
            "Radius",
            _viewerSettings.BloomRadius,
            4.0f,
            value =>
            {
                _viewerSettings.BloomRadius = value;
                _renderer.SetBloomRadius(value);
                ScheduleViewerSettingsSave();
            },
            width));

        content.Controls.Add(CreateInspectorGroupLabel("Directional shadows", width));
        content.Controls.Add(CreateInspectorSliderRow(
            "Strength",
            _viewerSettings.ShadowStrength,
            1.0f,
            value =>
            {
                _viewerSettings.ShadowStrength = value;
                _renderer.SetShadowStrength(value);
                ScheduleViewerSettingsSave();
            },
            width));
        content.Controls.Add(CreateInspectorSliderRow(
            "Softness",
            _viewerSettings.ShadowSoftness,
            3.0f,
            value =>
            {
                _viewerSettings.ShadowSoftness = value;
                _renderer.SetShadowSoftness(value);
                ScheduleViewerSettingsSave();
            },
            width));

        return CreateInspectorSection(
            "Rendering details",
            width,
            _renderingDetailsInspectorExpanded,
            () =>
            {
                _renderingDetailsInspectorExpanded =
                    !_renderingDetailsInspectorExpanded;
                RefreshSceneSidebar();
            },
            content);
    }

    private static Control CreateInspectorGroupLabel(string text, int width)
    {
        return new Label
        {
            ForeColor = Color.FromArgb(255, 224, 126),
            Height = 25,
            Margin = new Padding(0, 7, 0, 0),
            Text = text,
            TextAlign = ContentAlignment.BottomLeft,
            Width = width - 20
        };
    }

    private static Control CreateInspectorSliderRow(
        string text,
        float value,
        float maximum,
        Action<float> changed,
        int width)
    {
        int maximumPercent =
            (int)MathF.Round(maximum * 100.0f);

        var row = new Panel
        {
            BackColor = Color.FromArgb(46, 46, 54),
            Height = 52,
            Margin = Padding.Empty,
            Width = width - 20
        };

        var label = new Label
        {
            Dock = DockStyle.Top,
            ForeColor = Color.Silver,
            Height = 20,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var slider = new TrackBar
        {
            AutoSize = false,
            BackColor = Color.FromArgb(46, 46, 54),
            Dock = DockStyle.Bottom,
            Height = 30,
            LargeChange = Math.Max(10, maximumPercent / 6),
            Maximum = maximumPercent,
            Minimum = 0,
            SmallChange = Math.Max(1, maximumPercent / 100),
            TabStop = false,
            TickStyle = TickStyle.None,
            Value = Math.Clamp(
                (int)MathF.Round(value * 100.0f),
                0,
                maximumPercent)
        };

        void UpdateLabel()
        {
            label.Text = $"{text}: {slider.Value}%";
        }

        slider.ValueChanged +=
            (_, _) =>
            {
                UpdateLabel();
                changed(slider.Value / 100.0f);
            };

        UpdateLabel();
        row.Controls.Add(label);
        row.Controls.Add(slider);
        return row;
    }

    private static FlowLayoutPanel CreateInspectorSectionContent(int width)
    {
        return new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.FromArgb(46, 46, 54),
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            Padding = new Padding(10, 2, 10, 10),
            Width = width,
            WrapContents = false
        };
    }

    private static Control CreateInspectorSection(
        string title,
        int width,
        bool expanded,
        Action toggle,
        Control content)
    {
        var section = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.FromArgb(46, 46, 54),
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0, 8, 0, 0),
            Width = width,
            WrapContents = false
        };

        var header = new Button
        {
            BackColor = Color.FromArgb(49, 49, 57),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Height = 32,
            Margin = Padding.Empty,
            Padding = new Padding(8, 0, 0, 0),
            TabStop = false,
            Text = $"{(expanded ? "▼" : "▶")}  {title}",
            TextAlign = ContentAlignment.MiddleLeft,
            Width = width
        };

        header.FlatAppearance.BorderSize = 0;
        header.Click += (_, _) => toggle();
        section.Controls.Add(header);

        if (expanded)
        {
            section.Controls.Add(content);
        }

        return section;
    }

    private static Button CreateInspectorChoiceButton(
        string text,
        bool selected,
        Action click)
    {
        var button = new Button
        {
            BackColor = selected
                ? Color.FromArgb(66, 61, 46)
                : Color.FromArgb(43, 43, 50),
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            ForeColor = selected
                ? Color.FromArgb(255, 224, 126)
                : Color.Gainsboro,
            Margin = new Padding(2),
            TabStop = false,
            Text = text
        };

        button.FlatAppearance.BorderColor = selected
            ? Color.FromArgb(205, 169, 80)
            : Color.FromArgb(66, 66, 75);
        button.Click += (_, _) => click();
        return button;
    }

    private static Button CreateInspectorActionButton(string text)
    {
        var button = new Button
        {
            BackColor = Color.FromArgb(48, 48, 56),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.Gainsboro,
            Height = 30,
            Margin = new Padding(0, 7, 0, 2),
            TabStop = false,
            Text = text,
            Width = SceneSidebarWidth - 22
        };

        button.FlatAppearance.BorderColor = Color.FromArgb(67, 67, 76);
        return button;
    }

    private static Control CreateInspectorToggleRow(
        string text,
        bool value,
        Action<bool> changed,
        out CheckBox toggle)
    {
        var row = new Panel
        {
            Height = 31,
            Margin = Padding.Empty,
            Width = SceneSidebarWidth - 22
        };

        var label = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.Silver,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft
        };

        toggle = new CheckBox
        {
            Appearance = Appearance.Button,
            BackColor = value
                ? Color.FromArgb(66, 61, 46)
                : Color.FromArgb(43, 43, 50),
            Checked = value,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            ForeColor = value
                ? Color.FromArgb(255, 224, 126)
                : Color.Silver,
            Text = value ? "On" : "Off",
            TextAlign = ContentAlignment.MiddleCenter,
            Width = 52
        };

        CheckBox capturedToggle = toggle;
        capturedToggle.FlatAppearance.BorderColor = Color.FromArgb(67, 67, 76);
        capturedToggle.CheckedChanged +=
            (_, _) =>
            {
                capturedToggle.Text = capturedToggle.Checked ? "On" : "Off";
                capturedToggle.BackColor = capturedToggle.Checked
                    ? Color.FromArgb(66, 61, 46)
                    : Color.FromArgb(43, 43, 50);
                capturedToggle.ForeColor = capturedToggle.Checked
                    ? Color.FromArgb(255, 224, 126)
                    : Color.Silver;
                changed(capturedToggle.Checked);
            };

        row.Controls.Add(label);
        row.Controls.Add(capturedToggle);
        capturedToggle.BringToFront();
        return row;
    }

    private void AddCameraViewButton(
        TableLayoutPanel panel,
        string text,
        CameraPresetView preset,
        int column,
        int row)
    {
        Button button = CreateInspectorChoiceButton(
            text,
            selected: false,
            () => ApplyCameraPresetView(preset));

        panel.Controls.Add(button, column, row);
    }

    private static void SetMenuToggle(
        ToolStripMenuItem? item,
        bool value)
    {
        if (item != null)
        {
            item.Checked = value;
        }
    }

    private void UpdateInspectorControlState()
    {
        SetInspectorButtonSelected(
            _moveInspectorButton,
            _transformGizmoMode == TransformGizmoMode.Move);
        SetInspectorButtonSelected(
            _rotateInspectorButton,
            _transformGizmoMode == TransformGizmoMode.Rotate);
        SetInspectorButtonSelected(
            _scaleInspectorButton,
            _transformGizmoMode == TransformGizmoMode.Scale);
        SetInspectorButtonSelected(
            _localInspectorButton,
            _transformGizmoOrientation == TransformGizmoOrientation.Local);
        SetInspectorButtonSelected(
            _globalInspectorButton,
            _transformGizmoOrientation == TransformGizmoOrientation.Global);

        SetInspectorToggle(_gridInspectorToggle, _gridVisible);
        SetInspectorToggle(_statisticsInspectorToggle, _statisticsVisible);
        SetInspectorToggle(_spaceMouseInspectorToggle, _spaceMouseCameraMode);
        SetInspectorToggle(
            _environmentInspectorToggle,
            _viewerSettings.EnvironmentLightingEnabled);
        SetInspectorToggle(
            _directLightInspectorToggle,
            _viewerSettings.DirectLightEnabled);
        SetInspectorToggle(_shadowsInspectorToggle, _viewerSettings.ShadowsEnabled);
        SetInspectorToggle(_bloomInspectorToggle, _viewerSettings.BloomEnabled);
    }

    private static void SetInspectorButtonSelected(Button? button, bool selected)
    {
        if (button == null)
        {
            return;
        }

        button.BackColor = selected
            ? Color.FromArgb(66, 61, 46)
            : Color.FromArgb(43, 43, 50);
        button.ForeColor = selected
            ? Color.FromArgb(255, 224, 126)
            : Color.Gainsboro;
        button.FlatAppearance.BorderColor = selected
            ? Color.FromArgb(205, 169, 80)
            : Color.FromArgb(66, 66, 75);
    }

    private static void SetInspectorToggle(CheckBox? toggle, bool value)
    {
        if (toggle != null && toggle.Checked != value)
        {
            toggle.Checked = value;
        }
    }

    private static void AddAxisEditor(
        TableLayoutPanel row,
        int labelColumn,
        string axis,
        ScrubbableNumberBox editor)
    {
        var axisLabel =
            new Label
            {
                Text =
                    axis,

                Dock =
                    DockStyle.Fill,

                ForeColor = axis switch
                {
                    "X" => Color.FromArgb(235, 104, 100),
                    "Y" => Color.FromArgb(108, 202, 116),
                    _ => Color.FromArgb(105, 151, 230)
                },

                TextAlign =
                    ContentAlignment.MiddleCenter
            };

        row.Controls.Add(
            axisLabel,
            labelColumn,
            0);

        row.Controls.Add(
            editor,
            labelColumn + 1,
            0);
    }

    private static ScrubbableNumberBox CreateTransformNumberEditor(
        float value,
        TransformVectorKind kind)
    {
        return new ScrubbableNumberBox
        {
            Dock =
                DockStyle.Fill,
            Margin =
                new Padding(
                    0,
                    1,
                    2,
                    1),
            DragSensitivity = kind == TransformVectorKind.Rotation
                ? 0.1f
                : 0.01f,
            Value =
                value
        };
    }

    /// <summary>
    /// Rebuilds the renderer scene after a model transform changes without reframing the camera.
    /// </summary>
    private void ApplySceneModelTransform()
    {
        UpdateRenderedSceneGeometry();
    }

    private int GetSceneModelContentWidth()
    {
        return Math.Max(
            240,
            _sceneModelList.ClientSize.Width -
            2);
    }

    private void UpdateSceneModelPanelWidths()
    {
        int width =
            GetSceneModelContentWidth();

        foreach (Control control in
                 _sceneModelList.Controls)
        {
            control.Width =
                width;
        }
    }

    private void UpdateSceneSidebarCommands()
    {
        _removeSceneModelButton.Enabled =
            _sceneDocument.SelectedModel != null;
    }

    private static Button CreateSceneSidebarButton(
        string text)
    {
        return new Button
        {
            Text =
                text,

            AutoSize =
                true,

            Height =
                30,

            FlatStyle =
                FlatStyle.Flat,

            BackColor =
                Color.FromArgb(
                    52,
                    52,
                    60),

            ForeColor =
                Color.White,

            Margin =
                new Padding(
                    0,
                    0,
                    8,
                    0)
        };
    }
}
