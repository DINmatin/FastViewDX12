using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FastViewDX12;

public enum ModelLoadStage
{
    ReadingFile,
    ParsingScene,
    ProcessingMeshes,
    DecodingTextures,
    UploadingToGpu,
    BuildingScene,
    Finalizing
}

public sealed record ModelLoadProgress(
    ModelLoadStage Stage,
    string Message,
    string FileName,
    int? Current = null,
    int? Total = null)
{
    public bool IsIndeterminate =>
        !Current.HasValue ||
        !Total.HasValue ||
        Total.Value <= 0;
}

public sealed class ModelLoadPerformanceReport
{
    public const string TimingLogEnvironmentVariable =
        "FASTVIEW_LOAD_TIMING_LOG";

    private readonly object _sync = new();
    private readonly Dictionary<string, TimeSpan> _phaseDurations =
        new(StringComparer.Ordinal);
    private readonly Stopwatch _totalStopwatch = Stopwatch.StartNew();

    public ModelLoadPerformanceReport(string modelPath)
    {
        ModelPath = Path.GetFullPath(modelPath);
    }

    public string ModelPath { get; }

    public TimeSpan TotalDuration => _totalStopwatch.Elapsed;

    public IDisposable Measure(string phaseName)
    {
        return new PhaseMeasurement(this, phaseName);
    }

    public void AddDuration(string phaseName, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(phaseName) || duration < TimeSpan.Zero)
        {
            return;
        }

        lock (_sync)
        {
            _phaseDurations.TryGetValue(
                phaseName,
                out TimeSpan existing);

            _phaseDurations[phaseName] = existing + duration;
        }
    }

    public string FinishAndFormat()
    {
        _totalStopwatch.Stop();

        KeyValuePair<string, TimeSpan>[] phases;

        lock (_sync)
        {
            phases = _phaseDurations.ToArray();
        }

        var text = new StringBuilder();
        text.AppendLine(
            $"FastView model load timings: {Path.GetFileName(ModelPath)}");

        foreach ((string name, TimeSpan duration) in phases)
        {
            text.AppendLine(
                $"  {name}: {duration.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)} ms");
        }

        text.Append(
            $"  Total: {_totalStopwatch.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)} ms");

        return text.ToString();
    }

    public void Publish()
    {
        string summary = FinishAndFormat();
        Debug.WriteLine(summary);

        string? logPath =
            Environment.GetEnvironmentVariable(
                TimingLogEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(logPath))
        {
            return;
        }

        try
        {
            string fullPath = Path.GetFullPath(logPath);
            string? directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (_sync)
            {
                File.AppendAllText(
                    fullPath,
                    $"{DateTimeOffset.Now:O}{Environment.NewLine}" +
                    $"{summary}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not write model-load timing log: {exception.Message}");
        }
    }

    private sealed class PhaseMeasurement : IDisposable
    {
        private readonly ModelLoadPerformanceReport _owner;
        private readonly string _phaseName;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _disposed;

        public PhaseMeasurement(
            ModelLoadPerformanceReport owner,
            string phaseName)
        {
            _owner = owner;
            _phaseName = phaseName;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopwatch.Stop();
            _owner.AddDuration(
                _phaseName,
                _stopwatch.Elapsed);
        }
    }
}

internal sealed class MeasuredReadStream : Stream
{
    private readonly Stream _inner;
    private long _readTimestampTicks;

    public MeasuredReadStream(Stream inner)
    {
        _inner = inner;
    }

    public TimeSpan ReadDuration =>
        TimeSpan.FromSeconds(
            (double)_readTimestampTicks /
            Stopwatch.Frequency);

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush()
    {
        _inner.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        long start = Stopwatch.GetTimestamp();

        try
        {
            return _inner.Read(buffer, offset, count);
        }
        finally
        {
            Interlocked.Add(
                ref _readTimestampTicks,
                Stopwatch.GetTimestamp() - start);
        }
    }

    public override int Read(Span<byte> buffer)
    {
        long start = Stopwatch.GetTimestamp();

        try
        {
            return _inner.Read(buffer);
        }
        finally
        {
            Interlocked.Add(
                ref _readTimestampTicks,
                Stopwatch.GetTimestamp() - start);
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}

