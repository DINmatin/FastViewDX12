using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FastView.ThumbnailProvider;

/// <summary>
/// Converts PNG output to the premultiplied ARGB HBITMAP format expected by Windows Explorer.
/// </summary>
internal static partial class ThumbnailProviderCore
{
    /// <summary>
    /// Loads the transparent PNG and creates a top-down 32-bit DIB whose BGRA channels use premultiplied alpha as required by Explorer.
    /// </summary>
    private static IntPtr CreatePremultipliedArgbBitmap(
        string pngPath)
    {
        using var source =
            new Bitmap(
                pngPath);

        int width =
            source.Width;

        int height =
            source.Height;

        using var normalized =
            new Bitmap(
                width,
                height,
                PixelFormat.Format32bppArgb);

        using (Graphics graphics =
               Graphics.FromImage(
                   normalized))
        {
            graphics.Clear(
                Color.Transparent);

            graphics.CompositingMode =
                CompositingMode.SourceCopy;

            graphics.DrawImageUnscaled(
                source,
                0,
                0);
        }

        var bitmapInfo =
            new BitmapInfo
            {
                Header =
                    new BitmapInfoHeader
                    {
                        Size =
                            (uint)Marshal.SizeOf<BitmapInfoHeader>(),

                        Width =
                            width,

                        Height =
                            -height,

                        Planes =
                            1,

                        BitCount =
                            32,

                        Compression =
                            0,

                        SizeImage =
                            checked(
                                (uint)(width * height * 4))
                    }
            };

        IntPtr bitmapHandle =
            CreateDIBSection(
                IntPtr.Zero,
                ref bitmapInfo,
                0,
                out IntPtr destinationBits,
                IntPtr.Zero,
                0);

        if (bitmapHandle ==
                IntPtr.Zero ||
            destinationBits ==
                IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Could not create the thumbnail HBITMAP.");
        }

        try
        {
            Rectangle rectangle =
                new Rectangle(
                    0,
                    0,
                    width,
                    height);

            BitmapData sourceData =
                normalized.LockBits(
                    rectangle,
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

            try
            {
                int destinationStride =
                    checked(
                        width *
                        4);

                byte[] destination =
                    new byte[
                        checked(
                            destinationStride *
                            height)];

                int sourceStride =
                    Math.Abs(
                        sourceData.Stride);

                byte[] sourceRow =
                    new byte[
                        sourceStride];

                for (int y = 0;
                     y < height;
                     y++)
                {
                    int sourceRowIndex =
                        sourceData.Stride >= 0
                            ? y
                            : height - 1 - y;

                    IntPtr sourcePointer =
                        IntPtr.Add(
                            sourceData.Scan0,
                            sourceRowIndex *
                            sourceStride);

                    Marshal.Copy(
                        sourcePointer,
                        sourceRow,
                        0,
                        sourceRow.Length);

                    int destinationRowOffset =
                        y *
                        destinationStride;

                    for (int x = 0;
                         x < width;
                         x++)
                    {
                        int sourceOffset =
                            x *
                            4;

                        int destinationOffset =
                            destinationRowOffset +
                            sourceOffset;

                        int alpha =
                            sourceRow[
                                sourceOffset +
                                3];

                        destination[
                            destinationOffset +
                            0] =
                            Premultiply(
                                sourceRow[
                                    sourceOffset +
                                    0],
                                alpha);

                        destination[
                            destinationOffset +
                            1] =
                            Premultiply(
                                sourceRow[
                                    sourceOffset +
                                    1],
                                alpha);

                        destination[
                            destinationOffset +
                            2] =
                            Premultiply(
                                sourceRow[
                                    sourceOffset +
                                    2],
                                alpha);

                        destination[
                            destinationOffset +
                            3] =
                            (byte)alpha;
                    }
                }

                Marshal.Copy(
                    destination,
                    0,
                    destinationBits,
                    destination.Length);
            }
            finally
            {
                normalized.UnlockBits(
                    sourceData);
            }

            return bitmapHandle;
        }
        catch
        {
            DeleteObject(
                bitmapHandle);

            throw;
        }
    }

    private static byte Premultiply(
        byte color,
        int alpha)
    {
        return (byte)(
            (color *
             alpha +
             127) /
            255);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport(
        "gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(
        IntPtr graphicsObject);

}
