using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace FastViewDX12;

public static class TextureDecoder
{
    public static DecodedTexture Decode(
        byte[] encodedImage,
        int? maximumDimension = null)
    {
        ValidateImageData(encodedImage);

        try
        {
            return WicTextureDecoder.Decode(
                encodedImage,
                maximumDimension);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"WIC texture decode failed; using GDI+ fallback: " +
                exception.Message);
        }

        return DecodeWithGdiPlus(
            encodedImage,
            maximumDimension);
    }

    public static Size GetDimensions(byte[] encodedImage)
    {
        ValidateImageData(encodedImage);

        try
        {
            return WicTextureDecoder.GetDimensions(
                encodedImage);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"WIC dimension query failed; using GDI+ fallback: " +
                exception.Message);
        }

        return GetDimensionsWithGdiPlus(
            encodedImage);
    }

    private static DecodedTexture DecodeWithGdiPlus(
        byte[] encodedImage,
        int? maximumDimension)
    {

        using var stream =
            new MemoryStream(
                encodedImage,
                writable: false);

        using var source = new Bitmap(stream);

        (int width, int height) = CalculateOutputSize(
            source.Width,
            source.Height,
            maximumDimension);

        using var converted = new Bitmap(
            width,
            height,
            DrawingPixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(converted))
        {
            graphics.CompositingMode =
                CompositingMode.SourceCopy;

            graphics.CompositingQuality =
                CompositingQuality.HighSpeed;

            graphics.InterpolationMode =
                width == source.Width &&
                height == source.Height
                    ? InterpolationMode.NearestNeighbor
                    : InterpolationMode.HighQualityBilinear;

            graphics.PixelOffsetMode =
                PixelOffsetMode.HighSpeed;

            graphics.DrawImage(
                source,
                new Rectangle(0, 0, width, height),
                0,
                0,
                source.Width,
                source.Height,
                GraphicsUnit.Pixel);
        }

        byte[] bgra = CopyBitmapPixels(converted);
        ConvertBgraToRgba(bgra);

        return new DecodedTexture(
            width,
            height,
            bgra);
    }

    private static Size GetDimensionsWithGdiPlus(
        byte[] encodedImage)
    {
        using var stream =
            new MemoryStream(
                encodedImage,
                writable: false);

        using var bitmap = new Bitmap(stream);

        return new Size(
            bitmap.Width,
            bitmap.Height);
    }

    private static byte[] CopyBitmapPixels(Bitmap bitmap)
    {
        var rectangle =
            new Rectangle(
                0,
                0,
                bitmap.Width,
                bitmap.Height);

        BitmapData data = bitmap.LockBits(
            rectangle,
            ImageLockMode.ReadOnly,
            DrawingPixelFormat.Format32bppArgb);

        try
        {
            int rowBytes = checked(bitmap.Width * 4);
            byte[] pixels =
                new byte[checked(rowBytes * bitmap.Height)];

            for (int y = 0; y < bitmap.Height; y++)
            {
                int sourceY = data.Stride >= 0
                    ? y
                    : bitmap.Height - 1 - y;

                IntPtr sourceRow =
                    IntPtr.Add(
                        data.Scan0,
                        sourceY * Math.Abs(data.Stride));

                Marshal.Copy(
                    sourceRow,
                    pixels,
                    y * rowBytes,
                    rowBytes);
            }

            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void ConvertBgraToRgba(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 2]) =
                (pixels[i + 2], pixels[i]);
        }
    }

    private static (int Width, int Height) CalculateOutputSize(
        int sourceWidth,
        int sourceHeight,
        int? maximumDimension)
    {
        if (!maximumDimension.HasValue ||
            maximumDimension.Value <= 0 ||
            Math.Max(sourceWidth, sourceHeight) <= maximumDimension.Value)
        {
            return (sourceWidth, sourceHeight);
        }

        double scale =
            maximumDimension.Value /
            (double)Math.Max(sourceWidth, sourceHeight);

        return (
            Math.Max(1, (int)Math.Round(sourceWidth * scale)),
            Math.Max(1, (int)Math.Round(sourceHeight * scale)));
    }

    private static void ValidateImageData(byte[] encodedImage)
    {
        if (encodedImage == null || encodedImage.Length == 0)
        {
            throw new ArgumentException(
                "Image data is empty.",
                nameof(encodedImage));
        }
    }
}
