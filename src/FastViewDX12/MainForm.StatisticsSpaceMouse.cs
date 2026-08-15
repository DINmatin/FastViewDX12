using System.Diagnostics;
using System.Numerics;

namespace FastViewDX12;

// Viewport statistics and six-axis camera navigation.
public sealed partial class MainForm
{
    private const int WmInput = 0x00FF;

    private readonly Label _statisticsOverlay = new()
    {
        AutoSize = true,
        BackColor = Color.FromArgb(32, 34, 40),
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 9.0f, FontStyle.Regular),
        Padding = new Padding(9, 7, 9, 7),
        Location = new Point(12, 12),
        Visible = false
    };

    private readonly Stopwatch _statisticsClock = Stopwatch.StartNew();
    private readonly SpaceMouseInput _spaceMouseInput = new();
    private int _statisticsFrameCount;
    private double _framesPerSecond;
    private bool _statisticsVisible;
    private bool _spaceMouseCameraMode = true;

    private void InitializeStatisticsAndSpaceMouse()
    {
        _renderPanel.Controls.Add(_statisticsOverlay);

        _spaceMouseInput.Motion += OnSpaceMouseMotion;

        HandleCreated += (_, _) =>
        {
            _spaceMouseInput.Register(Handle);
        };
    }

    private void ToggleStatistics()
    {
        _statisticsVisible = !_statisticsVisible;
        _statisticsOverlay.Visible = _statisticsVisible;

        if (_statisticsVisible)
        {
            _statisticsOverlay.BringToFront();
            UpdateStatisticsOverlay(force: true);
        }
    }

    private void SetSpaceMouseCameraMode(bool enabled)
    {
        _spaceMouseCameraMode = enabled;
    }

    private void OnSpaceMouseMotion(Vector3 translation, Vector3 rotation)
    {
        if (!_spaceMouseCameraMode ||
            _isModelLoadInProgress ||
            _isVideoExportInProgress)
        {
            return;
        }

        _renderer.ApplySixAxisCameraInput(translation, rotation);
    }

    private void CountRenderedFrame()
    {
        _statisticsFrameCount++;
        UpdateStatisticsOverlay(force: false);
    }

    private void UpdateStatisticsOverlay(bool force)
    {
        double elapsed = _statisticsClock.Elapsed.TotalSeconds;

        if (!force && elapsed < 0.5)
        {
            return;
        }

        if (elapsed > 0.0)
        {
            _framesPerSecond = _statisticsFrameCount / elapsed;
        }

        _statisticsFrameCount = 0;
        _statisticsClock.Restart();

        if (!_statisticsVisible)
        {
            return;
        }

        Dx12Renderer.RendererStatistics statistics =
            _renderer.GetStatistics();

        string videoMemory = statistics.VideoMemoryBudget > 0
            ? $"{FormatBytes(statistics.VideoMemoryUsage)} / " +
              $"{FormatBytes(statistics.VideoMemoryBudget)}"
            : "not available";

        _statisticsOverlay.Text =
            $"FPS       {_framesPerSecond,7:0.0}\n" +
            $"VRAM      {videoMemory}\n" +
            $"Polys     {statistics.TriangleCount:N0}\n" +
            $"Vertices  {statistics.VertexCount:N0}\n" +
            $"3D mouse  {(_spaceMouseInput.HasReceivedInput ? "connected" : "ready")}";
    }

    private static string FormatBytes(ulong bytes)
    {
        const double gibibyte = 1024.0 * 1024.0 * 1024.0;
        const double mebibyte = 1024.0 * 1024.0;

        return bytes >= gibibyte
            ? $"{bytes / gibibyte:0.00} GiB"
            : $"{bytes / mebibyte:0} MiB";
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmInput)
        {
            _spaceMouseInput?.ProcessInput(message.LParam);
        }

        base.WndProc(ref message);
    }
}
