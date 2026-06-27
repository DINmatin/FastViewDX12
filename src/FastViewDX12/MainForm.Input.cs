using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FastViewDX12;

// Mouse, keyboard, and drag-and-drop routing for camera, light, selection, and file interaction.
public sealed partial class MainForm
{
    private const int ViewportClickDragThresholdPixels =
        4;

    private bool _leftViewportInteractionPending;

    private bool _leftViewportOrbitStarted;

    private Point _leftViewportMouseDownPosition;

    /// <summary>
    /// Gives the move gizmo first priority. A normal left press remains a
    /// possible selection click until the pointer moves far enough to become
    /// an orbit drag.
    /// </summary>
    private void RenderPanel_MouseDown(
        object? sender,
        MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left &&
            TryBeginMoveGizmoDrag(
                e.Location))
        {
            ResetPendingLeftViewportInteraction();

            _renderPanel.Capture =
                true;

            return;
        }

        bool shiftPressed =
            (ModifierKeys & Keys.Shift) ==
            Keys.Shift;

        if (e.Button == MouseButtons.Right &&
            shiftPressed)
        {
            ResetPendingLeftViewportInteraction();

            _isRotatingLight =
                true;

            _renderer.BeginLightRotation(
                e.Location);

            _renderPanel.Capture =
                true;

            return;
        }

        _isRotatingLight =
            false;

        if (e.Button == MouseButtons.Left)
        {
            _leftViewportMouseDownPosition =
                e.Location;

            _leftViewportInteractionPending =
                true;

            _leftViewportOrbitStarted =
                false;

            _renderPanel.Capture =
                true;

            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            ResetPendingLeftViewportInteraction();

            _renderer.BeginPan(
                e.Location);

            _renderPanel.Capture =
                true;
        }
    }

    /// <summary>
    /// Starts orbiting only after a small drag threshold. This keeps a simple
    /// left click available for model selection without causing a camera twitch.
    /// </summary>
    private void RenderPanel_MouseMove(
        object? sender,
        MouseEventArgs e)
    {
        if (UpdateMoveGizmoDrag(
                e.Location))
        {
            return;
        }

        UpdateMoveGizmoHover(
            e.Location);

        if (_isRotatingLight)
        {
            _renderer.OnLightMouseMove(
                e.Location);

            return;
        }

        if (_leftViewportInteractionPending &&
            (e.Button & MouseButtons.Left) ==
            MouseButtons.Left)
        {
            if (!_leftViewportOrbitStarted)
            {
                int deltaX =
                    e.Location.X -
                    _leftViewportMouseDownPosition.X;

                int deltaY =
                    e.Location.Y -
                    _leftViewportMouseDownPosition.Y;

                int thresholdSquared =
                    ViewportClickDragThresholdPixels *
                    ViewportClickDragThresholdPixels;

                int distanceSquared =
                    deltaX * deltaX +
                    deltaY * deltaY;

                if (distanceSquared <
                    thresholdSquared)
                {
                    return;
                }

                _renderer.BeginOrbit(
                    _leftViewportMouseDownPosition);

                _leftViewportOrbitStarted =
                    true;
            }

            _renderer.OnCameraMouseMove(
                e.Location);

            return;
        }

        _renderer.OnCameraMouseMove(
            e.Location);
    }

    /// <summary>
    /// A left release selects when no orbit drag began. Releasing after a drag
    /// ends orbiting without changing the current model selection.
    /// </summary>
    private void RenderPanel_MouseUp(
        object? sender,
        MouseEventArgs e)
    {
        if (EndMoveGizmoDrag())
        {
            ResetPendingLeftViewportInteraction();

            _renderPanel.Capture =
                false;

            return;
        }

        if (e.Button == MouseButtons.Left &&
            _leftViewportInteractionPending)
        {
            bool wasOrbitDrag =
                _leftViewportOrbitStarted;

            ResetPendingLeftViewportInteraction();

            if (wasOrbitDrag)
            {
                _renderer.EndCameraInteraction();
            }
            else
            {
                SelectSceneModelFromViewport(
                    e.Location);
            }

            _renderPanel.Capture =
                false;

            return;
        }

        if (_isRotatingLight)
        {
            _renderer.EndLightRotation();

            _isRotatingLight =
                false;
        }
        else
        {
            _renderer.EndCameraInteraction();
        }

        _renderPanel.Capture =
            false;
    }

    private void ResetPendingLeftViewportInteraction()
    {
        _leftViewportInteractionPending =
            false;

        _leftViewportOrbitStarted =
            false;
    }

    private void RenderPanel_MouseLeave(
        object? sender,
        EventArgs e)
    {
        ClearMoveGizmoHover();
    }

    /// <summary>
    /// Zooms the orbit camera.
    /// </summary>
    private void RenderPanel_MouseWheel(
        object? sender,
        MouseEventArgs e)
    {
        _renderer.OnCameraMouseWheel(
            e.Delta);
    }

    /// <summary>
    /// Accepts drag-and-drop only when at least one supported model path is present.
    /// </summary>
    private void RenderPanel_DragEnter(
        object? sender,
        DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(
                DataFormats.FileDrop) == true)
        {
            string[]? files =
                e.Data.GetData(
                    DataFormats.FileDrop) as string[];

            if (files is { Length: 1 })
            {
                string extension =
                    Path.GetExtension(
                        files[0])
                    .ToLowerInvariant();

                if (extension == ".glb" ||
                    extension == ".gltf")
                {
                    e.Effect =
                        DragDropEffects.Copy;

                    return;
                }
            }
        }

        e.Effect =
            DragDropEffects.None;
    }

    /// <summary>
    /// Loads the first supported GLB or glTF file from a drop operation.
    /// </summary>
    private void RenderPanel_DragDrop(
        object? sender,
        DragEventArgs e)
    {
        try
        {
            string[]? files =
                e.Data?.GetData(
                    DataFormats.FileDrop) as string[];

            if (files is not
                { Length: 1 })
            {
                return;
            }

            LoadModelFromPath(
                files[0],
                true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "GLB load failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// W, E, and R select Move, Rotate, and Scale. F frames the selected model,
    /// or the complete scene when no model is selected.
    /// </summary>
    private void MainForm_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Control ||
            e.Alt)
        {
            return;
        }

        bool handled =
            true;

        switch (e.KeyCode)
        {
            case Keys.W:
                SetTransformGizmoMode(
                    TransformGizmoMode.Move);
                break;

            case Keys.E:
                SetTransformGizmoMode(
                    TransformGizmoMode.Rotate);
                break;

            case Keys.R:
                SetTransformGizmoMode(
                    TransformGizmoMode.Scale);
                break;

            case Keys.F:
                FocusSelectedModelOrScene();
                break;

            default:
                handled =
                    false;
                break;
        }

        if (!handled)
        {
            return;
        }

        e.Handled =
            true;

        e.SuppressKeyPress =
            true;
    }
}
