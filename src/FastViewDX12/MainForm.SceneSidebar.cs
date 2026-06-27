using System;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;

namespace FastViewDX12;

// Optional scene-management panel. It starts collapsed so FastView still opens as a viewer.
public sealed partial class MainForm
{
    private const int SceneSidebarWidth = 300;

    private Panel _sceneSidebar = null!;

    private Button _sceneSidebarToggleButton = null!;

    private FlowLayoutPanel _sceneModelList = null!;

    private Button _removeSceneModelButton = null!;

    private bool _updatingTransformInspector;

    private bool _transformInspectorExpanded =
        true;

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
                    DockStyle.Top,

                Height =
                    42,

                Padding =
                    new Padding(
                        10,
                        12,
                        0,
                        0),

                Text =
                    "Scene models",

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
            titleLabel);

        _sceneSidebarToggleButton =
            new Button
            {
                Text =
                    "⋯",

                Size =
                    new Size(
                        36,
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
                    "Show scene models"
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

        _sceneSidebarToggleButton.Text =
            showSidebar
                ? "×"
                : "⋯";

        _sceneSidebarToggleButton.AccessibleName =
            showSidebar
                ? "Hide scene models"
                : "Show scene models";

        if (showSidebar)
        {
            RefreshSceneSidebar();
        }
        else
        {
            ClearMoveGizmoHover();
        }

        PositionSceneSidebarToggle();
        _sceneSidebarToggleButton.BringToFront();
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
    }

    /// <summary>
    /// Creates one model foldout. The selected model contains a Unity-style Transform section.
    /// </summary>
    private Control CreateSceneModelPanel(
        SceneModel model)
    {
        bool selected =
            ReferenceEquals(
                model,
                _sceneDocument.SelectedModel);

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
                    selected
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
                    selected
                        ? $"▼  {model.Name}"
                        : $"▶  {model.Name}",

                TextAlign =
                    ContentAlignment.MiddleLeft,

                FlatStyle =
                    FlatStyle.Flat,

                BackColor =
                    selected
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

        if (selected)
        {
            modelPanel.Controls.Add(
                CreateTransformInspector(
                    model,
                    contentWidth));
        }

        return modelPanel;
    }

    /// <summary>
    /// Creates the Transform foldout shown beneath the selected model.
    /// </summary>
    private Control CreateTransformInspector(
        SceneModel model,
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
                model.Position =
                    Vector3.Zero;

                model.RotationDegrees =
                    Vector3.Zero;

                model.Scale =
                    Vector3.One;

                ApplySceneModelTransform();
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
        SceneModel model,
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
                    7,

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
                68));

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

        NumericUpDown xEditor =
            CreateTransformNumberEditor(
                value.X);

        NumericUpDown yEditor =
            CreateTransformNumberEditor(
                value.Y);

        NumericUpDown zEditor =
            CreateTransformNumberEditor(
                value.Z);

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
                    (float)xEditor.Value,
                    (float)yEditor.Value,
                    (float)zEditor.Value);

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

        xEditor.ValueChanged +=
            HandleValueChanged;

        yEditor.ValueChanged +=
            HandleValueChanged;

        zEditor.ValueChanged +=
            HandleValueChanged;

        return row;
    }

    private static void AddAxisEditor(
        TableLayoutPanel row,
        int labelColumn,
        string axis,
        NumericUpDown editor)
    {
        var axisLabel =
            new Label
            {
                Text =
                    axis,

                Dock =
                    DockStyle.Fill,

                ForeColor =
                    Color.Silver,

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

    private static NumericUpDown CreateTransformNumberEditor(
        float value)
    {
        decimal safeValue =
            Math.Clamp(
                (decimal)value,
                -1000000m,
                1000000m);

        return new NumericUpDown
        {
            BackColor =
                Color.FromArgb(
                    31,
                    31,
                    37),

            BorderStyle =
                BorderStyle.FixedSingle,

            DecimalPlaces =
                3,

            Dock =
                DockStyle.Fill,

            ForeColor =
                Color.White,

            Increment =
                0.1m,

            Maximum =
                1000000m,

            Minimum =
                -1000000m,

            Margin =
                new Padding(
                    0,
                    1,
                    2,
                    1),

            ThousandsSeparator =
                false,

            Value =
                safeValue
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
