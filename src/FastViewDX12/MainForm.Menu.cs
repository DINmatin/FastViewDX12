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

        var exportPreviewItem =
            new ToolStripMenuItem(
                "Export preview PNG...");

        exportPreviewItem.Click +=
            (_, _) =>
            {
                ExportPreviewPng();
            };

        fileMenu.DropDownItems.Add(
            exportPreviewItem);

        var viewMenu =
            new ToolStripMenuItem(
                "&View");

        var backgroundMenu =
            new ToolStripMenuItem(
                "Background");

        var solidBackgroundItem =
            new ToolStripMenuItem(
                "Solid color...")
            {
                Checked =
                    true
            };

        var environmentBackgroundItem =
            new ToolStripMenuItem(
                "EXR environment...")
            {
                Checked =
                    false
            };

        var backgroundOpacityLabel =
            new ToolStripLabel(
                "EXR opacity: 100%")
            {
                Enabled =
                    false
            };

        var backgroundOpacitySlider =
            new TrackBar
            {
                Minimum =
                    0,

                Maximum =
                    100,

                Value =
                    100,

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
                    false
            };

        ToolStripControlHost backgroundOpacitySliderHost =
            CreateSliderHost(
                backgroundOpacitySlider);

        backgroundOpacitySliderHost.Enabled =
            false;

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

                solidBackgroundItem.Checked =
                    true;

                environmentBackgroundItem.Checked =
                    false;

                backgroundOpacityLabel.Enabled =
                    false;

                backgroundOpacitySlider.Enabled =
                    false;

                backgroundOpacitySliderHost.Enabled =
                    false;
            };

        environmentBackgroundItem.Click +=
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

                    solidBackgroundItem.Checked =
                        false;

                    environmentBackgroundItem.Checked =
                        true;

                    environmentBackgroundItem.Text =
                        $"EXR environment... ({Path.GetFileName(dialog.FileName)})";

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
            };

        backgroundMenu.DropDownItems.Add(
            solidBackgroundItem);

        backgroundMenu.DropDownItems.Add(
            environmentBackgroundItem);

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
                    true
            };

        environmentEnabledItem.CheckedChanged +=
            (_, _) =>
            {
                _renderer
                    .SetEnvironmentLightingEnabled(
                        environmentEnabledItem.Checked);
            };

        var directLightEnabledItem =
            new ToolStripMenuItem(
                "Direct Light")
            {
                CheckOnClick =
                    true,

                Checked =
                    true
            };

        directLightEnabledItem.CheckedChanged +=
            (_, _) =>
            {
                _renderer
                    .SetDirectLightEnabled(
                        directLightEnabledItem.Checked);
            };

        var environmentIntensityLabel =
            new ToolStripLabel(
                "Environment intensity: 100%");

        TrackBar environmentIntensitySlider =
            CreateIntensitySlider();

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
            };

        var environmentSliderHost =
            CreateSliderHost(
                environmentIntensitySlider);

        var directLightIntensityLabel =
            new ToolStripLabel(
                "Direct light intensity: 100%");

        TrackBar directLightIntensitySlider =
            CreateIntensitySlider();

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
