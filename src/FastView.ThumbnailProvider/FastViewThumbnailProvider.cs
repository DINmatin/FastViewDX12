using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FastView.ThumbnailProvider;

/// <summary>
/// Explorer thumbnail provider for self-contained GLB files received through an <see cref="System.Runtime.InteropServices.ComTypes.IStream"/>.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
[ComDefaultInterface(typeof(IThumbnailProvider))]
[Guid("A2A86C88-5B89-4D5E-92B3-7A6CF4E0A1B7")]
public sealed class FastViewThumbnailProvider :
    IInitializeWithStream,
    IThumbnailProvider
{
    private const int Success = 0;

    private IStream? _sourceStream;

    /// <summary>
    /// Stores the Explorer-provided GLB stream for the following thumbnail request.
    /// </summary>
    public int Initialize(
        IStream stream,
        uint mode)
    {
        if (stream == null)
        {
            return unchecked(
                (int)0x80070057);
        }

        _sourceStream =
            stream;

        return Success;
    }

    /// <summary>
    /// Copies the GLB stream to a temporary file, renders it through FastView, and returns a premultiplied ARGB bitmap.
    /// </summary>
    public int GetThumbnail(
        uint size,
        out IntPtr bitmapHandle,
        out WtsAlphaType alphaType)
    {
        bitmapHandle =
            IntPtr.Zero;

        alphaType =
            WtsAlphaType.Argb;

        string? temporaryGlbPath =
            null;

        try
        {
            if (_sourceStream == null)
            {
                throw new InvalidOperationException(
                    "The GLB thumbnail provider was not initialized.");
            }

            temporaryGlbPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "FastView_Source_" +
                    Guid.NewGuid().ToString("N") +
                    ".glb");

            ThumbnailProviderCore.CopyStreamToFile(
                _sourceStream,
                temporaryGlbPath);

            bitmapHandle =
                ThumbnailProviderCore.CreateThumbnail(
                    temporaryGlbPath,
                    size);

            return Success;
        }
        catch (Exception exception)
        {
            ThumbnailProviderCore.TryWriteDiagnosticLog(
                "GLB",
                exception);

            bitmapHandle =
                IntPtr.Zero;

            alphaType =
                WtsAlphaType.Unknown;

            return Marshal.GetHRForException(
                exception);
        }
        finally
        {
            ThumbnailProviderCore.TryDeleteFile(
                temporaryGlbPath);
        }
    }
}

/// <summary>
/// Explorer thumbnail provider for glTF files that may reference neighboring BIN and texture files.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
[ComDefaultInterface(typeof(IThumbnailProvider))]
[Guid("6B07920A-CFEC-42E6-B718-73A199156EFD")]
public sealed class FastViewGltfThumbnailProvider :
    IInitializeWithFile,
    IThumbnailProvider
{
    private const int Success = 0;

    private string? _sourcePath;

    /// <summary>
    /// Stores the original glTF path so external buffers and textures remain resolvable.
    /// </summary>
    public int Initialize(
        string filePath,
        uint mode)
    {
        if (string.IsNullOrWhiteSpace(
            filePath))
        {
            return unchecked(
                (int)0x80070057);
        }

        _sourcePath =
            Path.GetFullPath(
                filePath);

        return Success;
    }

    /// <summary>
    /// Renders the original glTF path and returns a premultiplied ARGB bitmap to Explorer.
    /// </summary>
    public int GetThumbnail(
        uint size,
        out IntPtr bitmapHandle,
        out WtsAlphaType alphaType)
    {
        bitmapHandle =
            IntPtr.Zero;

        alphaType =
            WtsAlphaType.Argb;

        try
        {
            if (string.IsNullOrWhiteSpace(
                _sourcePath))
            {
                throw new InvalidOperationException(
                    "The glTF thumbnail provider was not initialized.");
            }

            if (!File.Exists(
                _sourcePath))
            {
                throw new FileNotFoundException(
                    "The glTF source file was not found.",
                    _sourcePath);
            }

            bitmapHandle =
                ThumbnailProviderCore.CreateThumbnail(
                    _sourcePath,
                    size);

            return Success;
        }
        catch (Exception exception)
        {
            ThumbnailProviderCore.TryWriteDiagnosticLog(
                "glTF",
                exception);

            bitmapHandle =
                IntPtr.Zero;

            alphaType =
                WtsAlphaType.Unknown;

            return Marshal.GetHRForException(
                exception);
        }
    }
}
