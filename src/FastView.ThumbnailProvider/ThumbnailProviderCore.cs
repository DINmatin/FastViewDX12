using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FastView.ThumbnailProvider;

/// <summary>
/// Coordinates thumbnail generation, temporary files, validation, diagnostics, and cleanup.
/// </summary>
internal static partial class ThumbnailProviderCore
{
    private const int MinimumThumbnailSize = 64;

    private const int MaximumThumbnailSize = 2048;

    /// <summary>
    /// Runs FastView in thumbnail mode, validates the generated PNG, and returns a premultiplied ARGB HBITMAP owned by Explorer.
    /// </summary>
    public static IntPtr CreateThumbnail(
        string inputPath,
        uint requestedSize)
    {
        int thumbnailSize =
            Math.Clamp(
                (int)Math.Min(
                    requestedSize,
                    MaximumThumbnailSize),
                MinimumThumbnailSize,
                MaximumThumbnailSize);

        string temporaryPngPath =
            Path.Combine(
                Path.GetTempPath(),
                "FastView_Thumb_" +
                Guid.NewGuid().ToString("N") +
                ".png");

        try
        {
            string providerDirectory =
                Path.GetDirectoryName(
                    typeof(FastViewThumbnailProvider)
                        .Assembly
                        .Location)
                ?? throw new InvalidOperationException(
                    "Could not determine the thumbnail-provider directory.");

            string executablePath =
                Path.Combine(
                    providerDirectory,
                    "FastViewDX12.exe");

            if (!File.Exists(
                executablePath))
            {
                throw new FileNotFoundException(
                    "FastViewDX12.exe was not found beside the thumbnail provider.",
                    executablePath);
            }

            RunFastViewThumbnailCommand(
                executablePath,
                inputPath,
                temporaryPngPath,
                thumbnailSize);

            return CreatePremultipliedArgbBitmap(
                temporaryPngPath);
        }
        finally
        {
            TryDeleteFile(
                temporaryPngPath);

            TryDeleteFile(
                temporaryPngPath +
                ".log.txt");

            TryDeleteFile(
                temporaryPngPath +
                ".error.txt");
        }
    }

    /// <summary>
    /// Copies an Explorer-provided COM stream to a temporary GLB file without assuming a managed Stream wrapper.
    /// </summary>
    public static void CopyStreamToFile(
        IStream source,
        string destinationPath)
    {
        source.Seek(
            0,
            0,
            IntPtr.Zero);

        byte[] buffer =
            new byte[64 * 1024];

        IntPtr bytesReadPointer =
            Marshal.AllocCoTaskMem(
                sizeof(int));

        try
        {
            using FileStream destination =
                new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read);

            while (true)
            {
                Marshal.WriteInt32(
                    bytesReadPointer,
                    0);

                source.Read(
                    buffer,
                    buffer.Length,
                    bytesReadPointer);

                int bytesRead =
                    Marshal.ReadInt32(
                        bytesReadPointer);

                if (bytesRead <= 0)
                {
                    break;
                }

                destination.Write(
                    buffer,
                    0,
                    bytesRead);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(
                bytesReadPointer);
        }
    }

    /// <summary>
    /// Writes an exception report to the user temp directory without throwing secondary errors.
    /// </summary>
    public static void TryWriteDiagnosticLog(
        string providerType,
        Exception exception)
    {
        try
        {
            string path =
                Path.Combine(
                    Path.GetTempPath(),
                    "FastView_ThumbnailProvider_Error.txt");

            File.WriteAllText(
                path,
                "Provider: " +
                providerType +
                Environment.NewLine +
                exception);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Best-effort cleanup for temporary source, PNG, and diagnostic files.
    /// </summary>
    public static void TryDeleteFile(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
            path))
        {
            return;
        }

        try
        {
            if (File.Exists(
                path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
        }
    }

}
