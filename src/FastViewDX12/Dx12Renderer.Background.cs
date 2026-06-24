using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using DrawingColor = System.Drawing.Color;

namespace FastViewDX12;

/// <summary>
/// Environment-map upload, image-based lighting controls, and visible background rendering.
/// </summary>
public sealed partial class Dx12Renderer
{
    /// <summary>
    /// Creates the full-screen environment-background pipeline and its constant buffer.
    /// </summary>
    private void CreateBackgroundPipeline()
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
                    "BackgroundVS.cso"));

        byte[] pixelShader =
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Shaders",
                    "BackgroundPS.cso"));

        var environmentSrvRange =
            new DescriptorRange1(
                DescriptorRangeType.ShaderResourceView,
                1,
                0,
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
                        ShaderVisibility.All),

                    new RootParameter1(
                        new RootDescriptorTable1(
                            environmentSrvRange),
                        ShaderVisibility.Pixel)
                ],
                [
                    new StaticSamplerDescription(
                        SamplerDescription.LinearWrap,
                        ShaderVisibility.Pixel,
                        0,
                        0)
                ]);

        _backgroundRootSignature =
            _device.CreateRootSignature(
                rootSignatureDescription);

        var pipelineDescription =
            new GraphicsPipelineStateDescription
            {
                RootSignature =
                    _backgroundRootSignature,

                VertexShader =
                    vertexShader,

                PixelShader =
                    pixelShader,

                BlendState =
                    BlendDescription.Opaque,

                RasterizerState =
                    RasterizerDescription.CullNone,

                DepthStencilState =
                    DepthStencilDescription.Read,

                DepthStencilFormat =
                    DepthFormat,

                InputLayout =
                    new InputLayoutDescription(
                        Array.Empty<InputElementDescription>()),

                PrimitiveTopologyType =
                    PrimitiveTopologyType.Triangle,

                RenderTargetFormats =
                [
                    BackBufferFormat
                ],

                SampleDescription =
                    new SampleDescription(
                        1,
                        0),

                SampleMask =
                    uint.MaxValue
            };

        _backgroundPipelineState =
            _device.CreateGraphicsPipelineState(
                pipelineDescription);

        const int constantBufferSize =
            256;

        _backgroundConstantBuffer =
            _device.CreateCommittedResource(
                new HeapProperties(
                    HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(
                    constantBufferSize),
                ResourceStates.GenericRead);
    }

    private static DecodedHdrTexture CreateFallbackEnvironment()
    {
        const int width = 4;
        const int height = 2;

        float[] pixels =
        [
            // Obere Reihe: Himmel
            0.45f, 0.55f, 0.75f, 1.0f,
        0.45f, 0.55f, 0.75f, 1.0f,
        0.45f, 0.55f, 0.75f, 1.0f,
        0.45f, 0.55f, 0.75f, 1.0f,

        // Untere Reihe: Boden
        0.06f, 0.05f, 0.04f, 1.0f,
        0.06f, 0.05f, 0.04f, 1.0f,
        0.06f, 0.05f, 0.04f, 1.0f,
        0.06f, 0.05f, 0.04f, 1.0f
        ];

        return new DecodedHdrTexture(
            width,
            height,
            pixels);
    }

    private void CreateEnvironmentSrv()
    {
        if (_device == null ||
            _srvHeap == null ||
            _environmentTexture == null)
        {
            return;
        }

        var srvDescription =
            new ShaderResourceViewDescription
            {
                Shader4ComponentMapping =
                    ShaderComponentMapping.Default,

                Format =
                    Format.R16G16B16A16_Float,

                ViewDimension =
                    Vortice.Direct3D12
                        .ShaderResourceViewDimension.Texture2D
            };

        srvDescription.Texture2D.MipLevels =
      (uint)Math.Max(
          1,
          _environmentMipCount);

        CpuDescriptorHandle handle =
            _srvHeap
                .GetCPUDescriptorHandleForHeapStart();

        handle.Ptr +=
            EnvironmentTextureSlot *
            _srvDescriptorSize;

        _device.CreateShaderResourceView(
            _environmentTexture,
            srvDescription,
            handle);
    }

    private void SetEnvironmentTexture(
     DecodedHdrTexture decodedTexture)
    {
        if (_device == null ||
            _commandQueue == null ||
            _srvHeap == null ||
            _fence == null)
        {
            throw new InvalidOperationException(
                "Renderer is not ready for environment textures.");
        }

        WaitForGpu();

        ID3D12Resource newTexture =
            GpuHdrTextureUploader
                .UploadRgba16FloatMipChain(
                    _device,
                    _commandQueue,
                    SignalAndWait,
                    decodedTexture,
                    out int newMipCount);

        _environmentTexture?.Dispose();

        _environmentTexture =
            newTexture;

        _environmentMipCount =
            newMipCount;

        CreateEnvironmentSrv();

        Debug.WriteLine(
            $"Environment GPU texture ready: " +
            $"{decodedTexture.Width} x " +
            $"{decodedTexture.Height}, " +
            $"mips={_environmentMipCount}");
    }

    /// <summary>
    /// Decodes an EXR environment map, uploads its mip chain, and makes it available for lighting and the visible background.
    /// </summary>
    /// <param name="path">Path to an OpenEXR image.</param>
    public void LoadEnvironmentMap(
    string path)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "Renderer must be initialized before loading an environment map.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Environment-map path is empty.",
                nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Environment map was not found.",
                path);
        }

        string extension =
            Path.GetExtension(path);

        if (!extension.Equals(
            ".exr",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "Only EXR environment maps are supported at the moment.");
        }

        Debug.WriteLine(
            $"Loading environment map: {path}");

        DecodedHdrTexture decodedTexture =
            ExrTextureDecoder.DecodeFile(
                path);

        SetEnvironmentTexture(
            decodedTexture);

        _environmentMapPath =
            path;

        Debug.WriteLine(
            $"Environment map loaded: " +
            $"{decodedTexture.Width} x " +
            $"{decodedTexture.Height}");
    }

    /// <summary>
    /// Draws a full-screen triangle that samples the equirectangular environment map.
    /// </summary>
    private void DrawEnvironmentBackground(
        int width,
        int height)
    {
        if (_commandList == null ||
            _srvHeap == null ||
            _backgroundRootSignature == null ||
            _backgroundPipelineState == null ||
            _backgroundConstantBuffer == null)
        {
            return;
        }

        UpdateBackgroundConstants(
            width,
            height);

        _commandList.SetGraphicsRootSignature(
            _backgroundRootSignature);

        _commandList.SetPipelineState(
            _backgroundPipelineState);

        _commandList.SetGraphicsRootConstantBufferView(
            0,
            _backgroundConstantBuffer.GPUVirtualAddress);

        GpuDescriptorHandle environmentHandle =
            _srvHeap
                .GetGPUDescriptorHandleForHeapStart();

        environmentHandle.Ptr +=
            EnvironmentTextureSlot *
            _srvDescriptorSize;

        _commandList.SetGraphicsRootDescriptorTable(
            1,
            environmentHandle);

        _commandList.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        _commandList.DrawInstanced(
            3,
            1,
            0,
            0);
    }

    /// <summary>
    /// Builds camera-basis constants used to reconstruct a view ray for every background pixel.
    /// </summary>
    private void UpdateBackgroundConstants(
        int width,
        int height)
    {
        if (_backgroundConstantBuffer == null)
        {
            return;
        }

        _camera.GetViewBasis(
            out System.Numerics.Vector3 forward,
            out System.Numerics.Vector3 right,
            out System.Numerics.Vector3 up);

        const float verticalFieldOfView =
            MathF.PI / 4.0f;

        float tanHalfFieldOfView =
            MathF.Tan(
                verticalFieldOfView *
                0.5f);

        float aspect =
            Math.Max(
                1,
                width) /
            (float)Math.Max(
                1,
                height);

        var constants =
            new BackgroundShaderConstants
            {
                CameraForwardAndTanHalfFov =
                    new System.Numerics.Vector4(
                        forward,
                        tanHalfFieldOfView),

                CameraRightAndAspect =
                    new System.Numerics.Vector4(
                        right,
                        aspect),

                CameraUpAndEnvironmentRotation =
                    new System.Numerics.Vector4(
                        up,
                        _light.GetEnvironmentRotationRadians()),

                SolidColorAndOpacity =
                    new System.Numerics.Vector4(
                        _backgroundColor.R / 255.0f,
                        _backgroundColor.G / 255.0f,
                        _backgroundColor.B / 255.0f,
                        _environmentBackgroundOpacity)
            };

        _backgroundConstantBuffer.SetData(
            in constants);
    }

    /// <summary>
    /// Enables or disables image-based environment lighting while retaining the loaded environment texture.
    /// </summary>
    public void SetEnvironmentLightingEnabled(
    bool enabled)
    {
        _environmentLightingEnabled =
            enabled;
    }

    /// <summary>
    /// Sets the image-based lighting multiplier after clamping it to a safe non-negative range.
    /// </summary>
    public void SetEnvironmentIntensity(
        float intensity)
    {
        _environmentIntensity =
            Math.Clamp(
                intensity,
                0.0f,
                3.0f);
    }

    /// <summary>
    /// Selects whether the viewer clears to a solid color or draws the environment map behind the model.
    /// </summary>
    /// <param name="mode">Background mode to activate.</param>
    public void SetBackgroundMode(
        ViewerBackgroundMode mode)
    {
        _backgroundMode =
            mode;
    }

    /// <summary>
    /// Sets the solid clear color and the color blended behind a partially opaque environment background.
    /// </summary>
    /// <param name="color">New background color.</param>
    public void SetBackgroundColor(
        DrawingColor color)
    {
        _backgroundColor =
            DrawingColor.FromArgb(
                color.R,
                color.G,
                color.B);
    }

    /// <summary>
    /// Sets the visible opacity of the environment background without changing environment-light intensity.
    /// </summary>
    /// <param name="opacity">Opacity clamped to the range 0 through 1.</param>
    public void SetEnvironmentBackgroundOpacity(
        float opacity)
    {
        _environmentBackgroundOpacity =
            Math.Clamp(
                opacity,
                0.0f,
                1.0f);
    }

    /// <summary>
    /// Enables an alpha-zero render target for Explorer thumbnail generation.
    /// </summary>
    /// <param name="enabled">True for transparent preview output.</param>
    public void SetTransparentBackground(
        bool enabled)
    {
        _transparentBackground =
            enabled;
    }

}
