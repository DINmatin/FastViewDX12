using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using DrawingColor = System.Drawing.Color;
using DrawingInterpolationMode = System.Drawing.Drawing2D.InterpolationMode;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace FastViewDX12;

/// <summary>
/// Preview readback, alpha conversion, content detection, crop calculation, and PNG output.
/// </summary>
public sealed partial class Dx12Renderer
{
    private sealed class PreviewCaptureRequest
    {
        public string Path { get; }

        public int Width { get; }

        public int Height { get; }

        public bool PreserveTransparency { get; }

        public bool Completed { get; set; }

        public Exception? Error { get; set; }

        public PreviewCaptureRequest(
            string path,
            int width,
            int height,
            bool preserveTransparency)
        {
            Path = path;
            Width = width;
            Height = height;
            PreserveTransparency = preserveTransparency;
        }
    }

    /// <summary>
    /// Renders one synchronous frame, crops it around visible model content, and writes a PNG preview.
    /// </summary>
    /// <param name="path">Destination PNG path.</param>
    /// <param name="width">Requested output width.</param>
    /// <param name="height">Requested output height.</param>
    public void SavePreviewPng(
    string path,
    int width = 800,
    int height = 800)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "Renderer is not initialized.");
        }

        if (_scene == null ||
            _scene.Meshes.Count == 0)
        {
            throw new InvalidOperationException(
                "No model is currently loaded.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Preview path is empty.",
                nameof(path));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height));
        }

        string fullPath =
            Path.GetFullPath(path);

        string? directory =
            Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        var request =
            new PreviewCaptureRequest(
                fullPath,
                width,
                height,
                _transparentBackground);

        _pendingPreviewCapture =
            request;

        // Render one frame synchronously; Render() completes the pending capture before returning.
        Render();

        if (request.Error != null)
        {
            throw new InvalidOperationException(
                "Preview image could not be saved.",
                request.Error);
        }

        if (!request.Completed)
        {
            throw new InvalidOperationException(
                "Preview capture did not complete.");
        }
    }

    /// <summary>
    /// Allocates a row-pitch-aligned readback buffer for a copied backbuffer.
    /// </summary>
    private ID3D12Resource CreatePreviewReadbackBuffer(
    int width,
    int height,
    out uint rowPitch)
    {
        if (_device == null)
        {
            throw new InvalidOperationException(
                "Device is not initialized.");
        }

        const uint bytesPerPixel =
            4;

        const uint texturePitchAlignment =
            256;

        uint unalignedRowPitch =
            checked(
                (uint)width *
                bytesPerPixel);

        rowPitch =
            AlignUp(
                unalignedRowPitch,
                texturePitchAlignment);

        ulong bufferSize =
            checked(
                (ulong)rowPitch *
                (uint)height);

        return _device.CreateCommittedResource(
            new HeapProperties(
                HeapType.Readback),
            HeapFlags.None,
            ResourceDescription.Buffer(
                bufferSize),
            ResourceStates.CopyDest);
    }

    /// <summary>
    /// Converts the copied RGBA backbuffer into straight-alpha BGRA, crops it, scales it, and writes the final PNG.
    /// </summary>
    private static void SavePreviewReadbackToPng(
    ID3D12Resource readbackBuffer,
    uint rowPitch,
    int sourceWidth,
    int sourceHeight,
    PreviewCaptureRequest request)
    {
        int mappedByteCount =
            checked(
                (int)(
                    (ulong)rowPitch *
                    (uint)sourceHeight));

        Span<byte> mappedData =
            readbackBuffer.Map<byte>(
                0,
                mappedByteCount);

        System.Drawing.Rectangle contentBounds =
    FindPreviewContentBounds(
        mappedData,
        rowPitch,
        sourceWidth,
        sourceHeight,
        request.PreserveTransparency);

        System.Drawing.Rectangle cropRectangle =
            CreatePreviewCropRectangle(
                contentBounds,
                sourceWidth,
                sourceHeight,
                request.Width,
                request.Height);

        using var sourceBitmap =
            new Bitmap(
                sourceWidth,
                sourceHeight,
                DrawingPixelFormat.Format32bppArgb);

        BitmapData bitmapData =
            sourceBitmap.LockBits(
                new System.Drawing.Rectangle(
                    0,
                    0,
                    sourceWidth,
                    sourceHeight),
                ImageLockMode.WriteOnly,
                DrawingPixelFormat.Format32bppArgb);

        try
        {
            int sourceRowBytes =
                checked(
                    sourceWidth *
                    4);

            var destinationRow =
                new byte[sourceRowBytes];

            for (int y = 0;
                 y < sourceHeight;
                 y++)
            {
                int sourceRowOffset =
                    checked(
                        y *
                        (int)rowPitch);

                for (int x = 0;
                     x < sourceWidth;
                     x++)
                {
                    int sourcePixel =
                        sourceRowOffset +
                        x * 4;

                    int destinationPixel =
                        x * 4;

                    // DX12 backbuffer: RGBA
                    // Bitmap Format32bppArgb: BGRA
                    byte red =
                        mappedData[
                            sourcePixel + 0];

                    byte green =
                        mappedData[
                            sourcePixel + 1];

                    byte blue =
                        mappedData[
                            sourcePixel + 2];

                    byte alpha =
                        mappedData[
                            sourcePixel + 3];

                    // Alpha blending into a transparent render target leaves
                    // premultiplied RGB in the backbuffer. PNG stores straight
                    // alpha, so undo that before writing the PNG.
                    if (request.PreserveTransparency &&
                        alpha > 0 &&
                        alpha < 255)
                    {
                        red =
                            UnpremultiplyColor(
                                red,
                                alpha);

                        green =
                            UnpremultiplyColor(
                                green,
                                alpha);

                        blue =
                            UnpremultiplyColor(
                                blue,
                                alpha);
                    }

                    destinationRow[
                        destinationPixel + 0] =
                        blue;

                    destinationRow[
                        destinationPixel + 1] =
                        green;

                    destinationRow[
                        destinationPixel + 2] =
                        red;

                    destinationRow[
                        destinationPixel + 3] =
                        alpha;
                }

                IntPtr destinationPointer =
                    IntPtr.Add(
                        bitmapData.Scan0,
                        y *
                        bitmapData.Stride);

                Marshal.Copy(
                    destinationRow,
                    0,
                    destinationPointer,
                    destinationRow.Length);
            }
        }
        finally
        {
            sourceBitmap.UnlockBits(
                bitmapData);

            readbackBuffer.Unmap(
                0);
        }

        using var outputBitmap =
            new Bitmap(
                request.Width,
                request.Height,
                DrawingPixelFormat.Format32bppArgb);

        using Graphics graphics =
            Graphics.FromImage(
                outputBitmap);

        graphics.Clear(
            DrawingColor.Transparent);

        graphics.CompositingMode =
            CompositingMode.SourceCopy;

        graphics.InterpolationMode =
            DrawingInterpolationMode.HighQualityBicubic;

        graphics.PixelOffsetMode =
            PixelOffsetMode.HighQuality;

        graphics.CompositingQuality =
            CompositingQuality.HighQuality;

        graphics.DrawImage(
        sourceBitmap,
        new System.Drawing.Rectangle(
            0,
            0,
            request.Width,
            request.Height),
        cropRectangle,
        GraphicsUnit.Pixel);

        outputBitmap.Save(
            request.Path,
            ImageFormat.Png);
    }

    private static byte UnpremultiplyColor(
        byte color,
        byte alpha)
    {
        if (alpha == 0)
        {
            return 0;
        }

        return (byte)Math.Min(
            255,
            (color * 255 + alpha / 2) /
            alpha);
    }

    private static uint AlignUp(
        uint value,
        uint alignment)
    {
        return
            (
                value +
                alignment -
                1
            ) /
            alignment *
            alignment;
    }

    /// <summary>
    /// Finds the rectangle that differs from the background, using alpha for transparent captures and corner colors otherwise.
    /// </summary>
    private static System.Drawing.Rectangle FindPreviewContentBounds(
    ReadOnlySpan<byte> rgbaData,
    uint rowPitch,
    int width,
    int height,
    bool useAlphaChannel)
    {
        if (width <= 0 ||
            height <= 0)
        {
            return System.Drawing.Rectangle.Empty;
        }

        (int backgroundR,
         int backgroundG,
         int backgroundB) =
            GetAverageCornerColor(
                rgbaData,
                rowPitch,
                width,
                height);

        int minX =
            width;

        int minY =
            height;

        int maxX =
            -1;

        int maxY =
            -1;

        const int colorDifferenceThreshold =
            18;

        for (int y = 0;
             y < height;
             y++)
        {
            int rowOffset =
                checked(
                    y *
                    (int)rowPitch);

            for (int x = 0;
                 x < width;
                 x++)
            {
                int pixelOffset =
                    rowOffset +
                    x * 4;

                if (useAlphaChannel)
                {
                    int alpha =
                        rgbaData[
                            pixelOffset + 3];

                    if (alpha <=
                        2)
                    {
                        continue;
                    }
                }
                else
                {
                    int red =
                        rgbaData[
                            pixelOffset + 0];

                    int green =
                        rgbaData[
                            pixelOffset + 1];

                    int blue =
                        rgbaData[
                            pixelOffset + 2];

                    int difference =
                        Math.Abs(
                            red -
                            backgroundR) +
                        Math.Abs(
                            green -
                            backgroundG) +
                        Math.Abs(
                            blue -
                            backgroundB);

                    if (difference <=
                        colorDifferenceThreshold)
                    {
                        continue;
                    }
                }

                minX =
                    Math.Min(
                        minX,
                        x);

                minY =
                    Math.Min(
                        minY,
                        y);

                maxX =
                    Math.Max(
                        maxX,
                        x);

                maxY =
                    Math.Max(
                        maxY,
                        y);
            }
        }

        if (maxX < minX ||
            maxY < minY)
        {
            return new System.Drawing.Rectangle(
                0,
                0,
                width,
                height);
        }

        return System.Drawing.Rectangle.FromLTRB(
            minX,
            minY,
            maxX + 1,
            maxY + 1);
    }

    private static (
        int Red,
        int Green,
        int Blue)
        GetAverageCornerColor(
            ReadOnlySpan<byte> rgbaData,
            uint rowPitch,
            int width,
            int height)
    {
        int red = 0;
        int green = 0;
        int blue = 0;

        AddCornerPixel(
            rgbaData,
            rowPitch,
            0,
            0,
            ref red,
            ref green,
            ref blue);

        AddCornerPixel(
            rgbaData,
            rowPitch,
            width - 1,
            0,
            ref red,
            ref green,
            ref blue);

        AddCornerPixel(
            rgbaData,
            rowPitch,
            0,
            height - 1,
            ref red,
            ref green,
            ref blue);

        AddCornerPixel(
            rgbaData,
            rowPitch,
            width - 1,
            height - 1,
            ref red,
            ref green,
            ref blue);

        return (
            red / 4,
            green / 4,
            blue / 4);
    }

    private static void AddCornerPixel(
        ReadOnlySpan<byte> rgbaData,
        uint rowPitch,
        int x,
        int y,
        ref int red,
        ref int green,
        ref int blue)
    {
        int offset =
            checked(
                y *
                (int)rowPitch +
                x * 4);

        red +=
            rgbaData[
                offset + 0];

        green +=
            rgbaData[
                offset + 1];

        blue +=
            rgbaData[
                offset + 2];
    }

    /// <summary>
    /// Expands visible bounds to the requested aspect ratio while keeping the crop inside the rendered frame.
    /// </summary>
    private static System.Drawing.Rectangle CreatePreviewCropRectangle(
        System.Drawing.Rectangle contentBounds,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        if (contentBounds.Width <= 0 ||
            contentBounds.Height <= 0)
        {
            return new System.Drawing.Rectangle(
                0,
                0,
                sourceWidth,
                sourceHeight);
        }

        int contentSize =
            Math.Max(
                contentBounds.Width,
                contentBounds.Height);

        int padding =
            Math.Max(
                4,
                (int)MathF.Ceiling(
                    contentSize *
                    0.06f));

        int left =
            Math.Max(
                0,
                contentBounds.Left -
                padding);

        int top =
            Math.Max(
                0,
                contentBounds.Top -
                padding);

        int right =
            Math.Min(
                sourceWidth,
                contentBounds.Right +
                padding);

        int bottom =
            Math.Min(
                sourceHeight,
                contentBounds.Bottom +
                padding);

        float targetAspect =
            targetWidth /
            (float)targetHeight;

        int cropWidth =
            right -
            left;

        int cropHeight =
            bottom -
            top;

        float cropAspect =
            cropWidth /
            (float)cropHeight;

        if (cropAspect <
            targetAspect)
        {
            int desiredWidth =
                (int)MathF.Ceiling(
                    cropHeight *
                    targetAspect);

            ExpandRange(
                ref left,
                ref right,
                desiredWidth,
                sourceWidth);
        }
        else if (cropAspect >
                 targetAspect)
        {
            int desiredHeight =
                (int)MathF.Ceiling(
                    cropWidth /
                    targetAspect);

            ExpandRange(
                ref top,
                ref bottom,
                desiredHeight,
                sourceHeight);
        }

        return System.Drawing.Rectangle.FromLTRB(
            left,
            top,
            right,
            bottom);
    }

    private static void ExpandRange(
        ref int start,
        ref int end,
        int desiredLength,
        int maximumLength)
    {
        int currentLength =
            end -
            start;

        int additionalLength =
            desiredLength -
            currentLength;

        if (additionalLength <= 0)
        {
            return;
        }

        int before =
            additionalLength /
            2;

        int after =
            additionalLength -
            before;

        start -=
            before;

        end +=
            after;

        if (start < 0)
        {
            end =
                Math.Min(
                    maximumLength,
                    end -
                    start);

            start =
                0;
        }

        if (end >
            maximumLength)
        {
            int overflow =
                end -
                maximumLength;

            start =
                Math.Max(
                    0,
                    start -
                    overflow);

            end =
                maximumLength;
        }
    }

}
