using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FastViewDX12;

// Compact viewport toolbar for the transform modes and their orientation.
// Icons are drawn at runtime, so the public repository needs no image assets.
public sealed partial class MainForm
{
    private const int TransformToolbarButtonSize =
        36;

    private const int TransformToolbarButtonGap =
        3;

    private Panel? _transformToolbar;

    private TransformToolbarButton? _moveTransformButton;

    private TransformToolbarButton? _rotateTransformButton;

    private TransformToolbarButton? _scaleTransformButton;

    private TransformToolbarButton? _orientationTransformButton;

    private ToolTip? _transformToolbarToolTip;

    private enum TransformToolbarIcon
    {
        Move,
        Rotate,
        Scale,
        Orientation
    }

    private void InitializeTransformToolbar()
    {
        if (_transformToolbar !=
            null)
        {
            return;
        }

        _transformToolbar =
            new Panel
            {
                BackColor =
                    Color.FromArgb(
                        28,
                        28,
                        34),

                Size =
                    new Size(
                        TransformToolbarButtonSize,
                        TransformToolbarButtonSize * 4 +
                        TransformToolbarButtonGap * 3)
            };

        _moveTransformButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.Move,
                "Move (W)");

        _rotateTransformButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.Rotate,
                "Rotate (E)");

        _scaleTransformButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.Scale,
                "Scale (R)");

        _orientationTransformButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.Orientation,
                "Toggle Local / Global orientation (L)");

        _moveTransformButton.Click +=
            (_, _) =>
            {
                SetTransformGizmoMode(
                    TransformGizmoMode.Move);
            };

        _rotateTransformButton.Click +=
            (_, _) =>
            {
                SetTransformGizmoMode(
                    TransformGizmoMode.Rotate);
            };

        _scaleTransformButton.Click +=
            (_, _) =>
            {
                SetTransformGizmoMode(
                    TransformGizmoMode.Scale);
            };

        _orientationTransformButton.Click +=
            (_, _) =>
            {
                ToggleTransformGizmoOrientation();
            };

        AddTransformToolbarButton(
            _moveTransformButton,
            0);

        AddTransformToolbarButton(
            _rotateTransformButton,
            1);

        AddTransformToolbarButton(
            _scaleTransformButton,
            2);

        AddTransformToolbarButton(
            _orientationTransformButton,
            3);

        _transformToolbarToolTip =
            new ToolTip
            {
                AutomaticDelay =
                    250,

                AutoPopDelay =
                    6000,

                ReshowDelay =
                    100,

                ShowAlways =
                    true
            };

        _transformToolbarToolTip.SetToolTip(
            _moveTransformButton,
            "Move (W)");

        _transformToolbarToolTip.SetToolTip(
            _rotateTransformButton,
            "Rotate (E)");

        _transformToolbarToolTip.SetToolTip(
            _scaleTransformButton,
            "Scale (R)");

        _transformToolbarToolTip.SetToolTip(
            _orientationTransformButton,
            "Local / Global orientation (L)");

        _transformToolbar.Disposed +=
            (_, _) =>
            {
                _transformToolbarToolTip?.Dispose();
                _transformToolbarToolTip =
                    null;
            };

        _renderPanel.Controls.Add(
            _transformToolbar);

        PositionTransformToolbar();
        UpdateTransformToolbarState();
        _transformToolbar.BringToFront();
    }

    private TransformToolbarButton CreateTransformToolbarButton(
        TransformToolbarIcon icon,
        string accessibleName)
    {
        return new TransformToolbarButton(
            icon)
        {
            AccessibleName =
                accessibleName,

            Location =
                Point.Empty,

            Size =
                new Size(
                    TransformToolbarButtonSize,
                    TransformToolbarButtonSize),

            TabStop =
                false
        };
    }

    private void AddTransformToolbarButton(
        TransformToolbarButton button,
        int index)
    {
        if (_transformToolbar ==
            null)
        {
            return;
        }

        button.Location =
            new Point(
                0,
                index *
                (TransformToolbarButtonSize +
                 TransformToolbarButtonGap));

        _transformToolbar.Controls.Add(
            button);
    }

    private void PositionTransformToolbar()
    {
        if (_transformToolbar ==
                null ||
            _sceneSidebarToggleButton ==
                null)
        {
            return;
        }

        _transformToolbar.Location =
            new Point(
                _sceneSidebarToggleButton.Left,
                _sceneSidebarToggleButton.Bottom +
                8);
    }

    private void UpdateTransformToolbarState()
    {
        if (_moveTransformButton ==
            null)
        {
            return;
        }

        _moveTransformButton.Selected =
            _transformGizmoMode ==
            TransformGizmoMode.Move;

        _rotateTransformButton!.Selected =
            _transformGizmoMode ==
            TransformGizmoMode.Rotate;

        _scaleTransformButton!.Selected =
            _transformGizmoMode ==
            TransformGizmoMode.Scale;

        bool localOrientation =
            _transformGizmoOrientation ==
            TransformGizmoOrientation.Local;

        _orientationTransformButton!.LocalOrientation =
            localOrientation;

        _orientationTransformButton.Selected =
            localOrientation;

        _moveTransformButton.Invalidate();
        _rotateTransformButton.Invalidate();
        _scaleTransformButton.Invalidate();
        _orientationTransformButton.Invalidate();
    }

    partial void TransformToolbarStateChanged()
    {
        UpdateTransformToolbarState();
    }

    private sealed class TransformToolbarButton : Button
    {
        private readonly TransformToolbarIcon _icon;

        private bool _hovered;

        public TransformToolbarButton(
            TransformToolbarIcon icon)
        {
            _icon =
                icon;

            DoubleBuffered =
                true;

            FlatStyle =
                FlatStyle.Flat;

            FlatAppearance.BorderSize =
                0;

            BackColor =
                Color.FromArgb(
                    46,
                    46,
                    54);

            ForeColor =
                Color.Gainsboro;

            Margin =
                Padding.Empty;

            Padding =
                Padding.Empty;

            UseVisualStyleBackColor =
                false;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public bool Selected { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public bool LocalOrientation { get; set; }

        protected override void OnMouseEnter(
            EventArgs e)
        {
            _hovered =
                true;

            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(
            EventArgs e)
        {
            _hovered =
                false;

            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(
            PaintEventArgs e)
        {
            Graphics graphics =
                e.Graphics;

            graphics.SmoothingMode =
                SmoothingMode.AntiAlias;

            Color background =
                Selected
                    ? Color.FromArgb(
                        74,
                        74,
                        88)
                    : _hovered
                        ? Color.FromArgb(
                            60,
                            60,
                            70)
                        : Color.FromArgb(
                            46,
                            46,
                            54);

            graphics.Clear(
                background);

            Color iconColor =
                Selected
                    ? Color.FromArgb(
                        255,
                        235,
                        90)
                    : Color.Gainsboro;

            using var iconPen =
                new Pen(
                    iconColor,
                    2.0f)
                {
                    StartCap =
                        LineCap.Round,

                    EndCap =
                        LineCap.Round,

                    LineJoin =
                        LineJoin.Round
                };

            using var iconBrush =
                new SolidBrush(
                    iconColor);

            Rectangle iconBounds =
                new(
                    6,
                    6,
                    Width - 12,
                    Height - 12);

            switch (_icon)
            {
                case TransformToolbarIcon.Move:
                    DrawMoveIcon(
                        graphics,
                        iconPen,
                        iconBrush,
                        iconBounds);
                    break;

                case TransformToolbarIcon.Rotate:
                    DrawRotateIcon(
                        graphics,
                        iconPen,
                        iconBrush,
                        iconBounds);
                    break;

                case TransformToolbarIcon.Scale:
                    DrawScaleIcon(
                        graphics,
                        iconPen,
                        iconBrush,
                        iconBounds);
                    break;

                case TransformToolbarIcon.Orientation:
                    DrawOrientationIcon(
                        graphics,
                        iconPen,
                        iconBounds,
                        LocalOrientation);
                    break;
            }

            if (Selected)
            {
                using var selectedPen =
                    new Pen(
                        Color.FromArgb(
                            255,
                            235,
                            90),
                        1.0f);

                graphics.DrawRectangle(
                    selectedPen,
                    0,
                    0,
                    Width - 1,
                    Height - 1);
            }
        }

        private static void DrawMoveIcon(
            Graphics graphics,
            Pen pen,
            Brush brush,
            Rectangle bounds)
        {
            Point center =
                new(
                    bounds.Left +
                    bounds.Width / 2,
                    bounds.Top +
                    bounds.Height / 2);

            Point top =
                new(
                    center.X,
                    bounds.Top + 1);

            Point right =
                new(
                    bounds.Right - 1,
                    center.Y);

            Point lowerLeft =
                new(
                    bounds.Left + 2,
                    bounds.Bottom - 2);

            graphics.DrawLine(
                pen,
                center,
                top);

            graphics.DrawLine(
                pen,
                center,
                right);

            graphics.DrawLine(
                pen,
                center,
                lowerLeft);

            DrawArrowHead(
                graphics,
                brush,
                top,
                new PointF(
                    0.0f,
                    -1.0f));

            DrawArrowHead(
                graphics,
                brush,
                right,
                new PointF(
                    1.0f,
                    0.0f));

            DrawArrowHead(
                graphics,
                brush,
                lowerLeft,
                new PointF(
                    -0.7f,
                    0.7f));

            graphics.FillEllipse(
                brush,
                center.X - 2,
                center.Y - 2,
                4,
                4);
        }

        private static void DrawRotateIcon(
            Graphics graphics,
            Pen pen,
            Brush brush,
            Rectangle bounds)
        {
            Rectangle arcBounds =
                Rectangle.Inflate(
                    bounds,
                    -2,
                    -2);

            graphics.DrawArc(
                pen,
                arcBounds,
                35.0f,
                285.0f);

            float angle =
                35.0f *
                MathF.PI /
                180.0f;

            PointF tip =
                new(
                    arcBounds.Left +
                    arcBounds.Width *
                    0.5f +
                    MathF.Cos(angle) *
                    arcBounds.Width *
                    0.5f,
                    arcBounds.Top +
                    arcBounds.Height *
                    0.5f +
                    MathF.Sin(angle) *
                    arcBounds.Height *
                    0.5f);

            DrawArrowHead(
                graphics,
                brush,
                tip,
                new PointF(
                    -0.75f,
                    0.65f));
        }

        private static void DrawScaleIcon(
            Graphics graphics,
            Pen pen,
            Brush brush,
            Rectangle bounds)
        {
            Point start =
                new(
                    bounds.Left + 4,
                    bounds.Bottom - 4);

            Point end =
                new(
                    bounds.Right - 4,
                    bounds.Top + 4);

            graphics.DrawLine(
                pen,
                start,
                end);

            graphics.FillRectangle(
                brush,
                start.X - 3,
                start.Y - 3,
                6,
                6);

            graphics.FillRectangle(
                brush,
                end.X - 4,
                end.Y - 4,
                8,
                8);
        }

        private static void DrawOrientationIcon(
            Graphics graphics,
            Pen pen,
            Rectangle bounds,
            bool local)
        {
            Rectangle globeBounds =
                Rectangle.Inflate(
                    bounds,
                    -3,
                    -3);

            if (local)
            {
                PointF top =
                    new(
                        globeBounds.Left +
                        globeBounds.Width * 0.5f,
                        globeBounds.Top);

                PointF right =
                    new(
                        globeBounds.Right,
                        globeBounds.Top +
                        globeBounds.Height * 0.42f);

                PointF bottom =
                    new(
                        globeBounds.Left +
                        globeBounds.Width * 0.5f,
                        globeBounds.Bottom);

                PointF left =
                    new(
                        globeBounds.Left,
                        globeBounds.Top +
                        globeBounds.Height * 0.42f);

                graphics.DrawPolygon(
                    pen,
                    [
                        top,
                        right,
                        bottom,
                        left
                    ]);

                graphics.DrawLine(
                    pen,
                    top,
                    new PointF(
                        top.X,
                        bottom.Y));

                graphics.DrawLine(
                    pen,
                    left,
                    new PointF(
                        top.X,
                        globeBounds.Top +
                        globeBounds.Height * 0.62f));

                graphics.DrawLine(
                    pen,
                    right,
                    new PointF(
                        top.X,
                        globeBounds.Top +
                        globeBounds.Height * 0.62f));
            }
            else
            {
                graphics.DrawEllipse(
                    pen,
                    globeBounds);

                graphics.DrawArc(
                    pen,
                    Rectangle.Inflate(
                        globeBounds,
                        -6,
                        0),
                    90.0f,
                    180.0f);

                graphics.DrawArc(
                    pen,
                    Rectangle.Inflate(
                        globeBounds,
                        -6,
                        0),
                    270.0f,
                    180.0f);

                graphics.DrawLine(
                    pen,
                    globeBounds.Left + 2,
                    globeBounds.Top +
                        globeBounds.Height / 2,
                    globeBounds.Right - 2,
                    globeBounds.Top +
                        globeBounds.Height / 2);
            }

            Font baseFont =
                SystemFonts.MessageBoxFont ??
                Control.DefaultFont;

            using var labelFont =
                new Font(
                    baseFont.FontFamily,
                    7.0f,
                    FontStyle.Bold,
                    GraphicsUnit.Point);

            using var labelBrush =
                new SolidBrush(
                    pen.Color);

            graphics.DrawString(
                local
                    ? "L"
                    : "G",
                labelFont,
                labelBrush,
                bounds.Right - 7,
                bounds.Bottom - 8);
        }

        private static void DrawArrowHead(
            Graphics graphics,
            Brush brush,
            PointF tip,
            PointF direction)
        {
            float length =
                MathF.Sqrt(
                    direction.X * direction.X +
                    direction.Y * direction.Y);

            if (length <
                0.0001f)
            {
                return;
            }

            float directionX =
                direction.X /
                length;

            float directionY =
                direction.Y /
                length;

            float perpendicularX =
                -directionY;

            float perpendicularY =
                directionX;

            PointF baseCenter =
                new(
                    tip.X - directionX * 6.0f,
                    tip.Y - directionY * 6.0f);

            graphics.FillPolygon(
                brush,
                [
                    tip,
                    new PointF(
                        baseCenter.X + perpendicularX * 3.0f,
                        baseCenter.Y + perpendicularY * 3.0f),
                    new PointF(
                        baseCenter.X - perpendicularX * 3.0f,
                        baseCenter.Y - perpendicularY * 3.0f)
                ]);
        }
    }
}
