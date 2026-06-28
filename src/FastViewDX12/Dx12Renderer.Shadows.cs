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
/// Directional-light shadow mapping. The shadow map is rebuilt only when the
/// scene or light direction changes, so camera orbiting does not redraw the
/// 2048-pixel depth map every frame.
/// </summary>
public sealed partial class Dx12Renderer
{
    private const int ShadowMapSize =
        2048;

    private bool _shadowsEnabled;

    private float _shadowStrength =
        0.78f;

    private float _shadowSoftness =
        1.0f;

    private const float ShadowReceiverBias =
        0.0012f;

    private bool _shadowMapDirty =
        true;

    private bool _shadowMapValid;

    private Matrix4x4 _lightViewProjection =
        Matrix4x4.Identity;

    private ID3D12RootSignature? _shadowRootSignature;

    private ID3D12PipelineState? _shadowPipelineState;

    private ID3D12Resource? _shadowConstantBuffer;

    private ID3D12Resource? _shadowMap;

    private ID3D12DescriptorHeap? _shadowDsvHeap;

    private Viewport _shadowViewport;

    private RectI _shadowScissorRect;

    [StructLayout(
        LayoutKind.Sequential,
        Pack = 4)]
    private struct ShadowShaderConstants
    {
        public Matrix4x4 LightViewProjection;
    }

    /// <summary>Enables or disables directional shadows in the viewport.</summary>
    public void SetShadowsEnabled(
        bool enabled)
    {
        if (_shadowsEnabled ==
            enabled)
        {
            return;
        }

        _shadowsEnabled =
            enabled;

        if (enabled)
        {
            MarkShadowMapDirty();
        }
    }

    /// <summary>Sets how strongly the directional shadow darkens direct light.</summary>
    public void SetShadowStrength(
        float strength)
    {
        _shadowStrength =
            float.IsFinite(strength)
                ? Math.Clamp(
                    strength,
                    0.0f,
                    1.0f)
                : 0.78f;
    }

    /// <summary>
    /// Sets the PCF sample radius in shadow-map texels. Zero gives a hard edge;
    /// larger values spread the same nine taps farther apart.
    /// </summary>
    public void SetShadowSoftness(
        float softness)
    {
        _shadowSoftness =
            float.IsFinite(softness)
                ? Math.Clamp(
                    softness,
                    0.0f,
                    3.0f)
                : 1.0f;
    }

    private void MarkShadowMapDirty()
    {
        _shadowMapDirty =
            true;
    }

    private void CreateShadowPipeline()
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
                    "ShadowVS.cso"));

        var rootSignatureDescription =
            new RootSignatureDescription1(
                RootSignatureFlags
                    .AllowInputAssemblerInputLayout,
                [
                    new RootParameter1(
                        RootParameterType
                            .ConstantBufferView,
                        new RootDescriptor1(
                            0,
                            0),
                        ShaderVisibility.Vertex)
                ],
                Array.Empty<StaticSamplerDescription>());

        _shadowRootSignature =
            _device.CreateRootSignature(
                rootSignatureDescription);

        InputElementDescription[] inputElements =
        [
            new InputElementDescription(
                "POSITION",
                0,
                Format.R32G32B32_Float,
                0,
                0)
        ];

        RasterizerDescription rasterizer =
            RasterizerDescription.CullNone;

        // A small rasterizer bias plus the receiver bias in the material shader
        // prevents most self-shadowing without visibly detaching the shadow.
        rasterizer.DepthBias =
            900;

        rasterizer.SlopeScaledDepthBias =
            1.5f;

        rasterizer.DepthBiasClamp =
            0.0f;

        var description =
            new GraphicsPipelineStateDescription
            {
                RootSignature =
                    _shadowRootSignature,

                VertexShader =
                    vertexShader,

                BlendState =
                    BlendDescription.Opaque,

                RasterizerState =
                    rasterizer,

                DepthStencilState =
                    DepthStencilDescription.Default,

                DepthStencilFormat =
                    DepthFormat,

                InputLayout =
                    new InputLayoutDescription(
                        inputElements),

                PrimitiveTopologyType =
                    PrimitiveTopologyType.Triangle,

                RenderTargetFormats =
                    Array.Empty<Format>(),

                SampleDescription =
                    new SampleDescription(
                        1,
                        0),

                SampleMask =
                    uint.MaxValue
            };

        _shadowPipelineState =
            _device.CreateGraphicsPipelineState(
                description);

        const int constantBufferSize =
            256;

        _shadowConstantBuffer =
            _device.CreateCommittedResource(
                new HeapProperties(
                    HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(
                    constantBufferSize),
                ResourceStates.GenericRead);
    }

    private void CreateShadowResources()
    {
        if (_device == null)
        {
            throw new InvalidOperationException(
                "Device is not initialized.");
        }

        _shadowDsvHeap?.Dispose();
        _shadowMap?.Dispose();

        _shadowDsvHeap =
            _device.CreateDescriptorHeap(
                new DescriptorHeapDescription(
                    DescriptorHeapType.DepthStencilView,
                    1));

        ResourceDescription shadowDescription =
            ResourceDescription.Texture2D(
                Format.R32_Typeless,
                ShadowMapSize,
                ShadowMapSize,
                1,
                0,
                1,
                0,
                ResourceFlags.AllowDepthStencil);

        var clearValue =
            new ClearValue(
                DepthFormat,
                new DepthStencilValue(
                    1.0f,
                    0));

        _shadowMap =
            _device.CreateCommittedResource(
                new HeapProperties(
                    HeapType.Default),
                HeapFlags.None,
                shadowDescription,
                ResourceStates.PixelShaderResource,
                clearValue);

        var dsvDescription =
            new DepthStencilViewDescription
            {
                Format =
                    DepthFormat,

                ViewDimension =
                    DepthStencilViewDimension.Texture2D
            };

        _device.CreateDepthStencilView(
            _shadowMap,
            dsvDescription,
            _shadowDsvHeap
                .GetCPUDescriptorHandleForHeapStart());

        _shadowViewport =
            new Viewport(
                0,
                0,
                ShadowMapSize,
                ShadowMapSize,
                0.0f,
                1.0f);

        _shadowScissorRect =
            new RectI(
                0,
                0,
                ShadowMapSize,
                ShadowMapSize);

        _shadowMapDirty =
            true;

        _shadowMapValid =
            false;
    }

    /// <summary>
    /// Recreates the shadow-map SRV whenever the shared material descriptor heap
    /// is rebuilt after a scene load.
    /// </summary>
    private void CreateShadowSrv()
    {
        if (_device == null ||
            _srvHeap == null ||
            _shadowMap == null)
        {
            return;
        }

        var srvDescription =
            new ShaderResourceViewDescription
            {
                Shader4ComponentMapping =
                    ShaderComponentMapping.Default,

                Format =
                    Format.R32_Float,

                ViewDimension =
                    Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D
            };

        srvDescription.Texture2D.MipLevels =
            1;

        CpuDescriptorHandle handle =
            _srvHeap
                .GetCPUDescriptorHandleForHeapStart();

        handle.Ptr +=
            ShadowTextureSlot *
            _srvDescriptorSize;

        _device.CreateShaderResourceView(
            _shadowMap,
            srvDescription,
            handle);
    }

    private void RenderShadowMapIfNeeded()
    {
        if (!_shadowsEnabled ||
            !_directLightEnabled ||
            !_shadowMapDirty ||
            _commandList == null ||
            _shadowMap == null ||
            _shadowDsvHeap == null ||
            _shadowRootSignature == null ||
            _shadowPipelineState == null ||
            _shadowConstantBuffer == null)
        {
            return;
        }

        if (!TryCreateLightViewProjection(
                out Matrix4x4 lightViewProjection))
        {
            _shadowMapValid =
                false;

            _shadowMapDirty =
                false;

            return;
        }

        _lightViewProjection =
            lightViewProjection;

        var constants =
            new ShadowShaderConstants
            {
                LightViewProjection =
                    Matrix4x4.Transpose(
                        lightViewProjection)
            };

        _shadowConstantBuffer.SetData(
            in constants);

        _commandList.ResourceBarrierTransition(
            _shadowMap,
            ResourceStates.PixelShaderResource,
            ResourceStates.DepthWrite);

        CpuDescriptorHandle shadowDsv =
            _shadowDsvHeap
                .GetCPUDescriptorHandleForHeapStart();

        _commandList.OMSetRenderTargets(
            ReadOnlySpan<CpuDescriptorHandle>.Empty,
            shadowDsv);

        _commandList.ClearDepthStencilView(
            shadowDsv,
            ClearFlags.Depth,
            1.0f,
            0);

        _commandList.RSSetViewport(
            _shadowViewport);

        _commandList.RSSetScissorRect(
            in _shadowScissorRect);

        _commandList.SetGraphicsRootSignature(
            _shadowRootSignature);

        _commandList.SetPipelineState(
            _shadowPipelineState);

        _commandList.SetGraphicsRootConstantBufferView(
            0,
            _shadowConstantBuffer.GPUVirtualAddress);

        _commandList.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        foreach (GpuRenderItem item in
                 _opaqueItems)
        {
            DrawShadowRenderItem(
                item);
        }

        _commandList.ResourceBarrierTransition(
            _shadowMap,
            ResourceStates.DepthWrite,
            ResourceStates.PixelShaderResource);

        _shadowMapDirty =
            false;

        _shadowMapValid =
            true;
    }

    private void DrawShadowRenderItem(
        GpuRenderItem item)
    {
        if (_commandList == null)
        {
            return;
        }

        _commandList.IASetVertexBuffers(
            0,
            item.VertexBufferView);

        if (item.UsesIndexBuffer &&
            item.IndexBuffer != null)
        {
            _commandList.IASetIndexBuffer(
                item.IndexBufferView);

            _commandList.DrawIndexedInstanced(
                item.IndexCount,
                1,
                0,
                0,
                0);
        }
        else
        {
            _commandList.DrawInstanced(
                item.VertexCount,
                1,
                0,
                0);
        }
    }

    private bool TryCreateLightViewProjection(
        out Matrix4x4 lightViewProjection)
    {
        lightViewProjection =
            Matrix4x4.Identity;

        if (!TryCalculateOpaqueSceneBounds(
                out Vector3 minimum,
                out Vector3 maximum))
        {
            return false;
        }

        Vector3 center =
            (minimum + maximum) *
            0.5f;

        Vector3 size =
            maximum - minimum;

        float radius =
            size.Length() *
            0.5f;

        radius =
            MathF.Max(
                radius,
                0.5f);

        float extent =
            radius *
            1.18f;

        Vector3 directionToLight =
            _light.GetDirectionToLight();

        Vector3 eye =
            center +
            directionToLight *
            radius *
            3.25f;

        Vector3 up =
            MathF.Abs(
                Vector3.Dot(
                    directionToLight,
                    Vector3.UnitY)) >
            0.94f
                ? Vector3.UnitZ
                : Vector3.UnitY;

        Matrix4x4 view =
            Matrix4x4.CreateLookAt(
                eye,
                center,
                up);

        Matrix4x4 projection =
            Matrix4x4.CreateOrthographic(
                extent *
                2.0f,
                extent *
                2.0f,
                MathF.Max(
                    radius *
                    0.05f,
                    0.01f),
                radius *
                7.0f);

        lightViewProjection =
            view *
            projection;

        return true;
    }

    private bool TryCalculateOpaqueSceneBounds(
        out Vector3 minimum,
        out Vector3 maximum)
    {
        minimum =
            new Vector3(
                float.MaxValue);

        maximum =
            new Vector3(
                float.MinValue);

        bool found =
            false;

        foreach (GpuRenderItem item in
                 _opaqueItems)
        {
            foreach (Vector3 position in
                     item.Mesh.Positions)
            {
                minimum =
                    Vector3.Min(
                        minimum,
                        position);

                maximum =
                    Vector3.Max(
                        maximum,
                        position);

                found =
                    true;
            }
        }

        return found;
    }

    private Vector4 CreateShadowSettings()
    {
        float effectiveStrength =
            _shadowsEnabled &&
            _directLightEnabled &&
            _shadowMapValid
                ? _shadowStrength
                : 0.0f;

        return new Vector4(
            effectiveStrength,
            _shadowSoftness,
            ShadowReceiverBias,
            1.0f /
            ShadowMapSize);
    }

    private void DisposeShadowPipeline()
    {
        _shadowMap?.Dispose();
        _shadowDsvHeap?.Dispose();
        _shadowConstantBuffer?.Dispose();
        _shadowPipelineState?.Dispose();
        _shadowRootSignature?.Dispose();

        _shadowMap =
            null;

        _shadowDsvHeap =
            null;

        _shadowConstantBuffer =
            null;

        _shadowPipelineState =
            null;

        _shadowRootSignature =
            null;
    }
}
