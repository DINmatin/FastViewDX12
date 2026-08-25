using System.Globalization;
using System.ComponentModel;

namespace FastViewDX12;

/// <summary>
/// Compact editor-style number field. Dragging horizontally scrubs the value;
/// double-clicking switches to precise keyboard entry.
/// </summary>
internal sealed class ScrubbableNumberBox : TextBox
{
    private const float MinimumValue = -1000000.0f;
    private const float MaximumValue = 1000000.0f;

    private float _value;
    private float _dragStartValue;
    private float _editStartValue;
    private int _dragStartScreenX;
    private bool _dragging;
    private bool _editing;
    private bool _interactionActive;

    public ScrubbableNumberBox()
    {
        BackColor = Color.FromArgb(31, 31, 37);
        BorderStyle = BorderStyle.FixedSingle;
        ForeColor = Color.White;
        ReadOnly = true;
        ShortcutsEnabled = true;
        TextAlign = HorizontalAlignment.Right;
        Cursor = Cursors.SizeWE;
    }

    public event EventHandler? ValueChanged;
    public event EventHandler? InteractionStarted;
    public event EventHandler? InteractionCompleted;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float DragSensitivity { get; set; } = 0.01f;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Value
    {
        get => _value;
        set => SetValue(value, raiseEvent: false);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left &&
            e.Clicks >= 2)
        {
            BeginTextEditing();
            base.OnMouseDown(e);
            return;
        }

        if (e.Button == MouseButtons.Left &&
            !_editing)
        {
            BeginInteraction();
            _dragging = true;
            _dragStartScreenX = Cursor.Position.X;
            _dragStartValue = _value;
            Capture = true;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            int delta = Cursor.Position.X - _dragStartScreenX;
            float sensitivity = DragSensitivity;

            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                sensitivity *= 10.0f;
            }
            else if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                sensitivity *= 0.1f;
            }

            SetValue(
                _dragStartValue + delta * sensitivity,
                raiseEvent: true);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left &&
            _dragging)
        {
            _dragging = false;
            Capture = false;
            EndInteraction();
        }

        base.OnMouseUp(e);
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        BeginTextEditing();
        base.OnDoubleClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_editing && e.KeyCode == Keys.Enter)
        {
            FinishTextEditing(commit: true);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (_editing && e.KeyCode == Keys.Escape)
        {
            SetValue(_editStartValue, raiseEvent: true);
            FinishTextEditing(commit: false);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnLeave(EventArgs e)
    {
        if (_editing)
        {
            FinishTextEditing(commit: true);
        }

        base.OnLeave(e);
    }

    private void BeginTextEditing()
    {
        if (_editing)
        {
            return;
        }

        _dragging = false;
        Capture = false;
        BeginInteraction();
        _editing = true;
        _editStartValue = _value;
        ReadOnly = false;
        Cursor = Cursors.IBeam;
        BackColor = Color.FromArgb(43, 43, 51);
        Focus();
        SelectAll();
    }

    private void FinishTextEditing(bool commit)
    {
        if (!_editing)
        {
            return;
        }

        if (commit &&
            float.TryParse(
                Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out float parsed))
        {
            SetValue(parsed, raiseEvent: true);
        }
        else
        {
            UpdateText();
        }

        _editing = false;
        ReadOnly = true;
        Cursor = Cursors.SizeWE;
        BackColor = Color.FromArgb(31, 31, 37);
        EndInteraction();
    }

    private void BeginInteraction()
    {
        if (_interactionActive)
        {
            return;
        }

        _interactionActive = true;
        InteractionStarted?.Invoke(this, EventArgs.Empty);
    }

    private void EndInteraction()
    {
        if (!_interactionActive)
        {
            return;
        }

        _interactionActive = false;
        InteractionCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void SetValue(float value, bool raiseEvent)
    {
        if (!float.IsFinite(value))
        {
            return;
        }

        float clamped = Math.Clamp(value, MinimumValue, MaximumValue);
        bool changed = MathF.Abs(clamped - _value) > 0.000001f;
        _value = clamped;
        UpdateText();

        if (changed && raiseEvent)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateText()
    {
        Text = _value.ToString("0.###", CultureInfo.CurrentCulture);
        SelectionStart = TextLength;
    }
}
