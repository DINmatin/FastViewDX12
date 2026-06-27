using System;
using System.Drawing;
using System.Windows.Forms;

namespace FastViewDX12;

// Optional scene-management panel. It starts collapsed so FastView still opens as a viewer.
public sealed partial class MainForm
{
    private const int SceneSidebarWidth = 280;

    private Panel _sceneSidebar = null!;

    private Button _sceneSidebarToggleButton = null!;

    private ListBox _sceneModelList = null!;

    private Button _removeSceneModelButton = null!;

    /// <summary>
    /// Creates the hidden scene sidebar and the small overlay button used to reveal it.
    /// </summary>
    private void InitializeSceneSidebar()
    {
        _sceneModelList =
            new ListBox
            {
                Dock =
                    DockStyle.Fill,

                BorderStyle =
                    BorderStyle.None,

                BackColor =
                    Color.FromArgb(
                        34,
                        34,
                        40),

                ForeColor =
                    Color.White,

                IntegralHeight =
                    false
            };

        _sceneModelList.SelectedIndexChanged +=
            (_, _) =>
            {
                _sceneDocument.Select(
                    _sceneModelList.SelectedItem as SceneModel);

                UpdateSceneSidebarCommands();
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
    /// Synchronizes the list with the scene document while preserving its current selection.
    /// </summary>
    private void RefreshSceneSidebar()
    {
        SceneModel? selectedModel =
            _sceneDocument.SelectedModel;

        _sceneModelList.BeginUpdate();

        try
        {
            _sceneModelList.Items.Clear();

            foreach (SceneModel model in
                     _sceneDocument.Models)
            {
                _sceneModelList.Items.Add(
                    model);
            }

            if (selectedModel != null)
            {
                _sceneModelList.SelectedItem =
                    selectedModel;
            }
        }
        finally
        {
            _sceneModelList.EndUpdate();
        }

        UpdateSceneSidebarCommands();
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
