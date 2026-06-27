using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Windows.Forms;

namespace FastViewDX12;

// Screen-space editor overlay for moving, rotating, and scaling the selected
// model in either global or local orientation. The overlay stays outside the
// Direct3D scene, so it never appears in thumbnails or exports.
public sealed partial class MainForm
{
    private const float TransformGizmoLengthPixels =
        86.0f;

    private const float TransformGizmoRingRadiusPixels =
        70.0f;

    private const float TransformGizmoHitRadiusPixels =
        9.0f;

    private const float RotationDegreesPerPixel =
        0.75f;

    private const float RotationSnapDegrees =
        90.0f;

    // Keep rotation rings readable when their real 3D plane is viewed almost
    // edge-on. The ring still indicates its plane, but no longer collapses
    // into an awkward one-pixel line.
    private const float MinimumRotationRingAspect =
        0.38f;

    private const float ScaleExponentPerPixel =
        0.012f;

    private const float UniformScaleHandleSizePixels =
        18.0f;

    private const float MinimumGizmoScale =
        0.001f;

    private const float MaximumGizmoScale =
        1000000.0f;

    private const int RotationRingSegmentCount =
        48;

    private TransformGizmoMode _transformGizmoMode =
        TransformGizmoMode.Move;

    private TransformGizmoMode _draggedTransformGizmoMode =
        TransformGizmoMode.Move;

    private TransformGizmoOrientation _transformGizmoOrientation =
        TransformGizmoOrientation.Global;

    private TransformGizmoOrientation _draggedTransformGizmoOrientation =
        TransformGizmoOrientation.Global;

    private TransformGizmoAxis _hoveredMoveGizmoAxis;

    private TransformGizmoAxis _draggedMoveGizmoAxis;

    private SceneModel? _moveGizmoDragModel;

    private Point _moveGizmoDragStartMouse;

    private Vector3 _moveGizmoDragStartPosition;

    private Matrix4x4 _moveGizmoDragStartLinearTransform;

    private Vector3 _moveGizmoDragWorldAxis;

    private Vector2 _moveGizmoDragScreenDirection;

    private float _moveGizmoDragWorldUnitsPerPixel;

    // Rotation dragging uses an accumulated world-axis delta rather than
    // editing one Euler component directly. The displayed rings are global,
    // so the applied transform must use those same global axes.
    private float _rotationGizmoDragRawDeltaDegrees;

    private float _rotationGizmoDragAppliedDeltaDegrees;

    private float _rotationGizmoDragLastPixelDistance;

    private bool _rotationGizmoDragSnapActive;

    private MoveGizmoOverlayForm? _moveGizmoOverlayForm;

    // Cached layout used by the overlay paint callback. The layout is rebuilt
    // only when the projected origin/axes, viewport, selection, or mode change.
    private TransformGizmoLayout? _moveGizmoOverlayLayout;

    private TransformGizmoAnchor? _moveGizmoOverlayAnchor;

    private TransformGizmoAxis _paintedHoveredMoveGizmoAxis;

    private TransformGizmoAxis _paintedDraggedMoveGizmoAxis;

    private TransformGizmoMode _paintedTransformGizmoMode =
        TransformGizmoMode.Move;

    private TransformGizmoOrientation _paintedTransformGizmoOrientation =
        TransformGizmoOrientation.Global;

    private enum TransformGizmoMode
    {
        Move,
        Rotate,
        Scale
    }

    private enum TransformGizmoOrientation
    {
        Global,
        Local
    }

    private enum TransformGizmoAxis
    {
        None,
        X,
        Y,
        Z,
        Uniform
    }

    private readonly struct TransformGizmoAnchor
    {
        public TransformGizmoAnchor(
            PointF origin,
            Vector3 worldX,
            Vector3 worldY,
            Vector3 worldZ,
            Vector2 projectedX,
            Vector2 projectedY,
            Vector2 projectedZ)
        {
            Origin =
                origin;

            WorldX =
                worldX;

            WorldY =
                worldY;

            WorldZ =
                worldZ;

            ProjectedX =
                projectedX;

            ProjectedY =
                projectedY;

            ProjectedZ =
                projectedZ;
        }

        public PointF Origin { get; }

        public Vector3 WorldX { get; }

        public Vector3 WorldY { get; }

        public Vector3 WorldZ { get; }

        public Vector2 ProjectedX { get; }

        public Vector2 ProjectedY { get; }

        public Vector2 ProjectedZ { get; }
    }

    private readonly struct TransformGizmoAxisLayout
    {
        public TransformGizmoAxisLayout(
            TransformGizmoAxis axis,
            Vector3 worldAxis,
            PointF end,
            Vector2 screenDirection,
            float worldUnitsPerPixel,
            PointF[]? ringPoints = null)
        {
            Axis =
                axis;

            WorldAxis =
                worldAxis;

            End =
                end;

            ScreenDirection =
                screenDirection;

            WorldUnitsPerPixel =
                worldUnitsPerPixel;

            RingPoints =
                ringPoints;
        }

        public TransformGizmoAxis Axis { get; }

        public Vector3 WorldAxis { get; }

        public PointF End { get; }

        public Vector2 ScreenDirection { get; }

        public float WorldUnitsPerPixel { get; }

        public PointF[]? RingPoints { get; }
    }

    private readonly struct TransformGizmoHit
    {
        public TransformGizmoHit(
            TransformGizmoAxis axis,
            Vector3 worldAxis,
            Vector2 dragScreenDirection,
            float worldUnitsPerPixel)
        {
            Axis =
                axis;

            WorldAxis =
                worldAxis;

            DragScreenDirection =
                dragScreenDirection;

            WorldUnitsPerPixel =
                worldUnitsPerPixel;
        }

        public TransformGizmoAxis Axis { get; }

        public Vector3 WorldAxis { get; }

        public Vector2 DragScreenDirection { get; }

        public float WorldUnitsPerPixel { get; }
    }

    private sealed class TransformGizmoLayout
    {
        public required TransformGizmoMode Mode { get; init; }

        public required TransformGizmoOrientation Orientation { get; init; }

        public required PointF Origin { get; init; }

        public required TransformGizmoAxisLayout[] Axes { get; init; }
    }

    /// <summary>
    /// Switches the active transform tool. W, E, and R call this from the
    /// keyboard input handler.
    /// </summary>
    private void SetTransformGizmoMode(
        TransformGizmoMode mode)
    {
        if (_draggedMoveGizmoAxis !=
            TransformGizmoAxis.None ||
            _transformGizmoMode ==
            mode)
        {
            return;
        }

        _transformGizmoMode =
            mode;

        _hoveredMoveGizmoAxis =
            TransformGizmoAxis.None;

        _moveGizmoOverlayAnchor =
            null;

        _moveGizmoOverlayLayout =
            null;

        _renderPanel.Cursor =
            Cursors.Default;

        TransformToolbarStateChanged();
        UpdateMoveGizmoOverlay();
        _moveGizmoOverlayForm?.Update();
    }

    /// <summary>
    /// Switches every transform tool between world axes and the selected
    /// model's current local axes.
    /// </summary>
    private void ToggleTransformGizmoOrientation()
    {
        SetTransformGizmoOrientation(
            _transformGizmoOrientation ==
                TransformGizmoOrientation.Global
                ? TransformGizmoOrientation.Local
                : TransformGizmoOrientation.Global);
    }

    private void SetTransformGizmoOrientation(
        TransformGizmoOrientation orientation)
    {
        if (_draggedMoveGizmoAxis !=
                TransformGizmoAxis.None ||
            _transformGizmoOrientation ==
                orientation)
        {
            return;
        }

        _transformGizmoOrientation =
            orientation;

        _hoveredMoveGizmoAxis =
            TransformGizmoAxis.None;

        _moveGizmoOverlayAnchor =
            null;

        _moveGizmoOverlayLayout =
            null;

        _renderPanel.Cursor =
            Cursors.Default;

        TransformToolbarStateChanged();
        UpdateMoveGizmoOverlay();
        _moveGizmoOverlayForm?.Update();
    }

    // The toolbar patch implements this optional hook. The transform core can
    // be committed and tested independently before any UI is added.
    partial void TransformToolbarStateChanged();

    /// <summary>
    /// Keeps a transparent, click-through overlay window aligned with the
    /// Direct3D render panel.
    /// </summary>
    private void UpdateMoveGizmoOverlay()
    {
        if (WindowState ==
                FormWindowState.Minimized ||
            !_renderPanel.Visible ||
            !TryCreateTransformGizmoAnchor(
                out TransformGizmoAnchor anchor))
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
            overlay.Bounds !=
            renderBounds;

        if (boundsChanged)
        {
            overlay.Bounds =
                renderBounds;
        }

        bool anchorChanged =
            !_moveGizmoOverlayAnchor.HasValue ||
            !TransformGizmoAnchorsMatch(
                _moveGizmoOverlayAnchor.Value,
                anchor);

        bool modeChanged =
            _moveGizmoOverlayLayout == null ||
            _moveGizmoOverlayLayout.Mode !=
                _transformGizmoMode ||
            _moveGizmoOverlayLayout.Orientation !=
                _transformGizmoOrientation;

        bool layoutChanged =
            anchorChanged ||
            modeChanged;

        if (layoutChanged)
        {
            _moveGizmoOverlayAnchor =
                anchor;

            _moveGizmoOverlayLayout =
                CreateTransformGizmoLayout(
                    anchor,
                    _transformGizmoMode,
                    _transformGizmoOrientation);
        }

        if (_moveGizmoOverlayLayout == null ||
            _moveGizmoOverlayLayout.Axes.Length ==
            0)
        {
            HideMoveGizmoOverlay();
            return;
        }

        bool highlightChanged =
            _paintedHoveredMoveGizmoAxis !=
                _hoveredMoveGizmoAxis ||
            _paintedDraggedMoveGizmoAxis !=
                _draggedMoveGizmoAxis ||
            _paintedTransformGizmoMode !=
                _transformGizmoMode ||
            _paintedTransformGizmoOrientation !=
                _transformGizmoOrientation;

        bool wasVisible =
            overlay.Visible;

        _paintedHoveredMoveGizmoAxis =
            _hoveredMoveGizmoAxis;

        _paintedDraggedMoveGizmoAxis =
            _draggedMoveGizmoAxis;

        _paintedTransformGizmoMode =
            _transformGizmoMode;

        _paintedTransformGizmoOrientation =
            _transformGizmoOrientation;

        if (!wasVisible)
        {
            overlay.Show(
                this);
        }

        // Never invalidate continuously from the unbounded Application.Idle
        // render loop. Repaint only when the overlay really changed.
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
        _moveGizmoOverlayAnchor =
            null;

        _moveGizmoOverlayLayout =
            null;

        if (_moveGizmoOverlayForm?.Visible ==
            true)
        {
            _moveGizmoOverlayForm.Hide();
        }
    }

    private void DisposeMoveGizmoOverlay()
    {
        if (_moveGizmoOverlayForm ==
            null)
        {
            return;
        }

        _moveGizmoOverlayForm.Close();
        _moveGizmoOverlayForm.Dispose();
        _moveGizmoOverlayForm =
            null;

        _moveGizmoOverlayAnchor =
            null;

        _moveGizmoOverlayLayout =
            null;
    }

    /// <summary>
    /// Paints the active transform tool into the transparent overlay window.
    /// </summary>
    private void PaintMoveGizmoOverlay(
        Graphics graphics)
    {
        TransformGizmoLayout? layout =
            _moveGizmoOverlayLayout;

        if (layout ==
            null)
        {
            return;
        }

        graphics.SmoothingMode =
            SmoothingMode.AntiAlias;

        foreach (TransformGizmoAxisLayout axis in
                 layout.Axes)
        {
            Color color =
                GetTransformGizmoAxisColor(
                    axis.Axis);

            bool highlighted =
                axis.Axis ==
                    _draggedMoveGizmoAxis ||
                axis.Axis ==
                    _hoveredMoveGizmoAxis;

            if (highlighted)
            {
                color =
                    Color.FromArgb(
                        255,
                        235,
                        90);
            }

            switch (layout.Mode)
            {
                case TransformGizmoMode.Move:
                    DrawMoveGizmoAxis(
                        graphics,
                        layout.Origin,
                        axis,
                        color,
                        highlighted);
                    break;

                case TransformGizmoMode.Rotate:
                    DrawRotateGizmoAxis(
                        graphics,
                        axis,
                        color,
                        highlighted);
                    break;

                case TransformGizmoMode.Scale:
                    DrawScaleGizmoAxis(
                        graphics,
                        layout.Origin,
                        axis,
                        color,
                        highlighted);
                    break;
            }
        }

        if (layout.Mode ==
            TransformGizmoMode.Scale)
        {
            bool uniformHighlighted =
                _draggedMoveGizmoAxis ==
                    TransformGizmoAxis.Uniform ||
                _hoveredMoveGizmoAxis ==
                    TransformGizmoAxis.Uniform;

            DrawUniformScaleHandle(
                graphics,
                layout.Origin,
                uniformHighlighted);
        }
        else
        {
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
    }

    private static void DrawMoveGizmoAxis(
        Graphics graphics,
        PointF origin,
        TransformGizmoAxisLayout axis,
        Color color,
        bool highlighted)
    {
        using var pen =
            CreateTransformGizmoPen(
                color,
                highlighted);

        graphics.DrawLine(
            pen,
            origin,
            axis.End);

        DrawMoveGizmoArrowHead(
            graphics,
            axis,
            color);

        DrawTransformGizmoAxisLabel(
            graphics,
            axis,
            color);
    }

    private static void DrawRotateGizmoAxis(
        Graphics graphics,
        TransformGizmoAxisLayout axis,
        Color color,
        bool highlighted)
    {
        PointF[]? ringPoints =
            axis.RingPoints;

        if (ringPoints == null ||
            ringPoints.Length <
            2)
        {
            return;
        }

        using var pen =
            CreateTransformGizmoPen(
                color,
                highlighted);

        graphics.DrawLines(
            pen,
            ringPoints);
    }

    private static void DrawScaleGizmoAxis(
        Graphics graphics,
        PointF origin,
        TransformGizmoAxisLayout axis,
        Color color,
        bool highlighted)
    {
        using var pen =
            CreateTransformGizmoPen(
                color,
                highlighted);

        graphics.DrawLine(
            pen,
            origin,
            axis.End);

        float handleSize =
            highlighted
                ? 12.0f
                : 10.0f;

        using var brush =
            new SolidBrush(
                color);

        graphics.FillRectangle(
            brush,
            axis.End.X - handleSize * 0.5f,
            axis.End.Y - handleSize * 0.5f,
            handleSize,
            handleSize);

        DrawTransformGizmoAxisLabel(
            graphics,
            axis,
            color);
    }

    private static void DrawUniformScaleHandle(
        Graphics graphics,
        PointF origin,
        bool highlighted)
    {
        float size =
            highlighted
                ? UniformScaleHandleSizePixels +
                  4.0f
                : UniformScaleHandleSizePixels;

        Color fillColor =
            highlighted
                ? Color.FromArgb(
                    255,
                    235,
                    90)
                : Color.White;

        RectangleF handleBounds =
            new(
                origin.X - size * 0.5f,
                origin.Y - size * 0.5f,
                size,
                size);

        using var brush =
            new SolidBrush(
                fillColor);

        using var outlinePen =
            new Pen(
                Color.FromArgb(
                    220,
                    28,
                    28,
                    32),
                2.0f);

        graphics.FillRectangle(
            brush,
            handleBounds);

        graphics.DrawRectangle(
            outlinePen,
            handleBounds.X,
            handleBounds.Y,
            handleBounds.Width,
            handleBounds.Height);
    }

    private static Pen CreateTransformGizmoPen(
        Color color,
        bool highlighted)
    {
        return new Pen(
            color,
            highlighted
                ? 4.0f
                : 3.0f)
        {
            StartCap =
                LineCap.Round,

            EndCap =
                LineCap.Round,

            LineJoin =
                LineJoin.Round
        };
    }

    private static void DrawTransformGizmoAxisLabel(
        Graphics graphics,
        TransformGizmoAxisLayout axis,
        Color color)
    {
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

    private static void DrawMoveGizmoArrowHead(
        Graphics graphics,
        TransformGizmoAxisLayout axis,
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

    private bool TryCreateTransformGizmoAnchor(
        out TransformGizmoAnchor anchor)
    {
        anchor =
            default;

        SceneModel? model =
            _sceneDocument.SelectedModel;

        if (model ==
                null ||
            !_renderer.TryProjectWorldToScreen(
                model.Position,
                out PointF origin))
        {
            return false;
        }

        Vector3 worldAxisX =
            Vector3.UnitX;

        Vector3 worldAxisY =
            Vector3.UnitY;

        Vector3 worldAxisZ =
            Vector3.UnitZ;

        if (_transformGizmoOrientation ==
            TransformGizmoOrientation.Local)
        {
            Matrix4x4 linearTransform =
                model.LinearTransform;

            worldAxisX =
                NormalizeGizmoAxis(
                    Vector3.TransformNormal(
                        Vector3.UnitX,
                        linearTransform),
                    Vector3.UnitX);

            worldAxisY =
                NormalizeGizmoAxis(
                    Vector3.TransformNormal(
                        Vector3.UnitY,
                        linearTransform),
                    Vector3.UnitY);

            worldAxisZ =
                NormalizeGizmoAxis(
                    Vector3.TransformNormal(
                        Vector3.UnitZ,
                        linearTransform),
                    Vector3.UnitZ);
        }

        Vector2 projectedX =
            TryProjectTransformGizmoAxis(
                model.Position,
                origin,
                worldAxisX);

        Vector2 projectedY =
            TryProjectTransformGizmoAxis(
                model.Position,
                origin,
                worldAxisY);

        Vector2 projectedZ =
            TryProjectTransformGizmoAxis(
                model.Position,
                origin,
                worldAxisZ);

        if (projectedX.LengthSquared() < 0.0001f &&
            projectedY.LengthSquared() < 0.0001f &&
            projectedZ.LengthSquared() < 0.0001f)
        {
            return false;
        }

        anchor =
            new TransformGizmoAnchor(
                origin,
                worldAxisX,
                worldAxisY,
                worldAxisZ,
                projectedX,
                projectedY,
                projectedZ);

        return true;
    }

    private Vector2 TryProjectTransformGizmoAxis(
        Vector3 worldOrigin,
        PointF screenOrigin,
        Vector3 worldAxis)
    {
        return _renderer.TryProjectWorldToScreen(
            worldOrigin + worldAxis,
            out PointF projectedPoint)
            ? ToScreenOffset(
                screenOrigin,
                projectedPoint)
            : Vector2.Zero;
    }


    private static Vector3 NormalizeGizmoAxis(
        Vector3 axis,
        Vector3 fallback)
    {
        return axis.LengthSquared() >
            0.00000001f
            ? Vector3.Normalize(
                axis)
            : fallback;
    }

    private static Vector2 ToScreenOffset(
        PointF origin,
        PointF projectedPoint)
    {
        return new Vector2(
            projectedPoint.X - origin.X,
            projectedPoint.Y - origin.Y);
    }

    private static bool TransformGizmoAnchorsMatch(
        TransformGizmoAnchor first,
        TransformGizmoAnchor second)
    {
        const float pointTolerance =
            0.05f;

        const float vectorToleranceSquared =
            0.0001f;

        return
            MathF.Abs(
                first.Origin.X - second.Origin.X) <=
                pointTolerance &&
            MathF.Abs(
                first.Origin.Y - second.Origin.Y) <=
                pointTolerance &&
            Vector2.DistanceSquared(
                first.ProjectedX,
                second.ProjectedX) <=
                vectorToleranceSquared &&
            Vector2.DistanceSquared(
                first.ProjectedY,
                second.ProjectedY) <=
                vectorToleranceSquared &&
            Vector2.DistanceSquared(
                first.ProjectedZ,
                second.ProjectedZ) <=
                vectorToleranceSquared &&
            Vector3.DistanceSquared(
                first.WorldX,
                second.WorldX) <=
                vectorToleranceSquared &&
            Vector3.DistanceSquared(
                first.WorldY,
                second.WorldY) <=
                vectorToleranceSquared &&
            Vector3.DistanceSquared(
                first.WorldZ,
                second.WorldZ) <=
                vectorToleranceSquared;
    }

    private static TransformGizmoLayout CreateTransformGizmoLayout(
        TransformGizmoAnchor anchor,
        TransformGizmoMode mode,
        TransformGizmoOrientation orientation)
    {
        var axes =
            new List<TransformGizmoAxisLayout>(
                3);

        if (mode ==
            TransformGizmoMode.Rotate)
        {
            AddRotateGizmoAxes(
                axes,
                anchor);
        }
        else
        {
            AddLinearGizmoAxis(
                axes,
                anchor.Origin,
                TransformGizmoAxis.X,
                anchor.WorldX,
                anchor.ProjectedX);

            AddLinearGizmoAxis(
                axes,
                anchor.Origin,
                TransformGizmoAxis.Y,
                anchor.WorldY,
                anchor.ProjectedY);

            AddLinearGizmoAxis(
                axes,
                anchor.Origin,
                TransformGizmoAxis.Z,
                anchor.WorldZ,
                anchor.ProjectedZ);
        }

        return new TransformGizmoLayout
        {
            Mode =
                mode,

            Orientation =
                orientation,

            Origin =
                anchor.Origin,

            Axes =
                axes.ToArray()
        };
    }

    private static void AddLinearGizmoAxis(
        List<TransformGizmoAxisLayout> axes,
        PointF screenOrigin,
        TransformGizmoAxis axis,
        Vector3 worldAxis,
        Vector2 projectedDirection)
    {
        float pixelsPerWorldUnit =
            projectedDirection.Length();

        // An axis that points almost directly toward the camera cannot be
        // manipulated reliably in screen space, so omit it for that view.
        if (pixelsPerWorldUnit <
            0.75f)
        {
            return;
        }

        Vector2 screenDirection =
            projectedDirection /
            pixelsPerWorldUnit;

        PointF end =
            new(
                screenOrigin.X +
                    screenDirection.X *
                    TransformGizmoLengthPixels,
                screenOrigin.Y +
                    screenDirection.Y *
                    TransformGizmoLengthPixels);

        axes.Add(
            new TransformGizmoAxisLayout(
                axis,
                worldAxis,
                end,
                screenDirection,
                1.0f /
                    pixelsPerWorldUnit));
    }

    private static void AddRotateGizmoAxes(
        List<TransformGizmoAxisLayout> axes,
        TransformGizmoAnchor anchor)
    {
        float maximumProjectedLength =
            MathF.Max(
                anchor.ProjectedX.Length(),
                MathF.Max(
                    anchor.ProjectedY.Length(),
                    anchor.ProjectedZ.Length()));

        if (maximumProjectedLength <
            0.75f)
        {
            return;
        }

        float radiusScale =
            TransformGizmoRingRadiusPixels /
            maximumProjectedLength;

        AddRotateGizmoAxis(
            axes,
            anchor.Origin,
            TransformGizmoAxis.X,
            anchor.WorldX,
            anchor.ProjectedY * radiusScale,
            anchor.ProjectedZ * radiusScale);

        AddRotateGizmoAxis(
            axes,
            anchor.Origin,
            TransformGizmoAxis.Y,
            anchor.WorldY,
            anchor.ProjectedZ * radiusScale,
            anchor.ProjectedX * radiusScale);

        AddRotateGizmoAxis(
            axes,
            anchor.Origin,
            TransformGizmoAxis.Z,
            anchor.WorldZ,
            anchor.ProjectedX * radiusScale,
            anchor.ProjectedY * radiusScale);
    }

    private static void AddRotateGizmoAxis(
        List<TransformGizmoAxisLayout> axes,
        PointF origin,
        TransformGizmoAxis axis,
        Vector3 worldAxis,
        Vector2 screenBasisA,
        Vector2 screenBasisB)
    {
        if (screenBasisA.LengthSquared() +
                screenBasisB.LengthSquared() <
            1.0f)
        {
            return;
        }

        // Projecting a real 3D circle can make it collapse into an almost
        // invisible line. Derive the ellipse orientation from the projected
        // plane, but clamp its minor radius for a stable editor-style gizmo.
        float covarianceXX =
            screenBasisA.X * screenBasisA.X +
            screenBasisB.X * screenBasisB.X;

        float covarianceXY =
            screenBasisA.X * screenBasisA.Y +
            screenBasisB.X * screenBasisB.Y;

        float covarianceYY =
            screenBasisA.Y * screenBasisA.Y +
            screenBasisB.Y * screenBasisB.Y;

        float trace =
            covarianceXX +
            covarianceYY;

        float determinant =
            covarianceXX * covarianceYY -
            covarianceXY * covarianceXY;

        float discriminant =
            MathF.Sqrt(
                MathF.Max(
                    0.0f,
                    trace * trace -
                    4.0f * determinant));

        float majorEigenvalue =
            MathF.Max(
                0.0f,
                (trace + discriminant) *
                0.5f);

        float minorEigenvalue =
            MathF.Max(
                0.0f,
                (trace - discriminant) *
                0.5f);

        float measuredMajorRadius =
            MathF.Sqrt(
                majorEigenvalue);

        if (measuredMajorRadius <
            0.5f)
        {
            return;
        }

        float measuredMinorRadius =
            MathF.Sqrt(
                minorEigenvalue);

        float ellipseAngle =
            0.5f *
            MathF.Atan2(
                2.0f * covarianceXY,
                covarianceXX - covarianceYY);

        Vector2 majorDirection =
            new(
                MathF.Cos(
                    ellipseAngle),
                MathF.Sin(
                    ellipseAngle));

        // Covariance gives the ellipse shape but discards which side of the
        // ring plane faces the camera. Preserve the projected basis handedness
        // so positive rotation never reverses merely because the camera moves
        // to the opposite side of the model.
        float projectedHandedness =
            screenBasisA.X * screenBasisB.Y -
            screenBasisA.Y * screenBasisB.X;

        Vector2 minorDirection =
            projectedHandedness < 0.0f
                ? new Vector2(
                    majorDirection.Y,
                    -majorDirection.X)
                : new Vector2(
                    -majorDirection.Y,
                    majorDirection.X);

        float minorAspect =
            Math.Clamp(
                measuredMinorRadius /
                measuredMajorRadius,
                MinimumRotationRingAspect,
                1.0f);

        float majorRadius =
            TransformGizmoRingRadiusPixels;

        float minorRadius =
            majorRadius *
            minorAspect;

        var points =
            new PointF[
                RotationRingSegmentCount + 1];

        for (int index = 0;
             index <= RotationRingSegmentCount;
             index++)
        {
            float angle =
                MathF.PI * 2.0f *
                index /
                RotationRingSegmentCount;

            Vector2 offset =
                majorDirection *
                    majorRadius *
                    MathF.Cos(
                        angle) +
                minorDirection *
                    minorRadius *
                    MathF.Sin(
                        angle);

            points[index] =
                new PointF(
                    origin.X + offset.X,
                    origin.Y + offset.Y);
        }

        axes.Add(
            new TransformGizmoAxisLayout(
                axis,
                worldAxis,
                points[0],
                Vector2.Zero,
                0.0f,
                points));
    }

    private bool TryBeginMoveGizmoDrag(
        Point mousePosition)
    {
        SceneModel? model =
            _sceneDocument.SelectedModel;

        if (model ==
                null ||
            !TryHitTransformGizmo(
                mousePosition,
                out TransformGizmoHit hit))
        {
            return false;
        }

        BeginTransformUndo(
            model,
            $"{_transformGizmoMode} model");

        _renderer.EndCameraInteraction();
        _renderer.EndLightRotation();
        _isRotatingLight =
            false;

        _draggedMoveGizmoAxis =
            hit.Axis;

        _hoveredMoveGizmoAxis =
            hit.Axis;

        _draggedTransformGizmoMode =
            _transformGizmoMode;

        _draggedTransformGizmoOrientation =
            _transformGizmoOrientation;

        _moveGizmoDragModel =
            model;

        _moveGizmoDragStartMouse =
            mousePosition;

        _moveGizmoDragStartPosition =
            model.Position;

        _moveGizmoDragStartLinearTransform =
            model.LinearTransform;

        _moveGizmoDragWorldAxis =
            hit.WorldAxis;

        _moveGizmoDragScreenDirection =
            hit.DragScreenDirection;

        _moveGizmoDragWorldUnitsPerPixel =
            hit.WorldUnitsPerPixel;

        _rotationGizmoDragRawDeltaDegrees =
            0.0f;

        _rotationGizmoDragAppliedDeltaDegrees =
            0.0f;

        _rotationGizmoDragLastPixelDistance =
            0.0f;

        _rotationGizmoDragSnapActive =
            (ModifierKeys & Keys.Shift) ==
            Keys.Shift;

        _renderPanel.Cursor =
            Cursors.SizeAll;

        UpdateMoveGizmoOverlay();
        _moveGizmoOverlayForm?.Update();

        return true;
    }

    private bool UpdateMoveGizmoDrag(
        Point mousePosition)
    {
        if (_draggedMoveGizmoAxis ==
                TransformGizmoAxis.None ||
            _moveGizmoDragModel ==
                null)
        {
            return false;
        }

        Vector2 mouseDelta =
            new(
                mousePosition.X -
                    _moveGizmoDragStartMouse.X,
                mousePosition.Y -
                    _moveGizmoDragStartMouse.Y);

        float pixelDistance =
            Vector2.Dot(
                mouseDelta,
                _moveGizmoDragScreenDirection);

        bool changed =
            _draggedTransformGizmoMode switch
            {
                TransformGizmoMode.Move =>
                    ApplyMoveGizmoDrag(
                        pixelDistance),

                TransformGizmoMode.Rotate =>
                    ApplyRotateGizmoDrag(
                        pixelDistance),

                TransformGizmoMode.Scale =>
                    ApplyScaleGizmoDrag(
                        pixelDistance),

                _ =>
                    false
            };

        if (!changed)
        {
            return true;
        }

        ApplySceneModelTransform();

        // FastView normally renders from Application.Idle. MouseMove can keep
        // the queue busy during a drag, so present one frame explicitly.
        _renderer.Render();

        UpdateMoveGizmoOverlay();
        _moveGizmoOverlayForm?.Update();

        return true;
    }

    private bool ApplyMoveGizmoDrag(
        float pixelDistance)
    {
        if (_moveGizmoDragModel ==
            null)
        {
            return false;
        }

        Vector3 newPosition =
            _moveGizmoDragStartPosition +
            _moveGizmoDragWorldAxis *
            pixelDistance *
            _moveGizmoDragWorldUnitsPerPixel;

        if (Vector3.DistanceSquared(
                _moveGizmoDragModel.Position,
                newPosition) <=
            0.0000000001f)
        {
            return false;
        }

        _moveGizmoDragModel.Position =
            newPosition;

        return true;
    }

    private bool ApplyRotateGizmoDrag(
        float pixelDistance)
    {
        if (_moveGizmoDragModel ==
            null)
        {
            return false;
        }

        bool snapToRightAngles =
            (ModifierKeys & Keys.Shift) ==
            Keys.Shift;

        // Rebase when Shift is pressed or released. Without this, releasing
        // Shift jumps back to the unsnapped mouse position accumulated since
        // the beginning of the drag.
        if (_rotationGizmoDragSnapActive !=
            snapToRightAngles)
        {
            _rotationGizmoDragRawDeltaDegrees =
                _rotationGizmoDragAppliedDeltaDegrees;

            _rotationGizmoDragSnapActive =
                snapToRightAngles;
        }

        float incrementalPixelDistance =
            pixelDistance -
            _rotationGizmoDragLastPixelDistance;

        _rotationGizmoDragLastPixelDistance =
            pixelDistance;

        _rotationGizmoDragRawDeltaDegrees +=
            incrementalPixelDistance *
            RotationDegreesPerPixel;

        float appliedDeltaDegrees =
            snapToRightAngles
                ? MathF.Round(
                    _rotationGizmoDragRawDeltaDegrees /
                    RotationSnapDegrees,
                    MidpointRounding.AwayFromZero) *
                    RotationSnapDegrees
                : _rotationGizmoDragRawDeltaDegrees;

        if (MathF.Abs(
                appliedDeltaDegrees -
                _rotationGizmoDragAppliedDeltaDegrees) <=
            0.000001f)
        {
            return false;
        }

        _rotationGizmoDragAppliedDeltaDegrees =
            appliedDeltaDegrees;

        Matrix4x4 axisDelta =
            CreateTransformGizmoAxisRotation(
                _draggedMoveGizmoAxis,
                appliedDeltaDegrees);

        // System.Numerics composes row-vector transforms from left to right.
        // A local delta acts before the model's existing linear transform;
        // a global delta acts after it.
        Matrix4x4 combinedLinearTransform =
            _draggedTransformGizmoOrientation ==
                TransformGizmoOrientation.Local
                ? axisDelta *
                  _moveGizmoDragStartLinearTransform
                : _moveGizmoDragStartLinearTransform *
                  axisDelta;

        if (LinearTransformsNearlyEqual(
                _moveGizmoDragModel.LinearTransform,
                combinedLinearTransform))
        {
            return false;
        }

        _moveGizmoDragModel.SetLinearTransform(
            combinedLinearTransform);

        return true;
    }

    private bool ApplyScaleGizmoDrag(
        float pixelDistance)
    {
        if (_moveGizmoDragModel ==
            null)
        {
            return false;
        }

        float scaleFactor =
            Math.Clamp(
                MathF.Exp(
                    pixelDistance *
                    ScaleExponentPerPixel),
                MinimumGizmoScale,
                MaximumGizmoScale);

        Vector3 axisScale =
            Vector3.One;

        if (_draggedMoveGizmoAxis ==
            TransformGizmoAxis.Uniform)
        {
            axisScale =
                new Vector3(
                    scaleFactor);
        }
        else
        {
            SetVectorAxis(
                ref axisScale,
                _draggedMoveGizmoAxis,
                scaleFactor);
        }

        Matrix4x4 scaleDelta =
            Matrix4x4.CreateScale(
                axisScale);

        Matrix4x4 combinedLinearTransform;

        if (_draggedMoveGizmoAxis ==
            TransformGizmoAxis.Uniform)
        {
            combinedLinearTransform =
                scaleDelta *
                _moveGizmoDragStartLinearTransform;
        }
        else
        {
            combinedLinearTransform =
                _draggedTransformGizmoOrientation ==
                    TransformGizmoOrientation.Local
                    ? scaleDelta *
                      _moveGizmoDragStartLinearTransform
                    : _moveGizmoDragStartLinearTransform *
                      scaleDelta;
        }

        if (LinearTransformsNearlyEqual(
                _moveGizmoDragModel.LinearTransform,
                combinedLinearTransform))
        {
            return false;
        }

        _moveGizmoDragModel.SetLinearTransform(
            combinedLinearTransform);

        return true;
    }

    private static bool LinearTransformsNearlyEqual(
        Matrix4x4 first,
        Matrix4x4 second)
    {
        const float tolerance =
            0.000001f;

        return
            MathF.Abs(first.M11 - second.M11) <= tolerance &&
            MathF.Abs(first.M12 - second.M12) <= tolerance &&
            MathF.Abs(first.M13 - second.M13) <= tolerance &&
            MathF.Abs(first.M21 - second.M21) <= tolerance &&
            MathF.Abs(first.M22 - second.M22) <= tolerance &&
            MathF.Abs(first.M23 - second.M23) <= tolerance &&
            MathF.Abs(first.M31 - second.M31) <= tolerance &&
            MathF.Abs(first.M32 - second.M32) <= tolerance &&
            MathF.Abs(first.M33 - second.M33) <= tolerance;
    }

    private bool EndMoveGizmoDrag()
    {
        if (_draggedMoveGizmoAxis ==
            TransformGizmoAxis.None)
        {
            return false;
        }

        CommitTransformUndo();

        _draggedMoveGizmoAxis =
            TransformGizmoAxis.None;

        _moveGizmoDragModel =
            null;

        _renderPanel.Cursor =
            Cursors.Default;

        RefreshSceneSidebar();
        UpdateMoveGizmoOverlay();
        _moveGizmoOverlayForm?.Update();

        return true;
    }

    private void UpdateMoveGizmoHover(
        Point mousePosition)
    {
        if (_draggedMoveGizmoAxis !=
            TransformGizmoAxis.None)
        {
            return;
        }

        TransformGizmoAxis previousAxis =
            _hoveredMoveGizmoAxis;

        _hoveredMoveGizmoAxis =
            TryHitTransformGizmo(
                mousePosition,
                out TransformGizmoHit hit)
                ? hit.Axis
                : TransformGizmoAxis.None;

        if (_hoveredMoveGizmoAxis !=
            previousAxis)
        {
            _renderPanel.Cursor =
                _hoveredMoveGizmoAxis ==
                    TransformGizmoAxis.None
                    ? Cursors.Default
                    : Cursors.SizeAll;

            UpdateMoveGizmoOverlay();
            _moveGizmoOverlayForm?.Update();
        }
    }

    private void ClearMoveGizmoHover()
    {
        if (_draggedMoveGizmoAxis !=
            TransformGizmoAxis.None)
        {
            return;
        }

        bool changed =
            _hoveredMoveGizmoAxis !=
            TransformGizmoAxis.None;

        _hoveredMoveGizmoAxis =
            TransformGizmoAxis.None;

        _renderPanel.Cursor =
            Cursors.Default;

        if (changed)
        {
            UpdateMoveGizmoOverlay();
            _moveGizmoOverlayForm?.Update();
        }
    }

    private bool TryHitTransformGizmo(
        Point mousePosition,
        out TransformGizmoHit hit)
    {
        hit =
            default;

        if (!TryGetCurrentTransformGizmoLayout(
                out TransformGizmoLayout? layout) ||
            layout ==
                null)
        {
            return false;
        }

        PointF mouse =
            new(
                mousePosition.X,
                mousePosition.Y);

        float bestDistance =
            TransformGizmoHitRadiusPixels;

        bool found =
            false;

        if (layout.Mode ==
            TransformGizmoMode.Scale)
        {
            float uniformHitRadius =
                UniformScaleHandleSizePixels *
                    0.5f +
                4.0f;

            if (Distance(
                    mouse,
                    layout.Origin) <=
                uniformHitRadius)
            {
                hit =
                    new TransformGizmoHit(
                        TransformGizmoAxis.Uniform,
                        Vector3.Zero,
                        Vector2.Normalize(
                            new Vector2(
                                1.0f,
                                -1.0f)),
                        0.0f);

                return true;
            }
        }

        foreach (TransformGizmoAxisLayout axis in
                 layout.Axes)
        {
            if (layout.Mode ==
                TransformGizmoMode.Rotate)
            {
                if (!TryHitRotateGizmoAxis(
                        mouse,
                        axis,
                        ref bestDistance,
                        out Vector2 tangent))
                {
                    continue;
                }

                hit =
                    new TransformGizmoHit(
                        axis.Axis,
                        axis.WorldAxis,
                        tangent,
                        0.0f);

                found =
                    true;

                continue;
            }

            PointF hitStart =
                new(
                    layout.Origin.X +
                        axis.ScreenDirection.X *
                        12.0f,
                    layout.Origin.Y +
                        axis.ScreenDirection.Y *
                        12.0f);

            float distance =
                DistanceToLineSegment(
                    mouse,
                    hitStart,
                    axis.End);

            if (layout.Mode ==
                TransformGizmoMode.Scale)
            {
                distance =
                    MathF.Min(
                        distance,
                        Distance(
                            mouse,
                            axis.End));
            }

            if (distance >
                bestDistance)
            {
                continue;
            }

            bestDistance =
                distance;

            hit =
                new TransformGizmoHit(
                    axis.Axis,
                    axis.WorldAxis,
                    axis.ScreenDirection,
                    axis.WorldUnitsPerPixel);

            found =
                true;
        }

        return found;
    }

    private bool TryGetCurrentTransformGizmoLayout(
        out TransformGizmoLayout? layout)
    {
        if (!TryCreateTransformGizmoAnchor(
                out TransformGizmoAnchor anchor))
        {
            layout =
                null;

            return false;
        }

        bool rebuild =
            !_moveGizmoOverlayAnchor.HasValue ||
            !TransformGizmoAnchorsMatch(
                _moveGizmoOverlayAnchor.Value,
                anchor) ||
            _moveGizmoOverlayLayout ==
                null ||
            _moveGizmoOverlayLayout.Mode !=
                _transformGizmoMode ||
            _moveGizmoOverlayLayout.Orientation !=
                _transformGizmoOrientation;

        if (rebuild)
        {
            _moveGizmoOverlayAnchor =
                anchor;

            _moveGizmoOverlayLayout =
                CreateTransformGizmoLayout(
                    anchor,
                    _transformGizmoMode,
                    _transformGizmoOrientation);
        }

        layout =
            _moveGizmoOverlayLayout;

        return layout !=
            null &&
            layout.Axes.Length >
            0;
    }

    private static bool TryHitRotateGizmoAxis(
        PointF mouse,
        TransformGizmoAxisLayout axis,
        ref float bestDistance,
        out Vector2 tangent)
    {
        tangent =
            Vector2.Zero;

        PointF[]? ringPoints =
            axis.RingPoints;

        if (ringPoints ==
                null ||
            ringPoints.Length <
                2)
        {
            return false;
        }

        bool found =
            false;

        for (int index = 0;
             index + 1 < ringPoints.Length;
             index++)
        {
            PointF start =
                ringPoints[index];

            PointF end =
                ringPoints[index + 1];

            float distance =
                DistanceToLineSegment(
                    mouse,
                    start,
                    end);

            if (distance >
                bestDistance)
            {
                continue;
            }

            Vector2 segment =
                new(
                    end.X - start.X,
                    end.Y - start.Y);

            if (segment.LengthSquared() <
                0.0001f)
            {
                continue;
            }

            bestDistance =
                distance;

            tangent =
                Vector2.Normalize(
                    segment);

            found =
                true;
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

        if (lengthSquared <=
            0.0001f)
        {
            return Distance(
                point,
                start);
        }

        Vector2 fromStart =
            new(
                point.X - start.X,
                point.Y - start.Y);

        float amount =
            Math.Clamp(
                Vector2.Dot(
                    fromStart,
                    segment) /
                lengthSquared,
                0.0f,
                1.0f);

        Vector2 closest =
            new Vector2(
                start.X,
                start.Y) +
            segment *
            amount;

        return Vector2.Distance(
            new Vector2(
                point.X,
                point.Y),
            closest);
    }

    private static float Distance(
        PointF first,
        PointF second)
    {
        float deltaX =
            first.X - second.X;

        float deltaY =
            first.Y - second.Y;

        return MathF.Sqrt(
            deltaX * deltaX +
            deltaY * deltaY);
    }

    private static Vector3 GetTransformGizmoWorldAxis(
        TransformGizmoAxis axis)
    {
        return axis switch
        {
            TransformGizmoAxis.X =>
                Vector3.UnitX,

            TransformGizmoAxis.Y =>
                Vector3.UnitY,

            TransformGizmoAxis.Z =>
                Vector3.UnitZ,

            _ =>
                Vector3.Zero
        };
    }

    private static float GetVectorAxis(
        Vector3 value,
        TransformGizmoAxis axis)
    {
        return axis switch
        {
            TransformGizmoAxis.X =>
                value.X,

            TransformGizmoAxis.Y =>
                value.Y,

            TransformGizmoAxis.Z =>
                value.Z,

            _ =>
                0.0f
        };
    }

    private static void SetVectorAxis(
        ref Vector3 value,
        TransformGizmoAxis axis,
        float axisValue)
    {
        switch (axis)
        {
            case TransformGizmoAxis.X:
                value.X =
                    axisValue;
                break;

            case TransformGizmoAxis.Y:
                value.Y =
                    axisValue;
                break;

            case TransformGizmoAxis.Z:
                value.Z =
                    axisValue;
                break;
        }
    }

    private static Matrix4x4 CreateEulerRotationMatrix(
        Vector3 rotationDegrees)
    {
        const float degreesToRadians =
            MathF.PI / 180.0f;

        return
            Matrix4x4.CreateRotationX(
                rotationDegrees.X *
                degreesToRadians) *
            Matrix4x4.CreateRotationY(
                rotationDegrees.Y *
                degreesToRadians) *
            Matrix4x4.CreateRotationZ(
                rotationDegrees.Z *
                degreesToRadians);
    }

    private static Matrix4x4 CreateTransformGizmoAxisRotation(
        TransformGizmoAxis axis,
        float degrees)
    {
        float radians =
            degrees *
            (MathF.PI / 180.0f);

        return axis switch
        {
            TransformGizmoAxis.X =>
                Matrix4x4.CreateRotationX(
                    radians),

            TransformGizmoAxis.Y =>
                Matrix4x4.CreateRotationY(
                    radians),

            TransformGizmoAxis.Z =>
                Matrix4x4.CreateRotationZ(
                    radians),

            _ =>
                Matrix4x4.Identity
        };
    }

    private static Vector3 ExtractEulerRotationDegrees(
        Matrix4x4 rotation,
        Vector3 referenceDegrees)
    {
        const float radiansToDegrees =
            180.0f / MathF.PI;

        float sinY =
            Math.Clamp(
                -rotation.M13,
                -1.0f,
                1.0f);

        float y =
            MathF.Asin(
                sinY);

        float cosY =
            MathF.Cos(
                y);

        float x;
        float z;

        if (MathF.Abs(cosY) >
            0.00001f)
        {
            x =
                MathF.Atan2(
                    rotation.M23,
                    rotation.M33);

            z =
                MathF.Atan2(
                    rotation.M12,
                    rotation.M11);
        }
        else if (sinY >=
            0.0f)
        {
            // At +90 degrees, only X-Z is observable. Preserve the previous Z
            // value so the inspector does not jump needlessly at gimbal lock.
            z =
                referenceDegrees.Z /
                radiansToDegrees;

            float xMinusZ =
                MathF.Atan2(
                    rotation.M21,
                    rotation.M22);

            x =
                xMinusZ +
                z;
        }
        else
        {
            // At -90 degrees, only X+Z is observable. Again retain the
            // previous Z value to keep the Euler representation continuous.
            z =
                referenceDegrees.Z /
                radiansToDegrees;

            float xPlusZ =
                MathF.Atan2(
                    -rotation.M21,
                    rotation.M22);

            x =
                xPlusZ -
                z;
        }

        Vector3 primary =
            new(
                x * radiansToDegrees,
                y * radiansToDegrees,
                z * radiansToDegrees);

        primary =
            UnwrapEulerNear(
                primary,
                referenceDegrees);

        if (MathF.Abs(cosY) <=
            0.00001f)
        {
            return primary;
        }

        // XYZ Euler angles have a second equivalent solution. Choose whichever
        // representation is nearest the currently displayed inspector values.
        Vector3 alternate =
            new(
                primary.X + 180.0f,
                180.0f - primary.Y,
                primary.Z + 180.0f);

        alternate =
            UnwrapEulerNear(
                alternate,
                referenceDegrees);

        float primaryDistance =
            Vector3.DistanceSquared(
                primary,
                referenceDegrees);

        float alternateDistance =
            Vector3.DistanceSquared(
                alternate,
                referenceDegrees);

        return alternateDistance <
            primaryDistance
                ? alternate
                : primary;
    }

    private static Vector3 UnwrapEulerNear(
        Vector3 value,
        Vector3 reference)
    {
        value.X =
            UnwrapDegreesNear(
                value.X,
                reference.X);

        value.Y =
            UnwrapDegreesNear(
                value.Y,
                reference.Y);

        value.Z =
            UnwrapDegreesNear(
                value.Z,
                reference.Z);

        return value;
    }

    private static float UnwrapDegreesNear(
        float degrees,
        float referenceDegrees)
    {
        while (degrees -
               referenceDegrees >
               180.0f)
        {
            degrees -=
                360.0f;
        }

        while (degrees -
               referenceDegrees <
               -180.0f)
        {
            degrees +=
                360.0f;
        }

        return degrees;
    }

    private static float NormalizeDegrees(
        float degrees)
    {
        float normalized =
            degrees %
            360.0f;

        if (normalized >
            180.0f)
        {
            normalized -=
                360.0f;
        }
        else if (normalized <
                 -180.0f)
        {
            normalized +=
                360.0f;
        }

        return normalized;
    }

    private static Color GetTransformGizmoAxisColor(
        TransformGizmoAxis axis)
    {
        return axis switch
        {
            TransformGizmoAxis.X =>
                Color.FromArgb(
                    235,
                    72,
                    72),

            TransformGizmoAxis.Y =>
                Color.FromArgb(
                    90,
                    210,
                    105),

            TransformGizmoAxis.Z =>
                Color.FromArgb(
                    75,
                    135,
                    245),

            TransformGizmoAxis.Uniform =>
                Color.White,

            _ =>
                Color.White
        };
    }
}
