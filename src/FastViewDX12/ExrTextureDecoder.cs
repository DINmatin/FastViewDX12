using System;
using System.IO;
using TinyEXR;

namespace FastViewDX12;

/// <summary>
/// Decodes OpenEXR files into finite linear RGBA values suitable for mip generation and GPU upload.
/// </summary>
public static class ExrTextureDecoder
{
    /// <summary>
    /// Loads one EXR image and validates the decoder result.
    /// </summary>
    public static DecodedHdrTexture DecodeFile(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "EXR path is empty.",
                nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "EXR file was not found.",
                path);
        }

        ResultCode result =
            Exr.LoadEXR(
                path,
                out float[] rgba,
                out int width,
                out int height);

        if (result != ResultCode.Success)
        {
            throw new InvalidDataException(
                $"Could not load EXR file. " +
                $"TinyEXR result: {result}");
        }

        if (width <= 0 ||
            height <= 0 ||
            rgba.Length != width * height * 4)
        {
            throw new InvalidDataException(
                "The EXR decoder returned invalid image dimensions.");
        }

        SanitizePixels(
            rgba);

        return new DecodedHdrTexture(
            width,
            height,
            rgba);
    }

    /// <summary>
    /// Replaces non-finite or negative radiance values in place.
    /// </summary>
    private static void SanitizePixels(
        float[] rgba)
    {
        for (int i = 0;
             i + 3 < rgba.Length;
             i += 4)
        {
            rgba[i + 0] =
                SanitizeRadiance(
                    rgba[i + 0]);

            rgba[i + 1] =
                SanitizeRadiance(
                    rgba[i + 1]);

            rgba[i + 2] =
                SanitizeRadiance(
                    rgba[i + 2]);

            rgba[i + 3] = 1.0f;
        }
    }

    /// <summary>
    /// Converts one radiance channel to a finite non-negative value.
    /// </summary>
    private static float SanitizeRadiance(
        float value)
    {
        if (!float.IsFinite(value))
        {
            return 0.0f;
        }

        return Math.Clamp(
            value,
            0.0f,
            65504.0f);
    }
}