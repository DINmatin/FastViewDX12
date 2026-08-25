namespace FastViewDX12;

public sealed class DropFeedbackOverlay : UserControl
{
    private readonly Label _messageLabel;
    private readonly Label _fileLabel;

    public DropFeedbackOverlay()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(28, 30, 34);
        AllowDrop = true;
        Visible = false;

        _messageLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 46.0f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };

        _fileLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 90,
            Padding = new Padding(32, 0, 32, 28),
            Font = new Font("Segoe UI", 15.0f, FontStyle.Regular),
            ForeColor = Color.Gainsboro,
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            BackColor = Color.Transparent
        };

        Controls.Add(_messageLabel);
        Controls.Add(_fileLabel);
    }

    public void ShowFeedback(
        bool isValid,
        string? fileName,
        bool addToScene)
    {
        _messageLabel.Text = isValid
            ? addToScene
                ? "ADD TO SCENE"
                : "OPEN MODEL"
            : "WRONG FORMAT";
        _messageLabel.ForeColor = isValid
            ? Color.FromArgb(105, 230, 145)
            : Color.FromArgb(255, 105, 105);
        _fileLabel.Text = isValid
            ? $"{fileName}\n" +
              (addToScene
                  ? "Release Ctrl to replace the current scene"
                  : "Hold Ctrl to add without replacing")
            : fileName ?? string.Empty;
        Visible = true;
        BringToFront();
    }

    public void HideFeedback()
    {
        Visible = false;
    }
}
