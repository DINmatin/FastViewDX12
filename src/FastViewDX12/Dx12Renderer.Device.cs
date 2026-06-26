using System;
using System.Diagnostics;
using System.IO;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace FastViewDX12;

/// <summary>
/// Direct3D device creation, pipeline setup, swap-chain resize, descriptors, depth buffers, and GPU synchronization.
/// </summary>
public sealed partial class Dx12Renderer
{
    /// <summary>
    /// Creates the Direct3D device, swap chain, descriptor heaps, pipelines, depth buffer, and fallback resources.
    /// </summary>
    public void Initialize()
    {
        if (_initialized ||
            !_host.IsHandleCreated)
        {
            return;
        }

    #if DEBUG
        if (D3D12GetDebugInterface(
            out ID3D12Debug3? debug).Success &&
            debug != null)
        {
            debug.EnableDebugLayer();
            debug.SetEnableGPUBasedValidation(false);
            debug.Dispose();
        }
    #endif

        _factory =
            CreateDXGIFactory2<IDXGIFactory4>(
                false);

        IDXGIAdapter1? adapter =
            PickHardwareAdapter(
                _factory);

        _device =
            D3D12CreateDevice<ID3D12Device>(
                adapter,
                FeatureLevel.Level_11_0);

        adapter?.Dispose();

        _commandQueue =
            _device.CreateCommandQueue(
                new CommandQueueDescription(
                    CommandListType.Direct));

        for (int i = 0;
             i < FrameCount;
             i++)
        {
            _commandAllocators[i] =
                _device.CreateCommandAllocator(
                    CommandListType.Direct);
        }

        _commandList =
            _device.CreateCommandList<ID3D12GraphicsCommandList>(
                0,
                CommandListType.Direct,
                _commandAllocators[0]!);

        _commandList.Close();

        CreateSwapChain();
        CreateRtvHeapAndViews();
        CreateDepthBuffer();
        CreateSimplePipeline();
        CreateBackgroundPipeline();
        CreateSrvHeap(1);

        int width =
            Math.Max(
                1,
                _host.ClientSize.Width);

        int height =
            Math.Max(
                1,
                _host.ClientSize.Height);

        _camera.SetViewport(
            width,
            height);

        _viewport =
            new Viewport(
                0,
                0,
                width,
                height,
                0.0f,
                1.0f);

        _scissorRect =
            new RectI(
                0,
                0,
                width,
                height);

        _fence =
            _device.CreateFence(
                0);

        _fenceValue =
            1;
        SetEnvironmentTexture(
    CreateFallbackEnvironment());
        _initialized =
            true;

        Debug.WriteLine(
            "DX12 initialized.");
    }

    /// <summary>
    /// Creates the root signature and opaque/blended mesh pipeline states.
    /// </summary>
    private void CreateSimplePipeline()
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
                    "SimpleVS.cso"));

        byte[] pixelShader =
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Shaders",
                    "SimplePS.cso"));

        var materialSrvRange =
      new DescriptorRange1(
          DescriptorRangeType.ShaderResourceView,
          TextureDescriptorCountPerMaterial,
          0,
          flags:
              DescriptorRangeFlags.DataStatic);

        var environmentSrvRange =
            new DescriptorRange1(
                DescriptorRangeType.ShaderResourceView,
                1,
                4,
                flags:
                    DescriptorRangeFlags.None);

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
                        ShaderVisibility.All),

                   new RootParameter1(
    new RootDescriptorTable1(
        materialSrvRange),
    ShaderVisibility.Pixel),

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

        _rootSignature =
            _device.CreateRootSignature(
                rootSignatureDescription);

        InputElementDescription[] inputElements =
        [
            new InputElementDescription(
                "POSITION",
                0,
                Format.R32G32B32_Float,
                0,
                0),

            new InputElementDescription(
                "NORMAL",
                0,
                Format.R32G32B32_Float,
                12,
                0),

            new InputElementDescription(
                "TANGENT",
                0,
                Format.R32G32B32A32_Float,
                24,
                0),

            new InputElementDescription(
                "TEXCOORD",
                0,
                Format.R32G32_Float,
                40,
                0),

            new InputElementDescription(
                "TEXCOORD",
                1,
                Format.R32G32_Float,
                48,
                0),

            new InputElementDescription(
                "TEXCOORD",
                2,
                Format.R32G32_Float,
                56,
                0),

            new InputElementDescription(
                "TEXCOORD",
                3,
                Format.R32G32_Float,
                64,
                0)
        ];

        RasterizerDescription doubleSidedRasterizer =
            RasterizerDescription.CullNone;

        RasterizerDescription singleSidedRasterizer =
            RasterizerDescription.CullNone;

        // glTF defines counter-clockwise triangles as front-facing.
        // Match that convention before enabling back-face culling.
        singleSidedRasterizer.FrontCounterClockwise =
            true;

        singleSidedRasterizer.CullMode =
            CullMode.Back;

        GraphicsPipelineStateDescription opaqueDescription =
            CreatePipelineDescription(
                vertexShader,
                pixelShader,
                inputElements,
                BlendDescription.Opaque,
                DepthStencilDescription.Default,
                doubleSidedRasterizer);

        _opaquePipelineState =
            _device.CreateGraphicsPipelineState(
                opaqueDescription);

        GraphicsPipelineStateDescription opaqueSingleSidedDescription =
            CreatePipelineDescription(
                vertexShader,
                pixelShader,
                inputElements,
                BlendDescription.Opaque,
                DepthStencilDescription.Default,
                singleSidedRasterizer);

        _opaqueSingleSidedPipelineState =
            _device.CreateGraphicsPipelineState(
                opaqueSingleSidedDescription);

        GraphicsPipelineStateDescription blendDescription =
            CreatePipelineDescription(
                vertexShader,
                pixelShader,
                inputElements,
                BlendDescription.NonPremultiplied,
                DepthStencilDescription.Read,
                doubleSidedRasterizer);

        _blendPipelineState =
            _device.CreateGraphicsPipelineState(
                blendDescription);

        GraphicsPipelineStateDescription blendSingleSidedDescription =
            CreatePipelineDescription(
                vertexShader,
                pixelShader,
                inputElements,
                BlendDescription.NonPremultiplied,
                DepthStencilDescription.Read,
                singleSidedRasterizer);

        _blendSingleSidedPipelineState =
            _device.CreateGraphicsPipelineState(
                blendSingleSidedDescription);

        _pipelineReady =
            true;
    }

    /// <summary>
    /// Builds one mesh pipeline-state description for the requested blend mode.
    /// </summary>
    private GraphicsPipelineStateDescription CreatePipelineDescription(
        byte[] vertexShader,
        byte[] pixelShader,
        InputElementDescription[] inputElements,
        BlendDescription blendState,
        DepthStencilDescription depthStencilState,
        RasterizerDescription rasterizerState)
    {
        if (_rootSignature == null)
        {
            throw new InvalidOperationException(
                "Root signature is not initialized.");
        }

        return new GraphicsPipelineStateDescription
        {
            RootSignature =
                _rootSignature,

            VertexShader =
                vertexShader,

            PixelShader =
                pixelShader,

            BlendState =
                blendState,

            RasterizerState =
                rasterizerState,

            DepthStencilState =
                depthStencilState,

            DepthStencilFormat =
                DepthFormat,

            InputLayout =
                new InputLayoutDescription(
                    inputElements),

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
    }

    /// <summary>
    /// Recreates swap-chain and depth resources for a new host size.
    /// </summary>
    /// <param name="width">New render width in pixels.</param>
    /// <param name="height">New render height in pixels.</param>
    public void Resize()
    {
        if (!_initialized ||
            _swapChain == null ||
            _device == null ||
            _rtvHeap == null)
        {
            return;
        }

        int width =
            Math.Max(
                1,
                _host.ClientSize.Width);

        int height =
            Math.Max(
                1,
                _host.ClientSize.Height);

        WaitForGpu();
        ReleaseBackBuffers();

        _swapChain.ResizeBuffers(
            (uint)FrameCount,
            (uint)width,
            (uint)height,
            BackBufferFormat,
            SwapChainFlags.None);

        _frameIndex =
            _swapChain
                .CurrentBackBufferIndex;

        CreateRenderTargetViews();
        CreateDepthBuffer();

        _viewport =
            new Viewport(
                0,
                0,
                width,
                height,
                0.0f,
                1.0f);

        _scissorRect =
            new RectI(
                0,
                0,
                width,
                height);

        _camera.SetViewport(
            width,
            height);

        Debug.WriteLine(
            $"Resize -> {width}x{height}");
    }

    /// <summary>
    /// Creates the flip-discard DXGI swap chain for the WinForms host window.
    /// </summary>
    private void CreateSwapChain()
    {
        if (_factory == null ||
            _commandQueue == null)
        {
            throw new InvalidOperationException(
                "Factory or command queue " +
                "is not initialized.");
        }

        int width =
            Math.Max(
                1,
                _host.ClientSize.Width);

        int height =
            Math.Max(
                1,
                _host.ClientSize.Height);

        var description =
            new SwapChainDescription1
            {
                Width =
                    (uint)width,

                Height =
                    (uint)height,

                Format =
                    BackBufferFormat,

                Stereo =
                    false,

                SampleDescription =
                    new SampleDescription(
                        1,
                        0),

                BufferUsage =
                    Usage.RenderTargetOutput,

                BufferCount =
                    FrameCount,

                Scaling =
                    Scaling.Stretch,

                SwapEffect =
                    SwapEffect.FlipDiscard,

                AlphaMode =
                    Vortice.DXGI
                        .AlphaMode.Ignore,

                Flags =
                    SwapChainFlags.None
            };

        using IDXGISwapChain1 temporarySwapChain =
            _factory.CreateSwapChainForHwnd(
                _commandQueue,
                _host.Handle,
                description);

        _swapChain =
            temporarySwapChain
                .QueryInterface<IDXGISwapChain3>();

        _frameIndex =
            _swapChain
                .CurrentBackBufferIndex;

        _factory.MakeWindowAssociation(
            _host.Handle,
            WindowAssociationFlags.IgnoreAltEnter);
    }

    private void CreateRtvHeapAndViews()
    {
        if (_device == null)
        {
            throw new InvalidOperationException(
                "Device is not initialized.");
        }

        _rtvHeap =
            _device.CreateDescriptorHeap(
                new DescriptorHeapDescription(
                    DescriptorHeapType
                        .RenderTargetView,
                    FrameCount));

        _rtvDescriptorSize =
            _device.GetDescriptorHandleIncrementSize(
                DescriptorHeapType
                    .RenderTargetView);

        CreateRenderTargetViews();
    }

    /// <summary>
    /// Creates a depth texture matching the current swap-chain dimensions.
    /// </summary>
    private void CreateDepthBuffer()
    {
        if (_device == null)
        {
            throw new InvalidOperationException(
                "Device is not initialized.");
        }

        int width =
            Math.Max(
                1,
                _host.ClientSize.Width);

        int height =
            Math.Max(
                1,
                _host.ClientSize.Height);

        _dsvHeap?.Dispose();
        _depthStencil?.Dispose();

        _dsvHeap =
            _device.CreateDescriptorHeap(
                new DescriptorHeapDescription(
                    DescriptorHeapType
                        .DepthStencilView,
                    1));

        ResourceDescription depthDescription =
            ResourceDescription.Texture2D(
                DepthFormat,
                (uint)width,
                (uint)height,
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

        _depthStencil =
            _device.CreateCommittedResource(
                new HeapProperties(
                    HeapType.Default),
                HeapFlags.None,
                depthDescription,
                ResourceStates.DepthWrite,
                clearValue);

        _device.CreateDepthStencilView(
            _depthStencil,
            null,
            _dsvHeap
                .GetCPUDescriptorHandleForHeapStart());
    }

    private void CreateRenderTargetViews()
    {
        if (_device == null ||
            _swapChain == null ||
            _rtvHeap == null)
        {
            throw new InvalidOperationException(
                "DX12 render-target objects " +
                "are not ready.");
        }

        CpuDescriptorHandle rtvHandle =
            _rtvHeap
                .GetCPUDescriptorHandleForHeapStart();

        for (int i = 0;
             i < FrameCount;
             i++)
        {
            _renderTargets[i]?.Dispose();

            _renderTargets[i] =
                _swapChain
                    .GetBuffer<ID3D12Resource>(
                        (uint)i);

            _device.CreateRenderTargetView(
                _renderTargets[i],
                null,
                rtvHandle);

            rtvHandle.Ptr +=
                _rtvDescriptorSize;
        }
    }

    /// <summary>
    /// Selects the first non-software adapter capable of creating the required Direct3D 12 device.
    /// </summary>
    private static IDXGIAdapter1? PickHardwareAdapter(
        IDXGIFactory4 factory)
    {
        for (uint i = 0;
             factory.EnumAdapters1(
                 i,
                 out IDXGIAdapter1? adapter).Success;
             i++)
        {
            AdapterDescription1 description =
                adapter.Description1;

            if ((description.Flags &
                 AdapterFlags.Software) != 0)
            {
                adapter.Dispose();
                continue;
            }

            try
            {
                using ID3D12Device testDevice =
                    D3D12CreateDevice<ID3D12Device>(
                        adapter,
                        FeatureLevel.Level_11_0);

                return adapter;
            }
            catch
            {
                adapter.Dispose();
            }
        }

        return null;
    }

    /// <summary>
    /// Signals the command queue and blocks until the GPU reaches that fence value.
    /// </summary>
    private void SignalAndWait()
    {
        if (_commandQueue == null ||
            _fence == null)
        {
            return;
        }

        ulong fenceToWaitFor =
            _fenceValue;

        _commandQueue.Signal(
            _fence,
            fenceToWaitFor);

        _fenceValue++;

        if (_fence.CompletedValue <
            fenceToWaitFor)
        {
            _fence.SetEventOnCompletion(
                fenceToWaitFor,
                _fenceEvent
                    .SafeWaitHandle
                    .DangerousGetHandle());

            _fenceEvent.WaitOne();
        }
    }

    private void WaitForGpu()
    {
        SignalAndWait();
    }

    private void ReleaseBackBuffers()
    {
        for (int i = 0;
             i < FrameCount;
             i++)
        {
            _renderTargets[i]?.Dispose();
            _renderTargets[i] =
                null;
        }
    }

}
