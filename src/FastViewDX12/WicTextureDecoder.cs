using Vortice.WIC;
using WicPixelFormat = Vortice.WIC.PixelFormat;

namespace FastViewDX12;

internal static class WicTextureDecoder
{
    public static DecodedTexture Decode(
        byte[] encodedImage,
        int? maximumDimension)
    {
        using var stream =
            new MemoryStream(
                encodedImage,
                writable: false);

        using var factory = new IWICImagingFactory();
        using IWICBitmapDecoder decoder =
            factory.CreateDecoderFromStream(
                stream,
                DecodeOptions.CacheOnDemand);

        using IWICBitmapFrameDecode frame =
            decoder.GetFrame(0);

        (int width, int height) = CalculateOutputSize(
            frame.Size.Width,
            frame.Size.Height,
            maximumDimension);

        if ((width != frame.Size.Width ||
             height != frame.Size.Height) &&
            TryDecodeWithSourceTransform(
                frame,
                width,
                height,
                out DecodedTexture? transformed))
        {
            return transformed!;
        }

        IWICBitmapScaler? scaler = null;

        try
        {
            IWICBitmapSource source = frame;

            if (width != frame.Size.Width ||
                height != frame.Size.Height)
            {
                scaler = factory.CreateBitmapScaler();
                scaler.Initialize(
                    frame,
                    (uint)width,
                    (uint)height,
                    BitmapInterpolationMode.Fant);

                source = scaler;
            }

            using IWICFormatConverter converter =
                factory.CreateFormatConverter();

            converter.Initialize(
                source,
                WicPixelFormat.Format32bppRGBA)
                .CheckError();

            byte[] rgba =
                new byte[checked(width * height * 4)];

            converter.CopyPixels(
                checked((uint)(width * 4)),
                rgba);

            return new DecodedTexture(
                width,
                height,
                rgba);
        }
        finally
        {
            scaler?.Dispose();
        }
    }

    public static Size GetDimensions(byte[] encodedImage)
    {
        using var stream =
            new MemoryStream(
                encodedImage,
                writable: false);

        using var factory = new IWICImagingFactory();
        using IWICBitmapDecoder decoder =
            factory.CreateDecoderFromStream(
                stream,
                DecodeOptions.CacheOnDemand);

        using IWICBitmapFrameDecode frame =
            decoder.GetFrame(0);

        return new Size(
            frame.Size.Width,
            frame.Size.Height);
    }

    private static bool TryDecodeWithSourceTransform(
        IWICBitmapFrameDecode frame,
        int requestedWidth,
        int requestedHeight,
        out DecodedTexture? texture)
    {
        texture = null;

        using IWICBitmapSourceTransform? transform =
            frame.QueryInterfaceOrNull<IWICBitmapSourceTransform>();

        if (transform == null)
        {
            return false;
        }

        uint width = (uint)requestedWidth;
        uint height = (uint)requestedHeight;
        Guid pixelFormat =
            WicPixelFormat.Format32bppRGBA;

        transform.GetClosestSize(
            ref width,
            ref height);

        transform.GetClosestPixelFormat(
            ref pixelFormat);

        if (width == 0 ||
            height == 0 ||
            pixelFormat != WicPixelFormat.Format32bppRGBA)
        {
            return false;
        }

        uint stride = checked(width * 4);
        byte[] rgba =
            new byte[checked((int)(stride * height))];

        transform.CopyPixels(
            null,
            width,
            height,
            pixelFormat,
            BitmapTransformOptions.Rotate0,
            stride,
            (uint)rgba.Length,
            rgba);

        texture = new DecodedTexture(
            (int)width,
            (int)height,
            rgba);

        return true;
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
}

