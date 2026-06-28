using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FastViewDX12;

// Menu construction and commands that require user dialogs.
public sealed partial class MainForm
{
    /// <summary>
    /// Builds all File, View, Lighting, and background controls and connects them to renderer commands.
    /// </summary>
    /// <returns>The configured menu strip.</returns>
    private MenuStrip CreateMainMenu()
    {
        var menuStrip =
            new MenuStrip
            {
                Dock =
                    DockStyle.Top
            };

        var fileMenu =
            new ToolStripMenuItem(
                "&File");

        var openModelItem =
            new ToolStripMenuItem(
                "&Open model...")
            {
                ShortcutKeys =
                    Keys.Control |
                    Keys.O
            };

        openModelItem.Click +=
            (_, _) =>
            {
                ChooseModelFile(
                    addToScene: false);
            };

        var addModelItem =
            new ToolStripMenuItem(
                "&Add model...")
            {
                ShortcutKeys =
                    Keys.Control |
                    Keys.Shift |
                    Keys.O
            };

        addModelItem.Click +=
            (_, _) =>
            {
                ChooseModelFile(
                    addToScene: true);
            };

        var exportPreviewItem =
            new ToolStripMenuItem(
                "Export preview PNG...");

        exportPreviewItem.Click +=
            (_, _) =>
            {
                ExportPreviewPng();
            };

        fileMenu.DropDownItems.Add(
            openModelItem);

        fileMenu.DropDownItems.Add(
            addModelItem);

        fileMenu.DropDownItems.Add(
            new ToolStripSeparator());

        fileMenu.DropDownItems.Add(
            exportPreviewItem);

        var viewMenu =
            new ToolStripMenuItem(
                "&View");

        var backgroundMenu =
            new ToolStripMenuItem(
                "Background");

        bool environmentBackgroundSelected =
            _viewerSettings.BackgroundMode ==
            ViewerBackgroundMode.Environment;

        bool builtInEnvironmentSelected =
            environmentBackgroundSelected &&
            string.IsNullOrWhiteSpace(
                _viewerSettings.EnvironmentMapPath);

        bool customEnvironmentSelected =
            environmentBackgroundSelected &&
            !string.IsNullOrWhiteSpace(
                _viewerSettings.EnvironmentMapPath);

        var solidBackgroundItem =
            new ToolStripMenuItem(
                "Solid color...")
            {
                Checked =
                    !environmentBackgroundSelected
            };

        var builtInEnvironmentBackgroundItem =
            new ToolStripMenuItem(
                "Built-in EXR")
            {
                Checked =
                    builtInEnvironmentSelected
            };

        string customEnvironmentMenuText =
            string.IsNullOrWhiteSpace(
                _viewerSettings.EnvironmentMapPath)
                ? "EXR file..."
                : $"EXR file... " +
                  $"({Path.GetFileName(_viewerSettings.EnvironmentMapPath)})";

        var customEnvironmentBackgroundItem =
            new ToolStripMenuItem(
                customEnvironmentMenuText)
            {
                Checked =
                    customEnvironmentSelected
            };

        int savedBackgroundOpacityPercent =
            Math.Clamp(
                (int)MathF.Round(
                    _viewerSettings.EnvironmentBackgroundOpacity *
                    100.0f),
                0,
                100);

        var backgroundOpacityLabel =
            new ToolStripLabel(
                $"EXR opacity: {savedBackgroundOpacityPercent}%")
            {
                Enabled =
                    environmentBackgroundSelected
            };

        var backgroundOpacitySlider =
            new TrackBar
            {
                Minimum =
                    0,

                Maximum =
                    100,

                Value =
                    savedBackgroundOpacityPercent,

                SmallChange =
                    5,

                LargeChange =
                    10,

                TickFrequency =
                    10,

                TickStyle =
                    TickStyle.None,

                AutoSize =
                    false,

                Size =
                    new Size(
                        230,
                        34),

                TabStop =
                    false,

                Enabled =
                    environmentBackgroundSelected
            };

        ToolStripControlHost backgroundOpacitySliderHost =
            CreateSliderHost(
                backgroundOpacitySlider);

        backgroundOpacitySliderHost.Enabled =
            environmentBackgroundSelected;

        solidBackgroundItem.Click +=
            (_, _) =>
            {
                using var dialog =
                    new ColorDialog
                    {
                        Color =
                            _renderPanel.BackColor,

                        FullOpen =
                            true
                    };

                if (dialog.ShowDialog(this) !=
                    DialogResult.OK)
                {
                    return;
                }

                _renderPanel.BackColor =
                    dialog.Color;

                _renderer.SetBackgroundColor(
                    dialog.Color);

                _renderer.SetBackgroundMode(
                    ViewerBackgroundMode.SolidColor);

                _viewerSettings.BackgroundColorArgb =
                    dialog.Color.ToArgb();

                _viewerSettings.BackgroundMode =
                    ViewerBackgroundMode.SolidColor;

                ScheduleViewerSettingsSave();

                solidBackgroundItem.Checked =
                    true;

                builtInEnvironmentBackgroundItem.Checked =
                    false;

                customEnvironmentBackgroundItem.Checked =
                    false;

                backgroundOpacityLabel.Enabled =
                    false;

                backgroundOpacitySlider.Enabled =
                    false;

                backgroundOpacitySliderHost.Enabled =
                    false;
            };

        builtInEnvironmentBackgroundItem.Click +=
            (_, _) =>
            {
                if (!File.Exists(
                        DefaultEnvironmentMapPath))
                {
                    MessageBox.Show(
                        this,
                        $"Built-in EXR was not found:\n{DefaultEnvironmentMapPath}",
                        "Built-in EXR could not be loaded",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    return;
                }

                try
                {
                    _renderer.LoadEnvironmentMap(
                        DefaultEnvironmentMapPath);

                    _renderer.SetBackgroundMode(
                        ViewerBackgroundMode.Environment);

                    // Null means the bundled EXR. A non-null path is reserved
                    // for a user-selected external environment image.
                    _viewerSettings.EnvironmentMapPath =
                        null;

                    _viewerSettings.BackgroundMode =
                        ViewerBackgroundMode.Environment;

                    ScheduleViewerSettingsSave();

                    solidBackgroundItem.Checked =
                        false;

                    builtInEnvironmentBackgroundItem.Checked =
                        true;

                    customEnvironmentBackgroundItem.Checked =
                        false;

                    customEnvironmentBackgroundItem.Text =
                        "EXR file...";

                    backgroundOpacityLabel.Enabled =
                        true;

                    backgroundOpacitySlider.Enabled =
                        true;

                    backgroundOpacitySliderHost.Enabled =
                        true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        ex.Message,
                        "Built-in EXR could not be loaded",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

        customEnvironmentBackgroundItem.Click +=
            (_, _) =>
            {
                using var dialog =
                    new OpenFileDialog
                    {
                        Title =
                            "Choose EXR background",

                        Filter =
                            "OpenEXR image (*.exr)|*.exr",

                        CheckFileExists =
                            true,

                        Multiselect =
                            false,

                        RestoreDirectory =
                            true
                    };

                if (dialog.ShowDialog(this) !=
                    DialogResult.OK)
                {
                    return;
                }

                try
                {
                    _renderer.LoadEnvironmentMap(
                        dialog.FileName);

                    _renderer.SetBackgroundMode(
                        ViewerBackgroundMode.Environment);

                    _viewerSettings.EnvironmentMapPath =
                        dialog.FileName;

                    _viewerSettings.BackgroundMode =
                        ViewerBackgroundMode.Environment;

                    ScheduleViewerSettingsSave();

                    solidBackgroundItem.Checked =
                        false;

                    builtInEnvironmentBackgroundItem.Checked =
                        false;

                    customEnvironmentBackgroundItem.Checked =
                        true;

                    customEnvironmentBackgroundItem.Text =
                        $"EXR file... ({Path.GetFileName(dialog.FileName)})";

                    backgroundOpacityLabel.Enabled =
                        true;

                    backgroundOpacitySlider.Enabled =
                        true;

                    backgroundOpacitySliderHost.Enabled =
                        true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        ex.Message,
                        "EXR background could not be loaded",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

        backgroundOpacitySlider.ValueChanged +=
            (_, _) =>
            {
                float opacity =
                    backgroundOpacitySlider.Value /
                    100.0f;

                backgroundOpacityLabel.Text =
                    $"EXR opacity: " +
                    $"{backgroundOpacitySlider.Value}%";

                _renderer.SetEnvironmentBackgroundOpacity(
                    opacity);

                _viewerSettings.EnvironmentBackgroundOpacity =
                    opacity;

                ScheduleViewerSettingsSave();
            };

        backgroundMenu.DropDownItems.Add(
            solidBackgroundItem);

        backgroundMenu.DropDownItems.Add(
            builtInEnvironmentBackgroundItem);

        backgroundMenu.DropDownItems.Add(
            customEnvironmentBackgroundItem);

        backgroundMenu.DropDownItems.Add(
            new ToolStripSeparator());

        backgroundMenu.DropDownItems.Add(
            backgroundOpacityLabel);

        backgroundMenu.DropDownItems.Add(
            backgroundOpacitySliderHost);

        var environmentEnabledItem =
            new ToolStripMenuItem(
                "Environment Lighting")
            {
                CheckOnClick =
                    true,

                Checked =
                    _viewerSettings.EnvironmentLightingEnabled
            };

        environmentEnabledItem.CheckedChanged +=
            (_, _) =>
            {
                _renderer
                    .SetEnvironmentLightingEnabled(
                        environmentEnabledItem.Checked);

                _viewerSettings.EnvironmentLightingEnabled =
                    environmentEnabledItem.Checked;

                ScheduleViewerSettingsSave();
            };

        var directLightEnabledItem =
            new ToolStripMenuItem(
                "Direct Light")
            {
                CheckOnClick =
                    true,

                Checked =
                    _viewerSettings.DirectLightEnabled
            };

        directLightEnabledItem.CheckedChanged +=
            (_, _) =>
            {
                _renderer
                    .SetDirectLightEnabled(
                        directLightEnabledItem.Checked);

                _viewerSettings.DirectLightEnabled =
                    directLightEnabledItem.Checked;

                ScheduleViewerSettingsSave();
            };

        int savedEnvironmentIntensityPercent =
            Math.Clamp(
                (int)MathF.Round(
                    _viewerSettings.EnvironmentIntensity *
                    100.0f),
                0,
                300);

        var environmentIntensityLabel =
            new ToolStripLabel(
                $"Environment intensity: " +
                $"{savedEnvironmentIntensityPercent}%");

        TrackBar environmentIntensitySlider =
            CreateIntensitySlider();

        environmentIntensitySlider.Value =
            savedEnvironmentIntensityPercent;

        environmentIntensitySlider.ValueChanged +=
            (_, _) =>
            {
                float intensity =
                    environmentIntensitySlider.Value /
                    100.0f;

                environmentIntensityLabel.Text =
                    $"Environment intensity: " +
                    $"{environmentIntensitySlider.Value}%";

                _renderer.SetEnvironmentIntensity(
                    intensity);

                _viewerSettings.EnvironmentIntensity =
                    intensity;

                ScheduleViewerSettingsSave();
            };

        var environmentSliderHost =
            CreateSliderHost(
                environmentIntensitySlider);

        int savedDirectLightIntensityPercent =
            Math.Clamp(
                (int)MathF.Round(
                    _viewerSettings.DirectLightIntensity *
                    100.0f),
                0,
                300);

        var directLightIntensityLabel =
            new ToolStripLabel(
                $"Direct light intensity: " +
                $"{savedDirectLightIntensityPercent}%");

        TrackBar directLightIntensitySlider =
            CreateIntensitySlider();

        directLightIntensitySlider.Value =
            savedDirectLightIntensityPercent;

        directLightIntensitySlider.ValueChanged +=
            (_, _) =>
            {
                float intensity =
                    directLightIntensitySlider.Value /
                    100.0f;

                directLightIntensityLabel.Text =
                    $"Direct light intensity: " +
                    $"{directLightIntensitySlider.Value}%";

                _renderer.SetDirectLightIntensity(
                    intensity);

                _viewerSettings.DirectLightIntensity =
                    intensity;

                ScheduleViewerSettingsSave();
            };

        var directLightSliderHost =
            CreateSliderHost(
                directLightIntensitySlider);

        viewMenu.DropDownItems.Add(
            backgroundMenu);

        viewMenu.DropDownItems.Add(
            new ToolStripSeparator());

        viewMenu.DropDownItems.Add(
            environmentEnabledItem);

        viewMenu.DropDownItems.Add(
            directLightEnabledItem);

        viewMenu.DropDownItems.Add(
            new ToolStripSeparator());

        viewMenu.DropDownItems.Add(
            environmentIntensityLabel);

        viewMenu.DropDownItems.Add(
            environmentSliderHost);

        viewMenu.DropDownItems.Add(
            directLightIntensityLabel);

        viewMenu.DropDownItems.Add(
            directLightSliderHost);

        menuStrip.Items.Add(
            fileMenu);

        menuStrip.Items.Add(
            viewMenu);

        return menuStrip;
    }

    /// <summary>
    /// Creates a percentage slider hosted inside a ToolStrip item and invokes a normalized value callback.
    /// </summary>
    private static TrackBar CreateIntensitySlider()
    {
        return new TrackBar
        {
            Minimum =
                0,

            Maximum =
                300,

            Value =
                100,

            SmallChange =
                5,

            LargeChange =
                25,

            TickFrequency =
                25,

            TickStyle =
                TickStyle.None,

            AutoSize =
                false,

            Size =
                new Size(
                    230,
                    34),

            TabStop =
                false
        };
    }

    /// <summary>
    /// Wraps a WinForms track bar in a ToolStripControlHost with consistent sizing.
    /// </summary>
    private static ToolStripControlHost CreateSliderHost(
    TrackBar slider)
    {
        return new ToolStripControlHost(
            slider)
        {
            AutoSize =
                false,

            Size =
                new Size(
                    250,
                    40),

            Margin =
                new Padding(
                    8,
                    0,
                    8,
                    4)
        };
    }

    /// <summary>
    /// Prompts for a destination and exports a square PNG using the renderer capture pipeline.
    /// </summary>
    private void ExportPreviewPng()
    {
        using var dialog =
            new SaveFileDialog
            {
                Title =
                    "Export FastView preview",

                Filter =
                    "PNG image (*.png)|*.png",

                DefaultExt =
                    "png",

                AddExtension =
                    true,

                FileName =
                    "FastView_Preview.png",

                RestoreDirectory =
                    true
            };

        if (dialog.ShowDialog(this) !=
            DialogResult.OK)
        {
            return;
        }

        try
        {
            _renderer.FitCameraToScene();

            _renderer.SavePreviewPng(
                dialog.FileName,
                800,
                800);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Preview export failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

}
