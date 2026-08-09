using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace FastViewDX12;

public sealed class MediaFoundationVideoEncoder : IDisposable
{
    private const long MediaFoundationTicksPerSecond = 10_000_000;

    private readonly IMFSinkWriter _writer;
    private readonly int _streamIndex;
    private readonly int _frameByteCount;
    private readonly long _frameDuration;
    private bool _finalized;
    private bool _disposed;

    public MediaFoundationVideoEncoder(
        string path,
        int width,
        int height,
        int framesPerSecond,
        int bitrate = 12_000_000)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Video path is empty.", nameof(path));
        }

        if (width <= 0 || height <= 0 || framesPerSecond <= 0 || bitrate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Video dimensions, frame rate, and bitrate must be positive.");
        }

        if ((width & 1) != 0 || (height & 1) != 0)
        {
            throw new ArgumentException("H.264 video dimensions must be even.");
        }

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _frameByteCount = checked(width * height * 4);
        _frameDuration = MediaFoundationTicksPerSecond / framesPerSecond;

        MediaFactory.MFStartup().CheckError();

        try
        {
            _writer = MediaFactory.MFCreateSinkWriterFromURL(
                fullPath,
                null,
                null);

            using IMFMediaType outputType = MediaFactory.MFCreateMediaType();
            outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264).CheckError();
            outputType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate).CheckError();
            outputType.SetEnumValue(
                MediaTypeAttributeKeys.InterlaceMode,
                VideoInterlaceMode.Progressive).CheckError();
            MediaFactory.MFSetAttributeSize(
                outputType,
                MediaTypeAttributeKeys.FrameSize,
                (uint)width,
                (uint)height).CheckError();
            MediaFactory.MFSetAttributeRatio(
                outputType,
                MediaTypeAttributeKeys.FrameRate,
                (uint)framesPerSecond,
                1).CheckError();
            MediaFactory.MFSetAttributeRatio(
                outputType,
                MediaTypeAttributeKeys.PixelAspectRatio,
                1,
                1).CheckError();

            _streamIndex = _writer.AddStream(outputType);

            using IMFMediaType inputType = MediaFactory.MFCreateMediaType();
            inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            inputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32).CheckError();
            inputType.SetEnumValue(
                MediaTypeAttributeKeys.InterlaceMode,
                VideoInterlaceMode.Progressive).CheckError();
            inputType.Set(MediaTypeAttributeKeys.FixedSizeSamples, true).CheckError();
            inputType.Set(MediaTypeAttributeKeys.AllSamplesIndependent, true).CheckError();
            inputType.Set(MediaTypeAttributeKeys.SampleSize, (uint)_frameByteCount).CheckError();
            inputType.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(width * 4)).CheckError();
            MediaFactory.MFSetAttributeSize(
                inputType,
                MediaTypeAttributeKeys.FrameSize,
                (uint)width,
                (uint)height).CheckError();
            MediaFactory.MFSetAttributeRatio(
                inputType,
                MediaTypeAttributeKeys.FrameRate,
                (uint)framesPerSecond,
                1).CheckError();
            MediaFactory.MFSetAttributeRatio(
                inputType,
                MediaTypeAttributeKeys.PixelAspectRatio,
                1,
                1).CheckError();

            _writer.SetInputMediaType(_streamIndex, inputType, null);
            _writer.BeginWriting();
        }
        catch
        {
            MediaFactory.MFShutdown();
            throw;
        }
    }

    public void WriteFrame(byte[] bgra32, int frameIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (bgra32 == null || bgra32.Length != _frameByteCount)
        {
            throw new ArgumentException(
                $"A video frame must contain exactly {_frameByteCount} bytes.",
                nameof(bgra32));
        }

        if (frameIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        using IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(_frameByteCount);
        buffer.Lock(out IntPtr destination, out _, out _);

        try
        {
            Marshal.Copy(bgra32, 0, destination, bgra32.Length);
        }
        finally
        {
            buffer.Unlock();
        }

        buffer.CurrentLength = _frameByteCount;

        using IMFSample sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = checked(frameIndex * _frameDuration);
        sample.SampleDuration = _frameDuration;
        _writer.WriteSample(_streamIndex, sample);
    }

    public void FinalizeVideo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_finalized)
        {
            return;
        }

        _writer.Finalize();
        _finalized = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (!_finalized)
            {
                _writer.Finalize();
                _finalized = true;
            }
        }
        finally
        {
            _writer.Dispose();
            MediaFactory.MFShutdown();
            _disposed = true;
        }
    }
}

