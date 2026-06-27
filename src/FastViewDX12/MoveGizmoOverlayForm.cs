using System;
using System.Drawing;
using System.Windows.Forms;

namespace FastViewDX12;

/// <summary>
/// Transparent, click-through overlay window used for viewport editor graphics.
/// Keeping the overlay in its own HWND makes it visible above a DXGI
/// flip-discard swap chain without changing thumbnail or scene rendering.
/// </summary>
internal sealed class MoveGizmoOverlayForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static readonly Color TransparentColor =
        Color.FromArgb(1, 2, 3);

    private readonly Action<Graphics> _paintOverlay;

    public MoveGizmoOverlayForm(
        Action<Graphics> paintOverlay)
    {
        _paintOverlay =
            paintOverlay ??
            throw new ArgumentNullException(
                nameof(paintOverlay));

        FormBorderStyle =
            FormBorderStyle.None;

        ShowInTaskbar =
            false;

        StartPosition =
            FormStartPosition.Manual;

        BackColor =
            TransparentColor;

        TransparencyKey =
            TransparentColor;

        DoubleBuffered =
            true;
    }

    protected override bool ShowWithoutActivation =>
        true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters =
                base.CreateParams;

            parameters.ExStyle |=
                WsExTransparent |
                WsExToolWindow |
                WsExNoActivate;

            return parameters;
        }
    }

    protected override void OnPaint(
        PaintEventArgs e)
    {
        base.OnPaint(
            e);

        _paintOverlay(
            e.Graphics);
    }

    protected override void OnPaintBackground(
        PaintEventArgs e)
    {
        e.Graphics.Clear(
            TransparentColor);
    }
}
