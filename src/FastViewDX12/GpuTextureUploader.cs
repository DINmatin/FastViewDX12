using System;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace FastViewDX12;

/// <summary>
/// Uploads one decoded RGBA8 image through an intermediate upload buffer into a Direct3D texture resource.
/// </summary>
public static class GpuTextureUploader
{
    /// <summary>
    /// Creates, fills, transitions, and returns a shader-readable RGBA8 texture.
    /// </summary>
    public static unsafe ID3D12Resource UploadRgba8(
        ID3D12Device device,
        ID3D12CommandQueue commandQueue,
        Action waitForGpu,
        DecodedTexture source)
    {
        if (device == null)
        {
            throw new ArgumentNullException(
                nameof(device));
        }

        if (commandQueue == null)
        {
            throw new ArgumentNullException(
                nameof(commandQueue));
        }

        if (source == null)
        {
            throw new ArgumentNullException(
                nameof(source));
        }

        ResourceDescription textureDescription =
            ResourceDescription.Texture2D(
                Format.R8G8B8A8_UNorm,
                (uint)source.Width,
                (uint)source.Height,
                1,
                1,
                1,
                0,
                ResourceFlags.None);

        ID3D12Resource texture =
            device.CreateCommittedResource(
                new HeapProperties(HeapType.Default),
                HeapFlags.None,
                textureDescription,
                ResourceStates.CopyDest);

        ulong uploadSize =
            device.GetRequiredIntermediateSize(
                texture,
                0,
                1);

        using ID3D12Resource uploadBuffer =
            device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(uploadSize),
                ResourceStates.GenericRead);

        using ID3D12CommandAllocator allocator =
            device.CreateCommandAllocator(
                CommandListType.Direct);

        using ID3D12GraphicsCommandList commandList =
            device.CreateCommandList<ID3D12GraphicsCommandList>(
                0,
                CommandListType.Direct,
                allocator);

        fixed (byte* pixelPointer =
               source.Rgba8)
        {
            var subresourceData =
                new SubresourceData
                {
                    pData = pixelPointer,
                    RowPitch =
                        (nint)(source.Width * 4),
                    SlicePitch =
                        (nint)(
                            source.Width *
                            source.Height *
                            4)
                };

            commandList.UpdateSubresources(
                texture,
                uploadBuffer,
                0,
                0,
                1,
                &subresourceData);
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
}