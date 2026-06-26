using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using DrawingColor = System.Drawing.Color;

namespace FastViewDX12;

/// <summary>
/// Owns FastView's Direct3D 12 device state, scene resources, camera controls, and frame capture pipeline.
/// </summary>
/// <remarks>
/// The implementation is split into partial-class files by responsibility. This keeps device setup, scene upload,
/// rendering, background handling, interaction, and thumbnail capture independently readable without changing runtime behavior.
/// </remarks>
public sealed partial class Dx12Renderer : IDisposable
{
    private const uint GlobalTextureDescriptorCount = 1;

    private const uint EnvironmentTextureSlot = 0;

    private const int FrameCount = 2;

    private const uint TextureDescriptorCountPerMaterial = 4;

    private const uint BaseColorTextureSlot = 0;

    private const uint NormalTextureSlot = 1;

    private const uint MetallicRoughnessTextureSlot = 2;

    private const uint EmissiveTextureSlot = 3;

    private bool _environmentLightingEnabled = true;

    private bool _directLightEnabled = true;

    private float _environmentIntensity = 1.0f;

    private float _directLightIntensity = 1.0f;

    private ViewerBackgroundMode _backgroundMode =
        ViewerBackgroundMode.SolidColor;

    private DrawingColor _backgroundColor =
        DrawingColor.FromArgb(
            20,
            20,
            26);

    private float _environmentBackgroundOpacity =
        1.0f;

    private bool _transparentBackground;

    private static readonly Format BackBufferFormat =
        Format.R8G8B8A8_UNorm;

    private static readonly Format DepthFormat =
        Format.D32_Float;

    private readonly Control _host;

    private readonly AutoResetEvent _fenceEvent = new(false);

    private readonly ID3D12Resource?[] _renderTargets =
        new ID3D12Resource[FrameCount];

    private readonly ID3D12CommandAllocator?[] _commandAllocators =
        new ID3D12CommandAllocator[FrameCount];

    private readonly CameraController _camera = new();

    private readonly LightController _light = new();

    private readonly List<GpuMaterial> _gpuMaterials = new();

    private readonly List<GpuRenderItem> _opaqueItems = new();

    private readonly List<GpuRenderItem> _blendItems = new();

    private SceneData? _scene;

    private IDXGIFactory4? _factory;

    private ID3D12Device? _device;

    private ID3D12CommandQueue? _commandQueue;

    private IDXGISwapChain3? _swapChain;

    private ID3D12DescriptorHeap? _rtvHeap;

    private ID3D12DescriptorHeap? _dsvHeap;

    private ID3D12DescriptorHeap? _srvHeap;

    private ID3D12GraphicsCommandList? _commandList;

    private ID3D12Fence? _fence;

    private ID3D12RootSignature? _rootSignature;

    private ID3D12PipelineState? _opaquePipelineState;

    private ID3D12PipelineState? _blendPipelineState;

    private ID3D12RootSignature? _backgroundRootSignature;

    private ID3D12PipelineState? _backgroundPipelineState;

    private ID3D12Resource? _backgroundConstantBuffer;

    private ID3D12Resource? _depthStencil;

    private Viewport _viewport;

    private RectI _scissorRect;

    private uint _rtvDescriptorSize;

    private uint _srvDescriptorSize;

    private uint _frameIndex;

    private ulong _fenceValue;

    private bool _initialized;

    private bool _pipelineReady;

    private ID3D12Resource? _environmentTexture;

    private string? _environmentMapPath;

    private int _environmentMipCount = 1;

    private PreviewCaptureRequest? _pendingPreviewCapture;

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential,
        Pack = 4)]
    private struct VertexPositionNormalTangentTexture
    {
        public System.Numerics.Vector3 Position;
        public System.Numerics.Vector3 Normal;
        public System.Numerics.Vector4 Tangent;
        public System.Numerics.Vector2 BaseColorTexCoord;
        public System.Numerics.Vector2 NormalTexCoord;
        public System.Numerics.Vector2 MetallicRoughnessTexCoord;
        public System.Numerics.Vector2 EmissiveTexCoord;

        public VertexPositionNormalTangentTexture(
            System.Numerics.Vector3 position,
            System.Numerics.Vector3 normal,
            System.Numerics.Vector4 tangent,
            System.Numerics.Vector2 baseColorTexCoord,
            System.Numerics.Vector2 normalTexCoord,
            System.Numerics.Vector2 metallicRoughnessTexCoord,
            System.Numerics.Vector2 emissiveTexCoord)
        {
            Position = position;
            Normal = normal;
            Tangent = tangent;
            BaseColorTexCoord = baseColorTexCoord;
            NormalTexCoord = normalTexCoord;
            MetallicRoughnessTexCoord = metallicRoughnessTexCoord;
            EmissiveTexCoord = emissiveTexCoord;
        }
    }

    [System.Runtime.InteropServices.StructLayout(
    System.Runtime.InteropServices.LayoutKind.Sequential,
    Pack = 4)]
    private struct ShaderConstants
    {
        public System.Numerics.Matrix4x4 ViewProjection;

        public System.Numerics.Vector4 CameraPosition;

        public System.Numerics.Vector4 LightDirection;

        // x = Environment rotation in radians
        // y = Environment intensity
        // z = Direct light intensity
        // w = highest environment mip level

        public System.Numerics.Vector4 EnvironmentSettings;

        public System.Numerics.Vector4 BaseColorFactor;

        public System.Numerics.Vector4 EmissiveFactor;

        public System.Numerics.Vector4 MaterialFactors;

        public System.Numerics.Vector4 MaterialFlags;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential,
        Pack = 4)]
    private struct BackgroundShaderConstants
    {
        public System.Numerics.Vector4 CameraForwardAndTanHalfFov;

        public System.Numerics.Vector4 CameraRightAndAspect;

        public System.Numerics.Vector4 CameraUpAndEnvironmentRotation;

        public System.Numerics.Vector4 SolidColorAndOpacity;
    }

    /// <summary>
    /// Creates a Direct3D 12 renderer that targets the handle of a WinForms control.
    /// </summary>
    /// <param name="host">Control whose native window handle owns the DXGI swap chain.</param>
    public Dx12Renderer(Control host)
    {
        _host =
            host ??
            throw new ArgumentNullException(
                nameof(host));
    }

    /// <summary>
    /// Waits for outstanding GPU work and releases every Direct3D, DXGI, scene, and synchronization resource.
    /// </summary>
    public void Dispose()
    {
        if (_initialized)
        {
            WaitForGpu();
        }

        DisposeSceneResources();
        ReleaseBackBuffers();

        _depthStencil?.Dispose();

        _backgroundConstantBuffer?.Dispose();
        _backgroundPipelineState?.Dispose();
        _backgroundRootSignature?.Dispose();

        _blendPipelineState?.Dispose();
        _opaquePipelineState?.Dispose();
        _rootSignature?.Dispose();

        _srvHeap?.Dispose();
        _dsvHeap?.Dispose();
        _rtvHeap?.Dispose();

        _commandList?.Dispose();
        _environmentTexture?.Dispose();
        for (int i = 0;
             i < FrameCount;
             i++)
        {
            _commandAllocators[i]?.Dispose();
            _commandAllocators[i] =
                null;
        }

        _fence?.Dispose();
        _swapChain?.Dispose();
        _commandQueue?.Dispose();
        _device?.Dispose();
        _factory?.Dispose();

        _fenceEvent.Dispose();

        _depthStencil =
            null;

        _backgroundConstantBuffer =
            null;

        _backgroundPipelineState =
            null;

        _backgroundRootSignature =
            null;

        _blendPipelineState =
            null;

        _opaquePipelineState =
            null;

        _rootSignature =
            null;

        _srvHeap =
            null;

        _dsvHeap =
            null;

        _rtvHeap =
            null;

        _commandList =
            null;

        _fence =
            null;

        _swapChain =
            null;

        _commandQueue =
            null;

        _device =
            null;

        _factory =
            null;

        _scene =
            null;

        _initialized =
            false;

        _pipelineReady =
            false;

          _environmentTexture = null;
        _environmentMipCount = 1;
        _environmentMapPath = null;
    }

}
