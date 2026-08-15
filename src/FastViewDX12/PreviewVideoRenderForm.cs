using System.Runtime;

namespace FastViewDX12;

public sealed class PreviewVideoRenderForm : Form
{
    private readonly Panel _renderPanel;
    private readonly Dx12Renderer _renderer;
    private readonly string _inputPath;
    private readonly string _outputPath;
    private readonly int _width;
    private readonly int _height;
    private readonly int _framesPerSecond;
    private readonly int _durationSeconds;
    private bool _started;

    private static string DefaultEnvironmentMapPath =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Environment",
            "default.exr");

    public PreviewVideoRenderForm(
        string inputPath,
        string outputPath,
        int width,
        int height,
        int framesPerSecond,
        int durationSeconds)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
        _width = width;
        _height = height;
        _framesPerSecond = framesPerSecond;
        _durationSeconds = durationSeconds;

        Text = "FastView Preview Video Renderer";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(width, height);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(
            SystemInformation.VirtualScreen.Left - width - 200,
            SystemInformation.VirtualScreen.Top - height - 200);

        _renderPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black
        };

        Controls.Add(_renderPanel);
        _renderer = new Dx12Renderer(_renderPanel);
        _renderPanel.HandleCreated += (_, _) => _renderer.Initialize();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_started)
        {
            return;
        }

        _started = true;
        BeginInvoke(new Action(StartExport));
    }

    private async void StartExport()
    {
        try
        {
            _renderer.Initialize();

            if (File.Exists(DefaultEnvironmentMapPath))
            {
                _renderer.LoadEnvironmentMap(DefaultEnvironmentMapPath);
            }

            var performance = new ModelLoadPerformanceReport(_inputPath);

            try
            {
                SceneData scene = ModelSceneLoader.LoadFromFile(
                    _inputPath,
                    performance: performance);

                CompactLargeObjectHeap();

                // Offline preview output should preserve the source textures.
                _renderer.MaximumDecodedTextureSize = 0;
                using (performance.Measure("Uploading scene to GPU"))
                {
                    _renderer.LoadScene(scene);
                }

                scene.ReleaseEncodedTextureData();

                CompactLargeObjectHeap();
            }
            finally
            {
                performance.Publish();
            }

            await PreviewVideoExporter.ExportAsync(
                _renderer,
                _outputPath,
                _width,
                _height,
                _framesPerSecond,
                _durationSeconds);

            DeleteOldErrorLog();
            Environment.ExitCode = 0;
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            TryWriteErrorLog(exception);
        }
        finally
        {
            Close();
        }
    }

    private static void CompactLargeObjectHeap()
    {
        GCSettings.LargeObjectHeapCompactionMode =
            GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Aggressive,
            blocking: true,
            compacting: true);
    }

    private void DeleteOldErrorLog()
    {
        try
        {
            File.Delete(_outputPath + ".error.txt");
        }
        catch
        {
        }
    }

    private void TryWriteErrorLog(Exception exception)
    {
        try
        {
            File.WriteAllText(_outputPath + ".error.txt", exception.ToString());
        }
        catch
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderer.Dispose();
        }

        base.Dispose(disposing);
    }
}
