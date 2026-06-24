using System;
using System.Drawing;
using System.IO;

namespace FastViewDX12;

/// <summary>
/// Decodes common embedded glTF image formats into a tightly packed 32-bit RGBA buffer.
/// </summary>
public static class TextureDecoder
{
    /// <summary>
    /// Decodes encoded image bytes and normalizes the result to Format32bppArgb channel order.
    /// </summary>
    public static DecodedTexture Decode(
        byte[] encodedImage)
    {
        if (encodedImage == null ||
            encodedImage.Length == 0)
        {
            throw new ArgumentException(
                "Image data is empty.",
                nameof(encodedImage));
        }

        using var stream =
            new MemoryStream(
                encodedImage,
                writable: false);

        using var bitmap =
            new Bitmap(stream);

        int width =
            bitmap.Width;

        int height =
            bitmap.Height;

        byte[] rgba =
            new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color =
                    bitmap.GetPixel(
                        x,
                        y);

                int destinationIndex =
                    (y * width + x) * 4;

                rgba[destinationIndex + 0] =
                    color.R;

                rgba[destinationIndex + 1] =
                    color.G;

                rgba[destinationIndex + 2] =
                    color.B;

                rgba[destinationIndex + 3] =
                    color.A;
            }
        }

        return new DecodedTexture(
            width,
            height,
            rgba);
    }
}