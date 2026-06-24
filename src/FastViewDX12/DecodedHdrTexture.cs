using System;

namespace FastViewDX12;

/// <summary>
/// Immutable CPU representation of a linear RGBA floating-point environment image.
/// </summary>
public sealed class DecodedHdrTexture
{
    /// <summary>Gets the image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the image height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets tightly packed linear RGBA values in row-major order.</summary>
    public float[] Rgba32Float { get; }

    /// <summary>
    /// Validates dimensions and stores an owned RGBA float array.
    /// </summary>
    public DecodedHdrTexture(int width, int height, float[] rgba32Float)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        ArgumentNullException.ThrowIfNull(rgba32Float);

        int expectedLength = checked(width * height * 4);

        if (rgba32Float.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Expected {expectedLength} float values, but received {rgba32Float.Length}.",
                nameof(rgba32Float));
        }

        Width = width;
        Height = height;
        Rgba32Float = rgba32Float;
    }
}
