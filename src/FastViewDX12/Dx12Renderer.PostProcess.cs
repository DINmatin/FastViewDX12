using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace FastViewDX12;

/// <summary>
/// Screen-space post processing. Bloom copies the completed scene away from
/// the swap chain, extracts bright pixels, blurs them at half resolution, and
/// composites the result back over the original frame.
/// </summary>
public sealed partial class Dx12Renderer
{
    private const int PostProcessTextureCount =
        3;

    private const int SceneCopyTextureIndex =
        0;

    private const int BloomTextureAIndex =
        1;

    private const int BloomTextureBIndex =
        2;

    private static readonly Format BloomTextureFormat =
        Format.R16G16B16A16_Float;

    private bool _bloomEnabled;

    private float _bloomThreshold =
        0.78f;

    private float _bloomIntensity =
        0.8f;

    private float _bloomRadius =
        1.0f;

    private ID3D12RootSignature? _postProcessRootSignature;

    private ID3D12PipelineState? _bloomBrightPipelineState;

    private ID3D12PipelineState? _bloomBlurHorizontalPipelineState;

    private ID3D12PipelineState? _bloomBlurVerticalPipelineState;

    private ID3D12PipelineState? _bloomCompositePipelineState;

    private ID3D12DescriptorHeap? _postProcessRtvHeap;

    private ID3D12DescriptorHeap? _postProcessSrvHeap;

    private ID3D12Resource? _postProcessConstantBuffer;

    private ID3D12Resource? _postProcessBlurConstantBuffer;

    private ID3D12Resource? _sceneCopyTexture;

    private ID3D12Resource? _bloomTextureA;

    private ID3D12Resource? _bloomTextureB;

    private uint _postProcessRtvDescriptorSize;

    private uint _postProcessSrvDescriptorSize;

    private int _postProcessWidth;

    private int _postProcessHeight;

    [StructLayout(
        LayoutKind.Sequential,
        Pack = 4)]
    private struct PostProcessConstants
    {
        public Vector4 Settings;

        public Vector4 BloomSettings;
    }

    /// <summary>Enables or disables viewport bloom.</summary>
    public void SetBloomEnabled(
        bool enabled)
    {
        _bloomEnabled =
            enabled;
    }

    /// <summary>Sets the bright-pass threshold in the displayed 0..1 range.</summary>
    public void SetBloomThreshold(
        float threshold)
    {
        _bloomThreshold =
            float.IsFinite(threshold)
                ? Math.Clamp(
                    threshold,
                    0.0f,
                    1.0f)
                : 0.78f;
    }

    /// <summary>Sets the bloom contribution added to the original image.</summary>
    public void SetBloomIntensity(
        float intensity)
    {
        _bloomIntensity =
            float.IsFinite(intensity)
                ? Math.Clamp(
                    intensity,
                    0.0f,
                    3.0f)
                : 0.8f;
    }

    /// <summary>
    /// Sets the bloom spread. Zero keeps the bright pass sharp, while larger
    /// values use wider and repeated Gaussian blur passes.
    /// </summary>
    public void SetBloomRadius(
        float radius)
    {
        _bloomRadius =
            float.IsFinite(radius)
                ? Math.Clamp(
                    radius,
                    0.0f,
                    4.0f)
                : 1.0f;
    }

    private void CreatePostProcessPipeline()
    {
        if (_device == null)
        {
            throw new InvalidOperationException(
                "Device is not initialized.");
        }

        byte[] vertexShader =
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Shaders",
                    "PostProcessVS.cso"));

        byte[] brightPixelShader =
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Shaders",
                    "BloomBrightPS.cso"));

        byte[] blurHorizontalPixelShader =
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Shaders",
                    "BloomBlurHorizontalPS.cso"));

        byte[] blurVerticalPixelShader =
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Shaders",
                    "BloomBlurVerticalPS.cso"));

        byte[] compositePixelShader =
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Shaders",
                    "BloomCompositePS.cso"));

        var sourceSrvRange =
            new DescriptorRange1(
                DescriptorRangeType.ShaderResourceView,
                1,
                0,
                flags:
                    DescriptorRangeFlags.None);

        var bloomSrvRange =
            new DescriptorRange1(
                DescriptorRangeType.ShaderResourceView,
                1,
                1,
                flags:
                    DescriptorRangeFlags.None);

        var rootSignatureDescription =
            new RootSignatureDescription1(
                RootSignatureFlags.None,
                [
                    new RootParameter1(
                        RootParameterType.ConstantBufferView,
                        new RootDescriptor1(
                            0,
                            0),
                        ShaderVisibility.Pixel),

                    new RootParameter1(
                        new RootDescriptorTable1(
                            sourceSrvRange),
                        ShaderVisibility.Pixel),

                    new RootParameter1(
                        new RootDescriptorTable1(
                            bloomSrvRange),
                        ShaderVisibility.Pixel)
                ],
                [
                    new StaticSamplerDescription(
                        SamplerDescription.LinearClamp,
                        ShaderVisibility.Pixel,
                        0,
                        0)
                ]);

        _postProcessRootSignature =
            _device.CreateRootSignature(
                rootSignatureDescription);

        _bloomBrightPipelineState =
            CreatePostProcessPipelineState(
                vertexShader,
                brightPixelShader,
                BloomTextureFormat);

        _bloomBlurHorizontalPipelineState =
            CreatePostProcessPipelineState(
                vertexShader,
                blurHorizontalPixelShader,
                BloomTextureFormat);

        _bloomBlurVerticalPipelineState =
            CreatePostProcessPipelineState(
                vertexShader,
                blurVerticalPixelShader,
                BloomTextureFormat);

        _bloomCompositePipelineState =
            CreatePostProcessPipelineState(
                vertexShader,
                compositePixelShader,
                BackBufferFormat);

        const int constantBufferSize =
            256;

        _postProcessConstantBuffer =
            _device.CreateCommittedResource(
                new HeapProperties(
                    HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(
                    constantBufferSize),
                ResourceStates.GenericRead);

        _postProcessBlurConstantBuffer =
            _device.CreateCommittedResource(
                new HeapProperties(
                    HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(
                    constantBufferSize),
                ResourceStates.GenericRead);
    }

    private ID3D12PipelineState CreatePostProcessPipelineState(
        byte[] vertexShader,
        byte[] pixelShader,
        Format renderTargetFormat)
    {
        if (_device == null ||
            _postProcessRootSignature == null)
        {
            throw new InvalidOperationException(
                "Post-process pipeline is not initialized.");
        }

        var description =
            new GraphicsPipelineStateDescription
            {
                RootSignature =
                    _postProcessRootSignature,

                VertexShader =
                    vertexShader,

                PixelShader =
                    pixelShader,

                BlendState =
                    BlendDescription.Opaque,

                RasterizerState =
                    RasterizerDescription.CullNone,

                DepthStencilState =
                    DepthStencilDescription.None,

                DepthStencilFormat =
                    Format.Unknown,

                InputLayout =
                    new InputLayoutDescription(
                        Array.Empty<InputElementDescription>()),

                PrimitiveTopologyType =
                    PrimitiveTopologyType.Triangle,

                RenderTargetFormats =
                [
                    renderTargetFormat
                ],

                SampleDescription =
                    new SampleDescription(
                        1,
                        0),

                SampleMask =
                    uint.MaxValue
            };

        return _device.CreateGraphicsPipelineState(
            description);
    }

    private void CreatePostProcessResources(
        int width,
        int height)
    {
        if (_device == null)
        {
            return;
        }

        DisposePostProcessTextures();

        _postProcessWidth =
            Math.Max(
                1,
                width);

        _postProcessHeight =
            Math.Max(
                1,
                height);

        int bloomWidth =
            Math.Max(
                1,
                _postProcessWidth / 2);

        int bloomHeight =
            Math.Max(
                1,
                _postProcessHeight / 2);

        _postProcessRtvHeap =
            _device.CreateDescriptorHeap(
                new DescriptorHeapDescription(
                    DescriptorHeapType.RenderTargetView,
                    PostProcessTextureCount));

        _postProcessSrvHeap =
            _device.CreateDescriptorHeap(
                new DescriptorHeapDescription(
                    DescriptorHeapType
                        .ConstantBufferViewShaderResourceViewUnorderedAccessView,
                    PostProcessTextureCount,
                    DescriptorHeapFlags.ShaderVisible));

        _postProcessRtvDescriptorSize =
            _device.GetDescriptorHandleIncrementSize(
                DescriptorHeapType.RenderTargetView);

        _postProcessSrvDescriptorSize =
            _device.GetDescriptorHandleIncrementSize(
                DescriptorHeapType
                    .ConstantBufferViewShaderResourceViewUnorderedAccessView);

        _sceneCopyTexture =
            CreatePostProcessTexture(
                BackBufferFormat,
                _postProcessWidth,
                _postProcessHeight,
                SceneCopyTextureIndex);

        _bloomTextureA =
            CreatePostProcessTexture(
                BloomTextureFormat,
                bloomWidth,
                bloomHeight,
                BloomTextureAIndex);

        _bloomTextureB =
            CreatePostProcessTexture(
                BloomTextureFormat,
                bloomWidth,
                bloomHeight,
                BloomTextureBIndex);
    }

    private ID3D12Resource CreatePostProcessTexture(
        Format format,
        int width,
        int height,
        int descriptorIndex)
    {
        if (_device == null ||
            _postProcessRtvHeap == null ||
            _postProcessSrvHeap == null)
        {
            throw new InvalidOperationException(
                "Post-process descriptor heaps are not ready.");
        }

        ResourceDescription description =
            ResourceDescription.Texture2D(
                format,
                (uint)width,
                (uint)height,
                1,
                1,
                1,
                0,
                ResourceFlags.AllowRenderTarget);

        ID3D12Resource texture =
            _device.CreateCommittedResource(
                new HeapProperties(
                    HeapType.Default),
                HeapFlags.None,
                description,
                ResourceStates.PixelShaderResource);

        CpuDescriptorHandle rtvHandle =
            GetPostProcessRtvHandle(
                descriptorIndex);

        _device.CreateRenderTargetView(
            texture,
            null,
            rtvHandle);

        var srvDescription =
            new ShaderResourceViewDescription
            {
                Shader4ComponentMapping =
                    ShaderComponentMapping.Default,

                Format =
                    format,

                ViewDimension =
                    Vortice.Direct3D12
                        .ShaderResourceViewDimension.Texture2D
            };

        srvDescription.Texture2D.MipLevels =
            1;

        _device.CreateShaderResourceView(
            texture,
            srvDescription,
            GetPostProcessCpuSrvHandle(
                descriptorIndex));

        return texture;
    }

    /// <summary>
    /// Applies bloom to a completed back buffer that is currently in the
    /// RenderTarget state. The method leaves it in RenderTarget state so the
    /// existing preview-copy and Present transitions remain unchanged.
    /// </summary>
    private void ApplyBloom(
        ID3D12Resource backBuffer,
        int width,
        int height)
    {
        if (!_bloomEnabled ||
            _transparentBackground ||
            _commandList == null ||
            _postProcessRootSignature == null ||
            _bloomBrightPipelineState == null ||
            _bloomBlurHorizontalPipelineState == null ||
            _bloomBlurVerticalPipelineState == null ||
            _bloomCompositePipelineState == null ||
            _postProcessSrvHeap == null ||
            _postProcessConstantBuffer == null ||
            _postProcessBlurConstantBuffer == null ||
            _sceneCopyTexture == null ||
            _bloomTextureA == null ||
            _bloomTextureB == null)
        {
            return;
        }

        if (_postProcessWidth != width ||
            _postProcessHeight != height)
        {
            return;
        }

        int bloomWidth =
            Math.Max(
                1,
                width / 2);

        int bloomHeight =
            Math.Max(
                1,
                height / 2);

        // A larger radius is split over several ping-pong passes. This keeps
        // the Gaussian samples dense instead of producing separated ghost
        // copies at wide radii. Radius zero intentionally leaves the extracted
        // bright image sharp.
        int blurIterations =
            Math.Max(
                1,
                (int)MathF.Ceiling(
                    _bloomRadius));

        float blurRadiusPerPass =
            _bloomRadius /
            blurIterations;

        UpdatePostProcessConstants(
            _postProcessConstantBuffer,
            width,
            height,
            blurRadius: 0.0f);

        UpdatePostProcessConstants(
            _postProcessBlurConstantBuffer,
            bloomWidth,
            bloomHeight,
            blurRadiusPerPass);

        _commandList.ResourceBarrierTransition(
            _sceneCopyTexture,
            ResourceStates.PixelShaderResource,
            ResourceStates.CopyDest);

        _commandList.ResourceBarrierTransition(
            backBuffer,
            ResourceStates.RenderTarget,
            ResourceStates.CopySource);

        _commandList.CopyResource(
            _sceneCopyTexture,
            backBuffer);

        _commandList.ResourceBarrierTransition(
            backBuffer,
            ResourceStates.CopySource,
            ResourceStates.RenderTarget);

        _commandList.ResourceBarrierTransition(
            _sceneCopyTexture,
            ResourceStates.CopyDest,
            ResourceStates.PixelShaderResource);

        _commandList.ResourceBarrierTransition(
            _bloomTextureA,
            ResourceStates.PixelShaderResource,
            ResourceStates.RenderTarget);

        DrawPostProcessPass(
            _bloomBrightPipelineState,
            GetPostProcessRtvHandle(
                BloomTextureAIndex),
            bloomWidth,
            bloomHeight,
            SceneCopyTextureIndex,
            SceneCopyTextureIndex,
            width,
            height,
            _postProcessConstantBuffer);

        _commandList.ResourceBarrierTransition(
            _bloomTextureA,
            ResourceStates.RenderTarget,
            ResourceStates.PixelShaderResource);

        for (int iteration = 0;
             iteration < blurIterations;
             iteration++)
        {
            _commandList.ResourceBarrierTransition(
                _bloomTextureB,
                ResourceStates.PixelShaderResource,
                ResourceStates.RenderTarget);

            DrawPostProcessPass(
                _bloomBlurHorizontalPipelineState,
                GetPostProcessRtvHandle(
                    BloomTextureBIndex),
                bloomWidth,
                bloomHeight,
                BloomTextureAIndex,
                BloomTextureAIndex,
                bloomWidth,
                bloomHeight,
                _postProcessBlurConstantBuffer);

            _commandList.ResourceBarrierTransition(
                _bloomTextureB,
                ResourceStates.RenderTarget,
                ResourceStates.PixelShaderResource);

            _commandList.ResourceBarrierTransition(
                _bloomTextureA,
                ResourceStates.PixelShaderResource,
                ResourceStates.RenderTarget);

            DrawPostProcessPass(
                _bloomBlurVerticalPipelineState,
                GetPostProcessRtvHandle(
                    BloomTextureAIndex),
                bloomWidth,
                bloomHeight,
                BloomTextureBIndex,
                BloomTextureBIndex,
                bloomWidth,
                bloomHeight,
                _postProcessBlurConstantBuffer);

            _commandList.ResourceBarrierTransition(
                _bloomTextureA,
                ResourceStates.RenderTarget,
                ResourceStates.PixelShaderResource);
        }

        CpuDescriptorHandle backBufferRtv =
            _rtvHeap!
                .GetCPUDescriptorHandleForHeapStart();

        backBufferRtv.Ptr +=
            _frameIndex *
            _rtvDescriptorSize;

        DrawPostProcessPass(
            _bloomCompositePipelineState,
            backBufferRtv,
            width,
            height,
            SceneCopyTextureIndex,
            BloomTextureAIndex,
            width,
            height,
            _postProcessConstantBuffer);
    }

    private void UpdatePostProcessConstants(
        ID3D12Resource constantBuffer,
        int sourceWidth,
        int sourceHeight,
        float blurRadius)
    {
        constantBuffer.SetData(
            new PostProcessConstants
            {
                Settings =
                    new Vector4(
                        1.0f /
                        Math.Max(
                            1,
                            sourceWidth),

                        1.0f /
                        Math.Max(
                            1,
                            sourceHeight),

                        _bloomThreshold,
                        _bloomIntensity),

                BloomSettings =
                    new Vector4(
                        blurRadius,
                        0.0f,
                        0.0f,
                        0.0f)
            });
    }

    private void DrawPostProcessPass(
        ID3D12PipelineState pipelineState,
        CpuDescriptorHandle targetRtv,
        int targetWidth,
        int targetHeight,
        int sourceTextureIndex,
        int bloomTextureIndex,
        int sourceWidth,
        int sourceHeight,
        ID3D12Resource constantBuffer)
    {
        if (_commandList == null ||
            _postProcessRootSignature == null ||
            _postProcessSrvHeap == null)
        {
            return;
        }

        var viewport =
            new Viewport(
                0,
                0,
                targetWidth,
                targetHeight,
                0.0f,
                1.0f);

        var scissor =
            new RectI(
                0,
                0,
                targetWidth,
                targetHeight);

        _commandList.OMSetRenderTargets(
            targetRtv);

        _commandList.RSSetViewport(
            viewport);

        _commandList.RSSetScissorRect(
            in scissor);

        _commandList.SetDescriptorHeaps(
            _postProcessSrvHeap);

        _commandList.SetGraphicsRootSignature(
            _postProcessRootSignature);

        _commandList.SetPipelineState(
            pipelineState);

        _commandList.SetGraphicsRootConstantBufferView(
            0,
            constantBuffer.GPUVirtualAddress);

        _commandList.SetGraphicsRootDescriptorTable(
            1,
            GetPostProcessGpuSrvHandle(
                sourceTextureIndex));

        _commandList.SetGraphicsRootDescriptorTable(
            2,
            GetPostProcessGpuSrvHandle(
                bloomTextureIndex));

        _commandList.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        _commandList.DrawInstanced(
            3,
            1,
            0,
            0);
    }

    private CpuDescriptorHandle GetPostProcessRtvHandle(
        int descriptorIndex)
    {
        CpuDescriptorHandle handle =
            _postProcessRtvHeap!
                .GetCPUDescriptorHandleForHeapStart();

        handle.Ptr +=
            (uint)descriptorIndex *
            _postProcessRtvDescriptorSize;

        return handle;
    }

    private CpuDescriptorHandle GetPostProcessCpuSrvHandle(
        int descriptorIndex)
    {
        CpuDescriptorHandle handle =
            _postProcessSrvHeap!
                .GetCPUDescriptorHandleForHeapStart();

        handle.Ptr +=
            (uint)descriptorIndex *
            _postProcessSrvDescriptorSize;

        return handle;
    }

    private GpuDescriptorHandle GetPostProcessGpuSrvHandle(
        int descriptorIndex)
    {
        GpuDescriptorHandle handle =
            _postProcessSrvHeap!
                .GetGPUDescriptorHandleForHeapStart();

        handle.Ptr +=
            (uint)descriptorIndex *
            _postProcessSrvDescriptorSize;

        return handle;
    }

    private void DisposePostProcessTextures()
    {
        _bloomTextureB?.Dispose();
        _bloomTextureA?.Dispose();
        _sceneCopyTexture?.Dispose();
        _postProcessSrvHeap?.Dispose();
        _postProcessRtvHeap?.Dispose();

        _bloomTextureB =
            null;

        _bloomTextureA =
            null;

        _sceneCopyTexture =
            null;

        _postProcessSrvHeap =
            null;

        _postProcessRtvHeap =
            null;

        _postProcessWidth =
            0;

        _postProcessHeight =
            0;
    }

    private void DisposePostProcessPipeline()
    {
        DisposePostProcessTextures();

        _postProcessBlurConstantBuffer?.Dispose();
        _postProcessConstantBuffer?.Dispose();
        _bloomCompositePipelineState?.Dispose();
        _bloomBlurVerticalPipelineState?.Dispose();
        _bloomBlurHorizontalPipelineState?.Dispose();
        _bloomBrightPipelineState?.Dispose();
        _postProcessRootSignature?.Dispose();

        _postProcessBlurConstantBuffer =
            null;

        _postProcessConstantBuffer =
            null;

        _bloomCompositePipelineState =
            null;

        _bloomBlurVerticalPipelineState =
            null;

        _bloomBlurHorizontalPipelineState =
            null;

        _bloomBrightPipelineState =
            null;

        _postProcessRootSignature =
            null;
    }
}
