using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Windows.Forms;

namespace FastViewDX12;

// Screen-space editor overlay for moving the selected model along the global
// X, Y, or Z axis. The overlay stays outside the Direct3D scene, so it never
// appears in thumbnails or future scene exports.
public sealed partial class MainForm
{
    private const float MoveGizmoLengthPixels = 86.0f;

    private const float MoveGizmoHitRadiusPixels = 9.0f;

    private MoveGizmoAxis _hoveredMoveGizmoAxis;

    private MoveGizmoAxis _draggedMoveGizmoAxis;

    private SceneModel? _moveGizmoDragModel;

    private Point _moveGizmoDragStartMouse;

    private Vector3 _moveGizmoDragStartPosition;

    private Vector2 _moveGizmoDragScreenDirection;

    private float _moveGizmoDragWorldUnitsPerPixel;

    private MoveGizmoOverlayForm? _moveGizmoOverlayForm;

    // Cached layout used by the overlay paint callback. This avoids projecting
    // all four world-space points a second time during every WM_PAINT.
    private MoveGizmoLayout? _moveGizmoOverlayLayout;

    private MoveGizmoAxis _paintedHoveredMoveGizmoAxis;

    private MoveGizmoAxis _paintedDraggedMoveGizmoAxis;

    private enum MoveGizmoAxis
    {
        None,
        X,
        Y,
        Z
    }

    private readonly struct MoveGizmoAxisLayout
    {
        public MoveGizmoAxisLayout(
            MoveGizmoAxis axis,
            PointF end,
            Vector2 screenDirection,
            float worldUnitsPerPixel)
        {
            Axis = axis;
            End = end;
            ScreenDirection = screenDirection;
            WorldUnitsPerPixel = worldUnitsPerPixel;
        }

        public MoveGizmoAxis Axis { get; }

        public PointF End { get; }

        public Vector2 ScreenDirection { get; }

        public float WorldUnitsPerPixel { get; }
    }

    private sealed class MoveGizmoLayout
    {
        public required PointF Origin { get; init; }

        public required MoveGizmoAxisLayout[] Axes { get; init; }
    }

    /// <summary>
    /// Keeps a transparent, click-through overlay window aligned with the
    /// Direct3D render panel. A separate window is required because GDI drawing
    /// directly onto a flip-discard swap-chain window is not displayed reliably.
    /// </summary>
    private void UpdateMoveGizmoOverlay()
    {
        // Do the cheap visibility checks first. In the old version the layout
        // was projected even while the window was minimized or hidden.
        if (WindowState == FormWindowState.Minimized ||
            !_renderPanel.Visible ||
            !TryCreateMoveGizmoLayout(
                out MoveGizmoLayout? layout) ||
            layout == null)
        {
            HideMoveGizmoOverlay();
            return;
        }

        EnsureMoveGizmoOverlay();

        MoveGizmoOverlayForm? overlay =
            _moveGizmoOverlayForm;

        if (overlay == null ||
            overlay.IsDisposed)
        {
            return;
        }

        Rectangle renderBounds =
            _renderPanel.RectangleToScreen(
                _renderPanel.ClientRectangle);

        bool boundsChanged =
            overlay.Bounds != renderBounds;

        if (boundsChanged)
        {
            overlay.Bounds =
                renderBounds;
        }

        bool layoutChanged =
            !MoveGizmoLayoutsMatch(
                _moveGizmoOverlayLayout,
                layout);

        bool highlightChanged =
            _paintedHoveredMoveGizmoAxis !=
                _hoveredMoveGizmoAxis ||
            _paintedDraggedMoveGizmoAxis !=
                _draggedMoveGizmoAxis;

        bool wasVisible =
            overlay.Visible;

        _moveGizmoOverlayLayout =
            layout;

        _paintedHoveredMoveGizmoAxis =
            _hoveredMoveGizmoAxis;

        _paintedDraggedMoveGizmoAxis =
            _draggedMoveGizmoAxis;

        if (!wasVisible)
        {
            overlay.Show(
                this);
        }

        // The previous implementation invalidated the transparent top-level
        // window for every Direct3D frame. FastView renders from an unbounded
        // Application.Idle loop, so that produced a continuous stream of
        // WM_PAINT messages and made viewport input feel extremely sluggish.
        if (!wasVisible ||
            boundsChanged ||
            layoutChanged ||
            highlightChanged)
        {
            overlay.Invalidate();
        }
    }

    private void EnsureMoveGizmoOverlay()
    {
        if (_moveGizmoOverlayForm != null &&
            !_moveGizmoOverlayForm.IsDisposed)
        {
            return;
        }

        _moveGizmoOverlayForm =
            new MoveGizmoOverlayForm(
                PaintMoveGizmoOverlay);
    }

    private void HideMoveGizmoOverlay()
    {
        _moveGizmoOverlayLayout =
            null;

        if (_moveGizmoOverlayForm?.Visible == true)
        {
            _moveGizmoOverlayForm.Hide();
        }
    }

    private void DisposeMoveGizmoOverlay()
    {
        if (_moveGizmoOverlayForm == null)
        {
            return;
        }

        _moveGizmoOverlayForm.Close();
        _moveGizmoOverlayForm.Dispose();
        _moveGizmoOverlayForm = null;
        _moveGizmoOverlayLayout = null;
    }

    /// <summary>
    /// Paints the gizmo into the transparent overlay window.
    /// </summary>
    private void PaintMoveGizmoOverlay(
        Graphics graphics)
    {
        MoveGizmoLayout? layout =
            _moveGizmoOverlayLayout;

        if (layout == null)
        {
            return;
        }

        graphics.SmoothingMode =
            SmoothingMode.AntiAlias;

        foreach (MoveGizmoAxisLayout axis in
                 layout.Axes)
        {
            Color color =
                GetMoveGizmoAxisColor(
                    axis.Axis);

            bool highlighted =
                axis.Axis == _draggedMoveGizmoAxis ||
                axis.Axis == _hoveredMoveGizmoAxis;

            if (highlighted)
            {
                color =
                    Color.FromArgb(
                        255,
                        235,
                        90);
            }

            using var pen =
                new Pen(
                    color,
                    highlighted
                        ? 4.0f
                        : 3.0f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };

            graphics.DrawLine(
                pen,
                layout.Origin,
                axis.End);

            DrawMoveGizmoArrowHead(
                graphics,
                axis,
                color);

            using var labelBrush =
                new SolidBrush(
                    color);

            graphics.DrawString(
                axis.Axis.ToString(),
                SystemFonts.MessageBoxFont ??
                Control.DefaultFont,
                labelBrush,
                axis.End.X + 5.0f,
                axis.End.Y - 8.0f);
        }

        using var centerBrush =
            new SolidBrush(
                Color.White);

        graphics.FillEllipse(
            centerBrush,
            layout.Origin.X - 3.0f,
            layout.Origin.Y - 3.0f,
            6.0f,
            6.0f);
    }


    private static bool MoveGizmoLayoutsMatch(
        MoveGizmoLayout? first,
        MoveGizmoLayout second)
    {
        if (first == null ||
            first.Axes.Length != second.Axes.Length ||
            !PointsNearlyEqual(
                first.Origin,
                second.Origin))
        {
            return false;
        }

        for (int index = 0;
             index < first.Axes.Length;
             index++)
        {
            MoveGizmoAxisLayout firstAxis =
                first.Axes[index];

            MoveGizmoAxisLayout secondAxis =
                second.Axes[index];

            if (firstAxis.Axis != secondAxis.Axis ||
                !PointsNearlyEqual(
                    firstAxis.End,
                    secondAxis.End) ||
                Vector2.DistanceSquared(
                    firstAxis.ScreenDirection,
                    secondAxis.ScreenDirection) > 0.000001f ||
                MathF.Abs(
                    firstAxis.WorldUnitsPerPixel -
                    secondAxis.WorldUnitsPerPixel) > 0.000001f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PointsNearlyEqual(
        PointF first,
        PointF second)
    {
        const float tolerance =
            0.05f;

        return
            MathF.Abs(first.X - second.X) <= tolerance &&
            MathF.Abs(first.Y - second.Y) <= tolerance;
    }

    private static void DrawMoveGizmoArrowHead(
        Graphics graphics,
        MoveGizmoAxisLayout axis,
        Color color)
    {
        Vector2 direction =
            axis.ScreenDirection;

        Vector2 perpendicular =
            new(
                -direction.Y,
                direction.X);

        Vector2 tip =
            new(
                axis.End.X,
                axis.End.Y);

        Vector2 baseCenter =
            tip -
            direction * 11.0f;

        PointF[] triangle =
        [
            new PointF(
                tip.X,
                tip.Y),

            new PointF(
                baseCenter.X + perpendicular.X * 5.0f,
                baseCenter.Y + perpendicular.Y * 5.0f),

            new PointF(
                baseCenter.X - perpendicular.X * 5.0f,
                baseCenter.Y - perpendicular.Y * 5.0f)
        ];

        using var brush =
            new SolidBrush(
                color);

        graphics.FillPolygon(
            brush,
            triangle);
    }

    private bool TryCreateMoveGizmoLayout(
        out MoveGizmoLayout? layout)
    {
        layout = null;

        SceneModel? model =
            _sceneDocument.SelectedModel;

        if (!_sceneSidebar.Visible ||
            !_transformInspectorExpanded ||
            model == null ||
            !_renderer.TryProjectWorldToScreen(
                model.Position,
                out PointF origin))
        {
            return false;
        }

        var axes =
            new List<MoveGizmoAxisLayout>(
                3);

        AddMoveGizmoAxis(
            axes,
            model.Position,
            origin,
            MoveGizmoAxis.X,
            Vector3.UnitX);

        AddMoveGizmoAxis(
            axes,
            model.Position,
            origin,
            MoveGizmoAxis.Y,
            Vector3.UnitY);

        AddMoveGizmoAxis(
            axes,
            model.Position,
            origin,
            MoveGizmoAxis.Z,
            Vector3.UnitZ);

        if (axes.Count == 0)
        {
            return false;
        }

        layout =
            new MoveGizmoLayout
            {
                Origin = origin,
                Axes = axes.ToArray()
            };

        return true;
    }

    private void AddMoveGizmoAxis(
        List<MoveGizmoAxisLayout> axes,
        Vector3 worldOrigin,
        PointF screenOrigin,
        MoveGizmoAxis axis,
        Vector3 worldAxis)
    {
        if (!_renderer.TryProjectWorldToScreen(
            worldOrigin + worldAxis,
            out PointF projectedUnit))
        {
            return;
        }

        Vector2 projectedDirection =
            new(
                projectedUnit.X - screenOrigin.X,
                projectedUnit.Y - screenOrigin.Y);

        float pixelsPerWorldUnit =
            projectedDirection.Length();

        // An axis that points almost directly toward the camera cannot be
        // manipulated reliably in screen space, so omit it for that view.
        if (pixelsPerWorldUnit < 0.75f)
        {
            return;
        }

        Vector2 screenDirection =
            projectedDirection /
            pixelsPerWorldUnit;

        PointF end =
            new(
                screenOrigin.X +
                screenDirection.X * MoveGizmoLengthPixels,
                screenOrigin.Y +
                screenDirection.Y * MoveGizmoLengthPixels);

        axes.Add(
            new MoveGizmoAxisLayout(
                axis,
                end,
                screenDirection,
                1.0f / pixelsPerWorldUnit));
    }

    private bool TryBeginMoveGizmoDrag(
        Point mousePosition)
    {
        SceneModel? model =
            _sceneDocument.SelectedModel;

        if (model == null ||
            !TryHitMoveGizmo(
                mousePosition,
                out MoveGizmoAxisLayout hitAxis))
        {
            return false;
        }

        _renderer.EndCameraInteraction();
        _renderer.EndLightRotation();
        _isRotatingLight = false;

        _draggedMoveGizmoAxis =
            hitAxis.Axis;

        _hoveredMoveGizmoAxis =
            hitAxis.Axis;

        _moveGizmoDragModel =
            model;

        _moveGizmoDragStartMouse =
            mousePosition;

        _moveGizmoDragStartPosition =
            model.Position;

        _moveGizmoDragScreenDirection =
            hitAxis.ScreenDirection;

        _moveGizmoDragWorldUnitsPerPixel =
            hitAxis.WorldUnitsPerPixel;

        _renderPanel.Cursor =
            Cursors.SizeAll;

        return true;
    }

    private bool UpdateMoveGizmoDrag(
        Point mousePosition)
    {
        if (_draggedMoveGizmoAxis == MoveGizmoAxis.None ||
            _moveGizmoDragModel == null)
        {
            return false;
        }

        Vector2 mouseDelta =
            new(
                mousePosition.X - _moveGizmoDragStartMouse.X,
                mousePosition.Y - _moveGizmoDragStartMouse.Y);

        float pixelDistance =
            Vector2.Dot(
                mouseDelta,
                _moveGizmoDragScreenDirection);

        Vector3 worldAxis =
            GetMoveGizmoWorldAxis(
                _draggedMoveGizmoAxis);

        Vector3 newPosition =
            _moveGizmoDragStartPosition +
            worldAxis *
            pixelDistance *
            _moveGizmoDragWorldUnitsPerPixel;

        if (Vector3.DistanceSquared(
                _moveGizmoDragModel.Position,
                newPosition) <= 0.0000000001f)
        {
            return true;
        }

        _moveGizmoDragModel.Position =
            newPosition;

        ApplySceneModelTransform();

        // FastView normally renders from Application.Idle. During a mouse
        // drag, MouseMove messages can keep the WinForms queue busy, so the
        // idle renderer may not get a chance to present the changed transform.
        // Render one frame explicitly so the model follows the mouse live.
        _renderer.Render();

        // Move and repaint the screen-space gizmo in the same interaction
        // frame. Update() processes the pending WM_PAINT immediately instead
        // of waiting for the message queue to become idle.
        UpdateMoveGizmoOverlay();
        _moveGizmoOverlayForm?.Update();

        return true;
    }

    private bool EndMoveGizmoDrag()
    {
        if (_draggedMoveGizmoAxis == MoveGizmoAxis.None)
        {
            return false;
        }

        _draggedMoveGizmoAxis =
            MoveGizmoAxis.None;

        _moveGizmoDragModel = null;
        _renderPanel.Cursor = Cursors.Default;

        RefreshSceneSidebar();

        return true;
    }

    private void UpdateMoveGizmoHover(
        Point mousePosition)
    {
        if (_draggedMoveGizmoAxis != MoveGizmoAxis.None)
        {
            return;
        }

        MoveGizmoAxis previousAxis =
            _hoveredMoveGizmoAxis;

        _hoveredMoveGizmoAxis =
            TryHitMoveGizmo(
                mousePosition,
                out MoveGizmoAxisLayout hitAxis)
                ? hitAxis.Axis
                : MoveGizmoAxis.None;

        if (_hoveredMoveGizmoAxis != previousAxis)
        {
            _renderPanel.Cursor =
                _hoveredMoveGizmoAxis == MoveGizmoAxis.None
                    ? Cursors.Default
                    : Cursors.SizeAll;
        }
    }

    private void ClearMoveGizmoHover()
    {
        if (_draggedMoveGizmoAxis != MoveGizmoAxis.None)
        {
            return;
        }

        _hoveredMoveGizmoAxis = MoveGizmoAxis.None;
        _renderPanel.Cursor = Cursors.Default;
    }

    private bool TryHitMoveGizmo(
        Point mousePosition,
        out MoveGizmoAxisLayout hitAxis)
    {
        hitAxis = default;

        if (!TryCreateMoveGizmoLayout(
                out MoveGizmoLayout? layout) ||
            layout == null)
        {
            return false;
        }

        float bestDistance =
            MoveGizmoHitRadiusPixels;

        bool found = false;

        PointF mouse =
            new(
                mousePosition.X,
                mousePosition.Y);

        foreach (MoveGizmoAxisLayout axis in
                 layout.Axes)
        {
            PointF hitStart =
                new(
                    layout.Origin.X + axis.ScreenDirection.X * 12.0f,
                    layout.Origin.Y + axis.ScreenDirection.Y * 12.0f);

            float distance =
                DistanceToLineSegment(
                    mouse,
                    hitStart,
                    axis.End);

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                hitAxis = axis;
                found = true;
            }
        }

        return found;
    }

    private static float DistanceToLineSegment(
        PointF point,
        PointF start,
        PointF end)
    {
        Vector2 segment =
            new(
                end.X - start.X,
                end.Y - start.Y);

        float lengthSquared =
            segment.LengthSquared();

        if (lengthSquared <= 0.0001f)
        {
            return Vector2.Distance(
                new Vector2(point.X, point.Y),
                new Vector2(start.X, start.Y));
        }

        Vector2 fromStart =
            new(
                point.X - start.X,
                point.Y - start.Y);

        float amount =
            Math.Clamp(
                Vector2.Dot(fromStart, segment) /
                lengthSquared,
                0.0f,
                1.0f);

        Vector2 closest =
            new Vector2(start.X, start.Y) +
            segment * amount;

        return Vector2.Distance(
            new Vector2(point.X, point.Y),
            closest);
    }

    private static Vector3 GetMoveGizmoWorldAxis(
        MoveGizmoAxis axis)
    {
        return axis switch
        {
            MoveGizmoAxis.X => Vector3.UnitX,
            MoveGizmoAxis.Y => Vector3.UnitY,
            MoveGizmoAxis.Z => Vector3.UnitZ,
            _ => Vector3.Zero
        };
    }

    private static Color GetMoveGizmoAxisColor(
        MoveGizmoAxis axis)
    {
        return axis switch
        {
            MoveGizmoAxis.X =>
                Color.FromArgb(235, 72, 72),

            MoveGizmoAxis.Y =>
                Color.FromArgb(90, 210, 105),

            MoveGizmoAxis.Z =>
                Color.FromArgb(75, 135, 245),

            _ => Color.White
        };
    }
}
