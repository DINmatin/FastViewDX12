namespace FastViewDX12;

public static class PreviewVideoExporter
{
    public static async Task ExportAsync(
        Dx12Renderer renderer,
        string outputPath,
        int width = 1920,
        int height = 1080,
        int framesPerSecond = 30,
        int durationSeconds = 8,
        IProgress<ModelLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        int totalFrames = checked(framesPerSecond * durationSeconds);
        Dx12Renderer.CameraState cameraState = renderer.CaptureCameraState();

        try
        {
            float startAngle = renderer.PreparePreviewOrbit();

            using var encoder = new MediaFoundationVideoEncoder(
                outputPath,
                width,
                height,
                framesPerSecond);

            for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                float orbitProgress = frameIndex / (float)totalFrames;
                renderer.SetPreviewOrbitAngle(
                    startAngle + orbitProgress * MathF.Tau);

                byte[] frame = renderer.CaptureVideoFrameBgra32(width, height);
                encoder.WriteFrame(frame, frameIndex);

                progress?.Report(
                    new ModelLoadProgress(
                        ModelLoadStage.Finalizing,
                        $"Rendering video frame {frameIndex + 1}/{totalFrames}",
                        Path.GetFileName(outputPath),
                        frameIndex + 1,
                        totalFrames));

                await Task.Yield();
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(
                new ModelLoadProgress(
                    ModelLoadStage.Finalizing,
                    "Finalizing H.264 video",
                    Path.GetFileName(outputPath)));

            encoder.FinalizeVideo();
        }
        finally
        {
            renderer.RestoreCameraState(cameraState);
            renderer.Render();
        }
    }
}
