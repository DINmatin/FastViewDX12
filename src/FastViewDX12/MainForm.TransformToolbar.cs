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

    private TransformToolbarButton? _frontViewButton;

    private TransformToolbarButton? _rightViewButton;

    private TransformToolbarButton? _leftViewButton;

    private TransformToolbarButton? _topViewButton;

    private TransformToolbarButton? _bottomViewButton;

    private TransformToolbarButton? _backViewButton;

    private TransformToolbarButton? _shadowsButton;

    private TransformToolbarButton? _bloomButton;

    private ToolTip? _transformToolbarToolTip;

    private enum TransformToolbarIcon
    {
        Move,
        Rotate,
        Scale,
        Orientation,
        FrontView,
        RightView,
        LeftView,
        TopView,
        BottomView,
        BackView,
        Shadows,
        Bloom
    }

    private enum CameraPresetView
    {
        Front,
        Right,
        Left,
        Top,
        Bottom,
        Back
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
                        TransformToolbarButtonSize * 12 +
                        TransformToolbarButtonGap * 10 +
                        20),

                Visible =
                    false
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

        _frontViewButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.FrontView,
                "Front view");

        _rightViewButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.RightView,
                "Right view");

        _leftViewButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.LeftView,
                "Left view");

        _topViewButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.TopView,
                "Top view");

        _bottomViewButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.BottomView,
                "Bottom view");

        _backViewButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.BackView,
                "Back view");

        _shadowsButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.Shadows,
                "Toggle directional shadows");

        _bloomButton =
            CreateTransformToolbarButton(
                TransformToolbarIcon.Bloom,
                "Toggle bloom");

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

        _frontViewButton.Click +=
            (_, _) =>
            {
                ApplyCameraPresetView(
                    CameraPresetView.Front);
            };

        _rightViewButton.Click +=
            (_, _) =>
            {
                ApplyCameraPresetView(
                    CameraPresetView.Right);
            };

        _leftViewButton.Click +=
            (_, _) =>
            {
                ApplyCameraPresetView(
                    CameraPresetView.Left);
            };

        _topViewButton.Click +=
            (_, _) =>
            {
                ApplyCameraPresetView(
                    CameraPresetView.Top);
            };

        _bottomViewButton.Click +=
            (_, _) =>
            {
                ApplyCameraPresetView(
                    CameraPresetView.Bottom);
            };

        _backViewButton.Click +=
            (_, _) =>
            {
                ApplyCameraPresetView(
                    CameraPresetView.Back);
            };

        _shadowsButton.Click +=
            (_, _) =>
            {
                if (_shadowsEnabledMenuItem != null)
                {
                    _shadowsEnabledMenuItem.Checked =
                        !_shadowsEnabledMenuItem.Checked;
                }
            };

        _bloomButton.Click +=
            (_, _) =>
            {
                if (_bloomEnabledMenuItem != null)
                {
                    _bloomEnabledMenuItem.Checked =
                        !_bloomEnabledMenuItem.Checked;
                }
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

        AddTransformToolbarButton(
            _frontViewButton,
            0,
            secondGroup: true);

        AddTransformToolbarButton(
            _rightViewButton,
            1,
            secondGroup: true);

        AddTransformToolbarButton(
            _leftViewButton,
            2,
            secondGroup: true);

        AddTransformToolbarButton(
            _topViewButton,
            3,
            secondGroup: true);

        AddTransformToolbarButton(
            _bottomViewButton,
            4,
            secondGroup: true);

        AddTransformToolbarButton(
            _backViewButton,
            5,
            secondGroup: true);

        AddTransformToolbarButton(
            _shadowsButton,
            0,
            thirdGroup: true);

        AddTransformToolbarButton(
            _bloomButton,
            1,
            thirdGroup: true);

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

        _transformToolbarToolTip.SetToolTip(
            _frontViewButton,
            "Front view");

        _transformToolbarToolTip.SetToolTip(
            _rightViewButton,
            "Right view");

        _transformToolbarToolTip.SetToolTip(
            _leftViewButton,
            "Left view");

        _transformToolbarToolTip.SetToolTip(
            _topViewButton,
            "Top view");

        _transformToolbarToolTip.SetToolTip(
            _bottomViewButton,
            "Bottom view");

        _transformToolbarToolTip.SetToolTip(
            _backViewButton,
            "Back view");

        _transformToolbarToolTip.SetToolTip(
            _shadowsButton,
            "Directional shadows on / off");

        _transformToolbarToolTip.SetToolTip(
            _bloomButton,
            "Bloom on / off");

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
        int index,
        bool secondGroup = false,
        bool thirdGroup = false)
    {
        if (_transformToolbar ==
            null)
        {
            return;
        }

        int y =
            index *
            (TransformToolbarButtonSize +
             TransformToolbarButtonGap);

        if (secondGroup)
        {
            y +=
                TransformToolbarButtonSize * 4 +
                TransformToolbarButtonGap * 4 +
                10;
        }

        if (thirdGroup)
        {
            y +=
                TransformToolbarButtonSize * 10 +
                TransformToolbarButtonGap * 9 +
                20;
        }

        button.Location =
            new Point(
                0,
                y);

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

        if (_shadowsButton != null)
        {
            _shadowsButton.Selected =
                _viewerSettings.ShadowsEnabled;
        }

        if (_bloomButton != null)
        {
            _bloomButton.Selected =
                _viewerSettings.BloomEnabled;
        }

        _moveTransformButton.Invalidate();
        _rotateTransformButton.Invalidate();
        _scaleTransformButton.Invalidate();
        _orientationTransformButton.Invalidate();
        _shadowsButton?.Invalidate();
        _bloomButton?.Invalidate();
    }

    partial void TransformToolbarStateChanged()
    {
        UpdateTransformToolbarState();
    }

    private void ApplyCameraPresetView(
        CameraPresetView preset)
    {
        EndMoveGizmoDrag();
        _renderer.EndCameraInteraction();
        _renderer.EndLightRotation();
        _isRotatingLight = false;
        _renderPanel.Capture = false;

        (float yawDegrees, float pitchDegrees) =
            preset switch
            {
                CameraPresetView.Front => (0.0f, 0.0f),
                CameraPresetView.Right => (90.0f, 0.0f),
                CameraPresetView.Left => (-90.0f, 0.0f),
                CameraPresetView.Top => (0.0f, 90.0f),
                CameraPresetView.Bottom => (0.0f, -90.0f),
                CameraPresetView.Back => (180.0f, 0.0f),
                _ => (0.0f, 0.0f)
            };

        _renderer.SetCameraOrbitDegrees(
            yawDegrees,
            pitchDegrees);

        _renderer.Render();
        UpdateMoveGizmoOverlay();
        _moveGizmoOverlayForm?.Update();
        _renderPanel.Focus();
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

                case TransformToolbarIcon.FrontView:
                    DrawViewIcon(
                        graphics,
                        iconPen,
                        iconBrush,
                        iconBounds,
                        "FR");
                    break;

                case TransformToolbarIcon.RightView:
                    DrawViewIcon(
                        graphics,
                        iconPen,
                        iconBrush,
                        iconBounds,
                        "R");
                    break;

                case TransformToolbarIcon.LeftView:
                    DrawViewIcon(
                        graphics,
                        iconPen,
                        iconBrush,
                        iconBounds,
                        "L");
                    break;

                case TransformToolbarIcon.TopView:
                    DrawViewIcon(
                        graphics,
                        iconPen,
                        iconBrush,
                        iconBounds,
                        "T");
                    break;

                case TransformToolbarIcon.BottomView:
                    DrawViewIcon(
                        graphics,
                        iconPen,
                        iconBrush,
                        iconBounds,
                        "D");
                    break;

                case TransformToolbarIcon.BackView:
                    DrawViewIcon(
                        graphics,
                        iconPen,
                        iconBrush,
                        iconBounds,
                        "BK");
                    break;

                case TransformToolbarIcon.Shadows:
                    DrawShadowsIcon(
                        graphics,
                        iconPen,
                        iconBrush,
                        iconBounds);
                    break;

                case TransformToolbarIcon.Bloom:
                    DrawBloomIcon(
                        graphics,
                        iconPen,
                        iconBrush,
                        iconBounds);
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

        private static void DrawViewIcon(
            Graphics graphics,
            Pen pen,
            Brush brush,
            Rectangle bounds,
            string label)
        {
            Rectangle cubeBounds =
                Rectangle.Inflate(
                    bounds,
                    -4,
                    -4);

            PointF frontTopLeft =
                new(
                    cubeBounds.Left + 3,
                    cubeBounds.Top + 7);

            PointF frontTopRight =
                new(
                    cubeBounds.Right - 9,
                    cubeBounds.Top + 7);

            PointF frontBottomRight =
                new(
                    cubeBounds.Right - 9,
                    cubeBounds.Bottom - 5);

            PointF frontBottomLeft =
                new(
                    cubeBounds.Left + 3,
                    cubeBounds.Bottom - 5);

            PointF depth =
                new(
                    6.0f,
                    -4.0f);

            PointF backTopLeft =
                new(
                    frontTopLeft.X + depth.X,
                    frontTopLeft.Y + depth.Y);

            PointF backTopRight =
                new(
                    frontTopRight.X + depth.X,
                    frontTopRight.Y + depth.Y);

            PointF backBottomRight =
                new(
                    frontBottomRight.X + depth.X,
                    frontBottomRight.Y + depth.Y);

            graphics.DrawPolygon(
                pen,
                [
                    frontTopLeft,
                    frontTopRight,
                    frontBottomRight,
                    frontBottomLeft
                ]);

            graphics.DrawPolygon(
                pen,
                [
                    backTopLeft,
                    backTopRight,
                    backBottomRight,
                    new PointF(
                        frontBottomLeft.X + depth.X,
                        frontBottomLeft.Y + depth.Y)
                ]);

            graphics.DrawLine(
                pen,
                frontTopLeft,
                backTopLeft);

            graphics.DrawLine(
                pen,
                frontTopRight,
                backTopRight);

            graphics.DrawLine(
                pen,
                frontBottomRight,
                backBottomRight);

            Font baseFont =
                SystemFonts.MessageBoxFont ??
                Control.DefaultFont;

            using var labelFont =
                new Font(
                    baseFont.FontFamily,
                    label.Length > 1
                        ? 6.0f
                        : 8.0f,
                    FontStyle.Bold,
                    GraphicsUnit.Point);

            using var labelBrush =
                new SolidBrush(
                    pen.Color);

            SizeF labelSize =
                graphics.MeasureString(
                    label,
                    labelFont);

            float labelX =
                frontTopLeft.X +
                ((frontTopRight.X - frontTopLeft.X) - labelSize.Width) * 0.5f;

            float labelY =
                frontTopLeft.Y +
                ((frontBottomLeft.Y - frontTopLeft.Y) - labelSize.Height) * 0.5f - 0.5f;

            graphics.DrawString(
                label,
                labelFont,
                labelBrush,
                labelX,
                labelY);
        }

        private static void DrawShadowsIcon(
            Graphics graphics,
            Pen pen,
            Brush brush,
            Rectangle bounds)
        {
            PointF lightCenter =
                new(
                    bounds.Left + 6.0f,
                    bounds.Top + 6.0f);

            graphics.FillEllipse(
                brush,
                lightCenter.X - 3.0f,
                lightCenter.Y - 3.0f,
                6.0f,
                6.0f);

            for (int index = 0;
                 index < 8;
                 index++)
            {
                float angle =
                    index * MathF.PI * 0.25f;

                PointF rayStart =
                    new(
                        lightCenter.X + MathF.Cos(angle) * 5.0f,
                        lightCenter.Y + MathF.Sin(angle) * 5.0f);

                PointF rayEnd =
                    new(
                        lightCenter.X + MathF.Cos(angle) * 8.0f,
                        lightCenter.Y + MathF.Sin(angle) * 8.0f);

                graphics.DrawLine(
                    pen,
                    rayStart,
                    rayEnd);
            }

            RectangleF objectBounds =
                new(
                    bounds.Left + 10.0f,
                    bounds.Top + 11.0f,
                    7.0f,
                    9.0f);

            graphics.DrawRectangle(
                pen,
                objectBounds.X,
                objectBounds.Y,
                objectBounds.Width,
                objectBounds.Height);

            PointF shadowTop =
                new(
                    objectBounds.Right + 2.0f,
                    objectBounds.Top + 3.0f);

            PointF shadowBottom =
                new(
                    bounds.Right - 1.0f,
                    bounds.Bottom - 2.0f);

            using var shadowBrush =
                new SolidBrush(
                    Color.FromArgb(
                        150,
                        pen.Color));

            graphics.FillPolygon(
                shadowBrush,
                [
                    shadowTop,
                    new PointF(
                        objectBounds.Right + 2.0f,
                        objectBounds.Bottom),
                    shadowBottom,
                    new PointF(
                        shadowBottom.X,
                        shadowTop.Y + 5.0f)
                ]);
        }

        private static void DrawBloomIcon(
            Graphics graphics,
            Pen pen,
            Brush brush,
            Rectangle bounds)
        {
            PointF center =
                new(
                    bounds.Left + bounds.Width * 0.5f,
                    bounds.Top + bounds.Height * 0.5f);

            float innerRadius =
                MathF.Min(
                    bounds.Width,
                    bounds.Height) * 0.18f;

            graphics.FillEllipse(
                brush,
                center.X - innerRadius,
                center.Y - innerRadius,
                innerRadius * 2.0f,
                innerRadius * 2.0f);

            for (int index = 0;
                 index < 8;
                 index++)
            {
                float angle =
                    index * MathF.PI * 0.25f;

                float inner =
                    innerRadius + 2.0f;

                float outer =
                    innerRadius +
                    (index % 2 == 0
                        ? 8.0f
                        : 5.5f);

                PointF start =
                    new(
                        center.X + MathF.Cos(angle) * inner,
                        center.Y + MathF.Sin(angle) * inner);

                PointF end =
                    new(
                        center.X + MathF.Cos(angle) * outer,
                        center.Y + MathF.Sin(angle) * outer);

                graphics.DrawLine(
                    pen,
                    start,
                    end);
            }

            using var glowPen =
                new Pen(
                    Color.FromArgb(
                        110,
                        pen.Color),
                    1.0f);

            float glowRadius =
                innerRadius + 7.0f;

            graphics.DrawEllipse(
                glowPen,
                center.X - glowRadius,
                center.Y - glowRadius,
                glowRadius * 2.0f,
                glowRadius * 2.0f);
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
