namespace FastViewDX12;

// Borderless fullscreen handling. DXGI's automatic Alt+Enter behavior is
// disabled, so the WinForms shell owns the transition and can restore exactly.
public sealed partial class MainForm
{
    private bool _isFullscreen;
    private Rectangle _windowedBounds;
    private FormWindowState _windowedState;
    private FormBorderStyle _windowedBorderStyle;

    private void ToggleFullscreen()
    {
        SuspendLayout();

        try
        {
            if (!_isFullscreen)
            {
                _windowedState = WindowState;
                _windowedBorderStyle = FormBorderStyle;
                _windowedBounds = WindowState == FormWindowState.Normal
                    ? Bounds
                    : RestoreBounds;

                Rectangle screenBounds =
                    Screen.FromControl(this).Bounds;

                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = screenBounds;
                _isFullscreen = true;
            }
            else
            {
                WindowState = FormWindowState.Normal;
                FormBorderStyle = _windowedBorderStyle;
                Bounds = _windowedBounds;
                WindowState = _windowedState;
                _isFullscreen = false;
            }
        }
        finally
        {
            ResumeLayout(performLayout: true);
            _renderer.Resize();
            PositionSceneSidebarToggle();
        }
    }

    protected override bool ProcessCmdKey(
        ref Message message,
        Keys keyData)
    {
        if (keyData == (Keys.Alt | Keys.Enter))
        {
            ToggleFullscreen();
            return true;
        }

        return base.ProcessCmdKey(
            ref message,
            keyData);
    }
}
