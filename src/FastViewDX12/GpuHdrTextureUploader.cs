using System;
using System.Collections.Generic;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace FastViewDX12;

/// <summary>
/// Generates an HDR mip chain on the CPU and uploads it as a shader-readable RGBA16-float Direct3D texture.
/// </summary>
public static class GpuHdrTextureUploader
{
    private sealed class HdrMipLevel
    {
        public int Width { get; }

        public int Height { get; }

        public float[] Pixels { get; }

        public HdrMipLevel(
            int width,
            int height,
            float[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }
    }

    /// <summary>
    /// Creates the destination texture, uploads every subresource, transitions it to shader-resource state, and returns its mip count.
    /// </summary>
    public static unsafe ID3D12Resource UploadRgba16FloatMipChain(
        ID3D12Device device,
        ID3D12CommandQueue commandQueue,
        Action waitForGpu,
        DecodedHdrTexture source,
        out int mipCount)
    {
        ArgumentNullException.ThrowIfNull(
            device);

        ArgumentNullException.ThrowIfNull(
            commandQueue);

        ArgumentNullException.ThrowIfNull(
            waitForGpu);

        ArgumentNullException.ThrowIfNull(
            source);

        List<HdrMipLevel> mipLevels =
            BuildMipChain(
                source);

        mipCount =
            mipLevels.Count;

        ushort[] halfPixels =
            ConvertMipChainToHalf(
                mipLevels,
                out int[] mipOffsets);

        ResourceDescription textureDescription =
            ResourceDescription.Texture2D(
                Format.R16G16B16A16_Float,
                (uint)source.Width,
                (uint)source.Height,
                1,
                (ushort)mipCount,
                1,
                0,
                ResourceFlags.None);

        ID3D12Resource texture =
            device.CreateCommittedResource(
                new HeapProperties(
                    HeapType.Default),
                HeapFlags.None,
                textureDescription,
                ResourceStates.CopyDest);

        ulong uploadSize =
            device.GetRequiredIntermediateSize(
                texture,
                0,
                (uint)mipCount);

        using ID3D12Resource uploadBuffer =
            device.CreateCommittedResource(
                new HeapProperties(
                    HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(
                    uploadSize),
                ResourceStates.GenericRead);

        using ID3D12CommandAllocator allocator =
            device.CreateCommandAllocator(
                CommandListType.Direct);

        using ID3D12GraphicsCommandList commandList =
            device.CreateCommandList<ID3D12GraphicsCommandList>(
                0,
                CommandListType.Direct,
                allocator);

        fixed (ushort* pixelBase =
               halfPixels)
        {
            SubresourceData* subresources =
                stackalloc SubresourceData[
                    mipCount];

            for (int mipIndex = 0;
                 mipIndex < mipCount;
                 mipIndex++)
            {
                HdrMipLevel mip =
                    mipLevels[mipIndex];

                subresources[mipIndex] =
                    new SubresourceData
                    {
                        pData =
                            pixelBase +
                            mipOffsets[mipIndex],

                        RowPitch =
                            (nint)(
                                mip.Width *
                                4 *
                                sizeof(ushort)),

                        SlicePitch =
                            (nint)(
                                mip.Width *
                                mip.Height *
                                4 *
                                sizeof(ushort))
                    };
            }

            commandList.UpdateSubresources(
                texture,
                uploadBuffer,
                0,
                0,
                (uint)mipCount,
                subresources);
        }

        commandList.ResourceBarrierTransition(
            texture,
            ResourceStates.CopyDest,
            ResourceStates.PixelShaderResource);

        commandList.Close();

        commandQueue.ExecuteCommandList(
            commandList);

        waitForGpu();

        return texture;
    }

    /// <summary>
    /// Builds successively smaller HDR images down to one texel.
    /// </summary>
    private static List<HdrMipLevel> BuildMipChain(
        DecodedHdrTexture source)
    {
        var result =
            new List<HdrMipLevel>();

        var current =
            new HdrMipLevel(
                source.Width,
                source.Height,
                source.Rgba32Float);

        result.Add(
            current);

        while (current.Width > 1 ||
               current.Height > 1)
        {
            current =
                CreateNextMip(
                    current);

            result.Add(
                current);
        }

        return result;
    }

    /// <summary>
    /// Downsamples one mip level using a box filter that handles odd dimensions.
    /// </summary>
    private static HdrMipLevel CreateNextMip(
        HdrMipLevel source)
    {
        int targetWidth =
            Math.Max(
                1,
                source.Width / 2);

        int targetHeight =
            Math.Max(
                1,
                source.Height / 2);

        var targetPixels =
            new float[
                targetWidth *
                targetHeight *
                4];

        for (int y = 0;
             y < targetHeight;
             y++)
        {
            int sourceY0 =
                Math.Min(
                    y * 2,
                    source.Height - 1);

            int sourceY1 =
                Math.Min(
                    sourceY0 + 1,
                    source.Height - 1);

            for (int x = 0;
                 x < targetWidth;
                 x++)
            {
                int sourceX0 =
                    Math.Min(
                        x * 2,
                        source.Width - 1);

                int sourceX1 =
                    Math.Min(
                        sourceX0 + 1,
                        source.Width - 1);

                int targetIndex =
                    (
                        y *
                        targetWidth +
                        x
                    ) * 4;

                for (int channel = 0;
                     channel < 4;
                     channel++)
                {
                    float value00 =
                        GetPixelChannel(
                            source,
                            sourceX0,
                            sourceY0,
                            channel);

                    float value10 =
                        GetPixelChannel(
                            source,
                            sourceX1,
                            sourceY0,
                            channel);

                    float value01 =
                        GetPixelChannel(
                            source,
                            sourceX0,
                            sourceY1,
                            channel);

                    float value11 =
                        GetPixelChannel(
                            source,
                            sourceX1,
                            sourceY1,
                            channel);

                    targetPixels[
                        targetIndex +
                        channel] =
                        (
                            value00 +
                            value10 +
                            value01 +
                            value11
                        ) * 0.25f;
                }

                targetPixels[
                    targetIndex + 3] =
                    1.0f;
            }
        }

        return new HdrMipLevel(
            targetWidth,
            targetHeight,
            targetPixels);
    }

    /// <summary>
    /// Reads one channel while clamping coordinates to the source image.
    /// </summary>
    private static float GetPixelChannel(
        HdrMipLevel source,
        int x,
        int y,
        int channel)
    {
        int index =
            (
                y *
                source.Width +
                x
            ) * 4 +
            channel;

        return source.Pixels[index];
    }

    /// <summary>
    /// Packs all float mip pixels into IEEE 16-bit floating-point channel data.
    /// </summary>
    private static ushort[] ConvertMipChainToHalf(
        IReadOnlyList<HdrMipLevel> mipLevels,
        out int[] mipOffsets)
    {
        mipOffsets =
            new int[mipLevels.Count];

        int totalValueCount =
            0;

        for (int i = 0;
             i < mipLevels.Count;
             i++)
        {
            mipOffsets[i] =
                totalValueCount;

            totalValueCount =
                checked(
                    totalValueCount +
                    mipLevels[i].Pixels.Length);
        }

        var result =
            new ushort[totalValueCount];

        for (int mipIndex = 0;
             mipIndex < mipLevels.Count;
             mipIndex++)
        {
            float[] sourcePixels =
                mipLevels[mipIndex].Pixels;

            int destinationOffset =
                mipOffsets[mipIndex];

            for (int i = 0;
                 i < sourcePixels.Length;
                 i++)
            {
                float value =
                    sourcePixels[i];

                if (!float.IsFinite(value))
                {
                    value = 0.0f;
                }

                value =
                    Math.Clamp(
                        value,
                        0.0f,
                        65504.0f);

                result[
                    destinationOffset +
                    i] =
                    BitConverter.HalfToUInt16Bits(
                        (Half)value);
            }
        }

        return result;
    }
}