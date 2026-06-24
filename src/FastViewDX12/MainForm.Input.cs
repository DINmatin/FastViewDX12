using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FastViewDX12;

// Mouse, keyboard, and drag-and-drop routing for camera, light, and file interaction.
public sealed partial class MainForm
{
    /// <summary>
    /// Chooses light rotation, orbit, or pan according to the pressed mouse button and modifier keys.
    /// </summary>
    private void RenderPanel_MouseDown(
    object? sender,
    MouseEventArgs e)
    {
        bool shiftPressed =
            (ModifierKeys & Keys.Shift) ==
            Keys.Shift;

        if (e.Button == MouseButtons.Right &&
            shiftPressed)
        {
            _isRotatingLight = true;

            _renderer.BeginLightRotation(
                e.Location);

            _renderPanel.Capture = true;
            return;
        }

        _isRotatingLight = false;

        if (e.Button == MouseButtons.Left)
        {
            _renderer.BeginOrbit(
                e.Location);

            _renderPanel.Capture = true;
        }
        else if (e.Button == MouseButtons.Right)
        {
            _renderer.BeginPan(
                e.Location);

            _renderPanel.Capture = true;
        }
    }

    /// <summary>
    /// Forwards pointer movement to the active camera or light interaction.
    /// </summary>
    private void RenderPanel_MouseMove(
     object? sender,
     MouseEventArgs e)
    {
        if (_isRotatingLight)
        {
            _renderer.OnLightMouseMove(
                e.Location);

            return;
        }

        _renderer.OnCameraMouseMove(
            e.Location);
    }

    /// <summary>
    /// Ends camera and light interactions when a mouse button is released.
    /// </summary>
    private void RenderPanel_MouseUp(
      object? sender,
      MouseEventArgs e)
    {
        if (_isRotatingLight)
        {
            _renderer.EndLightRotation();
            _isRotatingLight = false;
        }
        else
        {
            _renderer.EndCameraInteraction();
        }

        _renderPanel.Capture = false;
    }

    /// <summary>
    /// Zooms the orbit camera.
    /// </summary>
    private void RenderPanel_MouseWheel(object? sender, MouseEventArgs e) { _renderer.OnCameraMouseWheel(e.Delta); }

    /// <summary>
    /// Accepts drag-and-drop only when at least one supported model path is present.
    /// </summary>
    private void RenderPanel_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];

            if (files is { Length: 1 })
            {
                string ext = Path.GetExtension(files[0]).ToLowerInvariant();
                if (ext == ".glb" || ext == ".gltf")
                {
                    e.Effect = DragDropEffects.Copy;
                    return;
                }
            }
        }

        e.Effect = DragDropEffects.None;
    }

    /// <summary>
    /// Loads the first supported GLB or glTF file from a drop operation.
    /// </summary>
    private void RenderPanel_DragDrop(object? sender, DragEventArgs e) { try { string[]? files = e.Data?.GetData(DataFormats.FileDrop) as string[]; if (files is not { Length: 1 }) return; LoadModelFromPath(files[0], true); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "GLB load failed", MessageBoxButtons.OK, MessageBoxIcon.Error); } }

    /// <summary>
    /// Handles keyboard shortcuts such as fitting the camera to the current scene.
    /// </summary>
    private void MainForm_KeyDown(object? sender, KeyEventArgs e) { if (e.KeyCode == Keys.F) { _renderer.FitCameraToScene(); } }

}
