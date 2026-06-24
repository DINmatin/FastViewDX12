namespace FastViewDX12;

/// <summary>
/// Immutable CPU representation of an 8-bit RGBA texture ready for GPU upload.
/// </summary>
public sealed class DecodedTexture
{
    /// <summary>Gets the image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the image height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets tightly packed RGBA bytes in row-major order.</summary>
    public byte[] Rgba8 { get; }

    /// <summary>
    /// Stores texture dimensions and RGBA pixel bytes.
    /// </summary>
    public DecodedTexture(int width, int height, byte[] rgba8)
    {
        Width = width;
        Height = height;
        Rgba8 = rgba8;
    }

    /// <summary>Creates the fallback base-color or emissive texture.</summary>
    public static DecodedTexture WhitePixel() => SolidColor(255, 255, 255, 255);

    /// <summary>Creates a tangent-space normal that points straight away from the surface.</summary>
    public static DecodedTexture NeutralNormalPixel() => SolidColor(128, 128, 255, 255);

    /// <summary>Creates the glTF-compatible fallback metallic-roughness texel.</summary>
    public static DecodedTexture DefaultMetallicRoughnessPixel() => SolidColor(255, 255, 255, 255);

    /// <summary>Creates a one-pixel RGBA texture.</summary>
    private static DecodedTexture SolidColor(byte red, byte green, byte blue, byte alpha)
    {
        return new DecodedTexture(1, 1, new[] { red, green, blue, alpha });
    }
}
