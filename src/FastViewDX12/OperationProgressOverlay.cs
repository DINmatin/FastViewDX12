using System.Drawing;
using System.Windows.Forms;

namespace FastViewDX12;

public sealed class OperationProgressOverlay : UserControl
{
    private readonly Label _titleLabel;
    private readonly Label _fileLabel;
    private readonly Label _messageLabel;
    private readonly Label _percentLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _cancelButton;

    public OperationProgressOverlay()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(24, 24, 28);
        Visible = false;
        TabStop = true;

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.FromArgb(36, 36, 42),
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(48, 38, 48, 34),
            Anchor = AnchorStyles.None,
            MinimumSize = new Size(720, 360)
        };

        _titleLabel = CreateLabel(
            new Font("Segoe UI", 28.0f, FontStyle.Bold),
            Color.White,
            70);

        _fileLabel = CreateLabel(
            new Font("Segoe UI", 15.0f, FontStyle.Regular),
            Color.Gainsboro,
            54);

        _messageLabel = CreateLabel(
            new Font("Segoe UI", 19.0f, FontStyle.Regular),
            Color.White,
            70);

        _percentLabel = CreateLabel(
            new Font("Segoe UI", 13.0f, FontStyle.Bold),
            Color.LightGray,
            42);

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 38,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 28,
            Margin = new Padding(0, 8, 0, 22)
        };

        _cancelButton = new Button
        {
            Text = "Cancel",
            AutoSize = false,
            Size = new Size(170, 46),
            Anchor = AnchorStyles.None,
            Font = new Font("Segoe UI", 12.0f, FontStyle.Regular),
            Margin = new Padding(0, 12, 0, 0)
        };

        _cancelButton.Click +=
            (_, _) =>
            {
                _cancelButton.Enabled = false;
                _messageLabel.Text = "Cancelling safely…";
                CancelRequested?.Invoke(this, EventArgs.Empty);
            };

        content.Controls.Add(_titleLabel, 0, 0);
        content.Controls.Add(_fileLabel, 0, 1);
        content.Controls.Add(_messageLabel, 0, 2);
        content.Controls.Add(_progressBar, 0, 3);
        content.Controls.Add(_percentLabel, 0, 4);
        content.Controls.Add(_cancelButton, 0, 5);

        Controls.Add(content);

        Resize +=
            (_, _) =>
            {
                content.Left = Math.Max(0, (ClientSize.Width - content.Width) / 2);
                content.Top = Math.Max(0, (ClientSize.Height - content.Height) / 2);
            };
    }

    public event EventHandler? CancelRequested;

    public void BeginOperation(string title, string fileName)
    {
        _titleLabel.Text = title;
        _fileLabel.Text = fileName;
        _messageLabel.Text = "Starting…";
        _percentLabel.Text = string.Empty;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 28;
        _cancelButton.Enabled = true;
        Visible = true;
        BringToFront();
        Focus();
    }

    public void UpdateProgress(ModelLoadProgress progress)
    {
        _fileLabel.Text = progress.FileName;
        _messageLabel.Text = progress.Message;

        if (progress.IsIndeterminate)
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 28;
            _percentLabel.Text = string.Empty;
            return;
        }

        int current = Math.Clamp(progress.Current!.Value, 0, progress.Total!.Value);
        int percent = (int)Math.Round(
            current * 100.0 / progress.Total.Value);

        _progressBar.MarqueeAnimationSpeed = 0;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        _progressBar.Value = Math.Clamp(percent, 0, 100);
        _percentLabel.Text = $"{percent}%";
    }

    public void EndOperation()
    {
        Visible = false;
    }

    private static Label CreateLabel(
        Font font,
        Color color,
        int height)
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = height,
            Font = font,
            ForeColor = color,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            Margin = new Padding(0, 0, 0, 8)
        };
    }
}

