using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Windows.Forms;

namespace FastViewDX12;

// Screen-space editor overlay for moving and rotating the selected model on
// global axes, and scaling it on its local model axes. The overlay stays
// outside the Direct3D scene, so it never appears in thumbnails or exports.
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

    // Keep rotation rings readable when their real 3D plane is viewed almost
    // edge-on. The ring still indicates its plane, but no longer collapses
    // into an awkward one-pixel line.
    private const float MinimumRotationRingAspect =
        0.38f;

    private const float ScaleExponentPerPixel =
        0.012f;

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

    private TransformGizmoAxis _hoveredMoveGizmoAxis;

    private TransformGizmoAxis _draggedMoveGizmoAxis;

    private SceneModel? _moveGizmoDragModel;

    private Point _moveGizmoDragStartMouse;

    private Vector3 _moveGizmoDragStartPosition;

    private Vector3 _moveGizmoDragStartRotationDegrees;

    private Vector3 _moveGizmoDragStartScale;

    private Vector2 _moveGizmoDragScreenDirection;

    private float _moveGizmoDragWorldUnitsPerPixel;

    private MoveGizmoOverlayForm? _moveGizmoOverlayForm;

    // Cached layout used by the overlay paint callback. The layout is rebuilt
    // only when the projected origin/axes, viewport, selection, or mode change.
    private TransformGizmoLayout? _moveGizmoOverlayLayout;

    private TransformGizmoAnchor? _moveGizmoOverlayAnchor;

    private TransformGizmoAxis _paintedHoveredMoveGizmoAxis;

    private TransformGizmoAxis _paintedDraggedMoveGizmoAxis;

    private TransformGizmoMode _paintedTransformGizmoMode =
        TransformGizmoMode.Move;

    private enum TransformGizmoMode
    {
        Move,
        Rotate,
        Scale
    }

    private enum TransformGizmoAxis
    {
        None,
        X,
        Y,
        Z
    }

    private readonly struct TransformGizmoAnchor
    {
        public TransformGizmoAnchor(
            PointF origin,
            Vector2 projectedX,
            Vector2 projectedY,
            Vector2 projectedZ)
        {
            Origin =
                origin;

            ProjectedX =
                projectedX;

            ProjectedY =
                projectedY;

            ProjectedZ =
                projectedZ;
        }

        public PointF Origin { get; }

        public Vector2 ProjectedX { get; }

        public Vector2 ProjectedY { get; }

        public Vector2 ProjectedZ { get; }
    }

    private readonly struct TransformGizmoAxisLayout
    {
        public TransformGizmoAxisLayout(
            TransformGizmoAxis axis,
            PointF end,
            Vector2 screenDirection,
            float worldUnitsPerPixel,
            PointF[]? ringPoints = null)
        {
            Axis =
                axis;

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

        public PointF End { get; }

        public Vector2 ScreenDirection { get; }

        public float WorldUnitsPerPixel { get; }

        public PointF[]? RingPoints { get; }
    }

    private readonly struct TransformGizmoHit
    {
        public TransformGizmoHit(
            TransformGizmoAxis axis,
            Vector2 dragScreenDirection,
            float worldUnitsPerPixel)
        {
            Axis =
                axis;

            DragScreenDirection =
                dragScreenDirection;

            WorldUnitsPerPixel =
                worldUnitsPerPixel;
        }

        public TransformGizmoAxis Axis { get; }

        public Vector2 DragScreenDirection { get; }

        public float WorldUnitsPerPixel { get; }
    }

    private sealed class TransformGizmoLayout
    {
        public required TransformGizmoMode Mode { get; init; }

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

        UpdateMoveGizmoOverlay();
        _moveGizmoOverlayForm?.Update();
    }

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
            _transformGizmoMode;

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
                    _transformGizmoMode);
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
                _transformGizmoMode;

        bool wasVisible =
            overlay.Visible;

        _paintedHoveredMoveGizmoAxis =
            _hoveredMoveGizmoAxis;

        _paintedDraggedMoveGizmoAxis =
            _draggedMoveGizmoAxis;

        _paintedTransformGizmoMode =
            _transformGizmoMode;

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

        // SceneModel.Scale is stored in model-local XYZ. A global-looking
        // scale gizmo therefore lies about the operation once the model has
        // been rotated. Rotate only the scale handles with the model so each
        // displayed handle controls the matching Scale.X/Y/Z component.
        if (_transformGizmoMode ==
            TransformGizmoMode.Scale)
        {
            Matrix4x4 modelTransform =
                model.CreateTransform();

            worldAxisX =
                NormalizeGizmoAxis(
                    Vector3.TransformNormal(
                        Vector3.UnitX,
                        modelTransform),
                    Vector3.UnitX);

            worldAxisY =
                NormalizeGizmoAxis(
                    Vector3.TransformNormal(
                        Vector3.UnitY,
                        modelTransform),
                    Vector3.UnitY);

            worldAxisZ =
                NormalizeGizmoAxis(
                    Vector3.TransformNormal(
                        Vector3.UnitZ,
                        modelTransform),
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
                vectorToleranceSquared;
    }

    private static TransformGizmoLayout CreateTransformGizmoLayout(
        TransformGizmoAnchor anchor,
        TransformGizmoMode mode)
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
                anchor.ProjectedX);

            AddLinearGizmoAxis(
                axes,
                anchor.Origin,
                TransformGizmoAxis.Y,
                anchor.ProjectedY);

            AddLinearGizmoAxis(
                axes,
                anchor.Origin,
                TransformGizmoAxis.Z,
                anchor.ProjectedZ);
        }

        return new TransformGizmoLayout
        {
            Mode =
                mode,

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
            anchor.ProjectedY * radiusScale,
            anchor.ProjectedZ * radiusScale);

        AddRotateGizmoAxis(
            axes,
            anchor.Origin,
            TransformGizmoAxis.Y,
            anchor.ProjectedZ * radiusScale,
            anchor.ProjectedX * radiusScale);

        AddRotateGizmoAxis(
            axes,
            anchor.Origin,
            TransformGizmoAxis.Z,
            anchor.ProjectedX * radiusScale,
            anchor.ProjectedY * radiusScale);
    }

    private static void AddRotateGizmoAxis(
        List<TransformGizmoAxisLayout> axes,
        PointF origin,
        TransformGizmoAxis axis,
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

        Vector2 minorDirection =
            new(
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

        _moveGizmoDragModel =
            model;

        _moveGizmoDragStartMouse =
            mousePosition;

        _moveGizmoDragStartPosition =
            model.Position;

        _moveGizmoDragStartRotationDegrees =
            model.RotationDegrees;

        _moveGizmoDragStartScale =
            model.Scale;

        _moveGizmoDragScreenDirection =
            hit.DragScreenDirection;

        _moveGizmoDragWorldUnitsPerPixel =
            hit.WorldUnitsPerPixel;

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

        Vector3 worldAxis =
            GetTransformGizmoWorldAxis(
                _draggedMoveGizmoAxis);

        Vector3 newPosition =
            _moveGizmoDragStartPosition +
            worldAxis *
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

        float deltaDegrees =
            pixelDistance *
            RotationDegreesPerPixel;

        Vector3 newRotation =
            _moveGizmoDragStartRotationDegrees;

        SetVectorAxis(
            ref newRotation,
            _draggedMoveGizmoAxis,
            NormalizeDegrees(
                GetVectorAxis(
                    _moveGizmoDragStartRotationDegrees,
                    _draggedMoveGizmoAxis) +
                deltaDegrees));

        if (Vector3.DistanceSquared(
                _moveGizmoDragModel.RotationDegrees,
                newRotation) <=
            0.0000000001f)
        {
            return false;
        }

        _moveGizmoDragModel.RotationDegrees =
            newRotation;

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

        float startValue =
            Math.Clamp(
                GetVectorAxis(
                    _moveGizmoDragStartScale,
                    _draggedMoveGizmoAxis),
                MinimumGizmoScale,
                MaximumGizmoScale);

        float scaleFactor =
            MathF.Exp(
                pixelDistance *
                ScaleExponentPerPixel);

        float newValue =
            Math.Clamp(
                startValue *
                scaleFactor,
                MinimumGizmoScale,
                MaximumGizmoScale);

        Vector3 newScale =
            _moveGizmoDragStartScale;

        SetVectorAxis(
            ref newScale,
            _draggedMoveGizmoAxis,
            newValue);

        if (Vector3.DistanceSquared(
                _moveGizmoDragModel.Scale,
                newScale) <=
            0.0000000001f)
        {
            return false;
        }

        _moveGizmoDragModel.Scale =
            newScale;

        return true;
    }

    private bool EndMoveGizmoDrag()
    {
        if (_draggedMoveGizmoAxis ==
            TransformGizmoAxis.None)
        {
            return false;
        }

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
                _transformGizmoMode;

        if (rebuild)
        {
            _moveGizmoOverlayAnchor =
                anchor;

            _moveGizmoOverlayLayout =
                CreateTransformGizmoLayout(
                    anchor,
                    _transformGizmoMode);
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

            _ =>
                Color.White
        };
    }
}
