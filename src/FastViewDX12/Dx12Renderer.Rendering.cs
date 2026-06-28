using System;
using System.Diagnostics;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace FastViewDX12;

/// <summary>
/// Per-frame command recording, render queues, draw calls, and shader-constant updates.
/// </summary>
public sealed partial class Dx12Renderer
{
    /// <summary>
    /// Records and submits one complete frame. If a preview capture is pending, the frame is also copied to a readback buffer.
    /// </summary>
    public void Render()
    {
        if (!_initialized ||
            _device == null ||
            _commandQueue == null ||
            _swapChain == null ||
            _commandList == null ||
            _rtvHeap == null ||
            _dsvHeap == null ||
            _srvHeap == null ||
            _fence == null ||
            !_pipelineReady ||
            _opaquePipelineState == null ||
            _blendPipelineState == null ||
            _opaqueSingleSidedPipelineState == null ||
            _blendSingleSidedPipelineState == null ||
            _rootSignature == null)
        {
            return;
        }

        int width =
            _host.ClientSize.Width;

        int height =
            _host.ClientSize.Height;

        if (width <= 0 ||
            height <= 0)
        {
            return;
        }

        ID3D12CommandAllocator allocator =
            _commandAllocators[
                (int)_frameIndex]!;

        allocator.Reset();

        _commandList.Reset(
            allocator);

        RenderShadowMapIfNeeded();

        ID3D12Resource backBuffer =
            _renderTargets[
                (int)_frameIndex]!;

        _commandList.ResourceBarrierTransition(
            backBuffer,
            ResourceStates.Present,
            ResourceStates.RenderTarget);

        CpuDescriptorHandle rtvHandle =
            _rtvHeap
                .GetCPUDescriptorHandleForHeapStart();

        rtvHandle.Ptr +=
            _frameIndex *
            _rtvDescriptorSize;

        CpuDescriptorHandle dsvHandle =
            _dsvHeap
                .GetCPUDescriptorHandleForHeapStart();

        _commandList.OMSetRenderTargets(
            rtvHandle,
            dsvHandle);

        Color4 clearColor =
            _transparentBackground
                ? new Color4(
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f)
                : new Color4(
                    _backgroundColor.R / 255.0f,
                    _backgroundColor.G / 255.0f,
                    _backgroundColor.B / 255.0f,
                    1.0f);

        _commandList.ClearRenderTargetView(
            rtvHandle,
            clearColor);

        _commandList.ClearDepthStencilView(
            dsvHandle,
            ClearFlags.Depth,
            1.0f,
            0);

        _commandList.RSSetViewport(
            _viewport);

        _commandList.RSSetScissorRect(
            in _scissorRect);

        _commandList.SetDescriptorHeaps(
            _srvHeap);

        if (!_transparentBackground &&
            _backgroundMode ==
                ViewerBackgroundMode.Environment)
        {
            DrawEnvironmentBackground(
                width,
                height);
        }

        _commandList.SetGraphicsRootSignature(
            _rootSignature);

        GpuDescriptorHandle environmentHandle =
            _srvHeap
                .GetGPUDescriptorHandleForHeapStart();

        environmentHandle.Ptr +=
            EnvironmentTextureSlot *
            _srvDescriptorSize;

        _commandList.SetGraphicsRootDescriptorTable(
            2,
            environmentHandle);

        GpuDescriptorHandle shadowHandle =
            _srvHeap
                .GetGPUDescriptorHandleForHeapStart();

        shadowHandle.Ptr +=
            ShadowTextureSlot *
            _srvDescriptorSize;

        _commandList.SetGraphicsRootDescriptorTable(
            3,
            shadowHandle);

        _commandList.IASetPrimitiveTopology(
            PrimitiveTopology.TriangleList);

        foreach (GpuRenderItem item in
                 _opaqueItems)
        {
            ID3D12PipelineState pipelineState =
                item.Material.Source.DoubleSided
                    ? _opaquePipelineState
                    : _opaqueSingleSidedPipelineState;

            DrawRenderItem(
                item,
                pipelineState);
        }

        System.Numerics.Vector3 cameraPosition =
            _camera.GetCameraPosition();

        _blendItems.Sort(
            (left, right) =>
            {
                float leftDistance =
                    System.Numerics.Vector3.DistanceSquared(
                        left.Center,
                        cameraPosition);

                float rightDistance =
                    System.Numerics.Vector3.DistanceSquared(
                        right.Center,
                        cameraPosition);

                return rightDistance.CompareTo(
                    leftDistance);
            });

        foreach (GpuRenderItem item in
                 _blendItems)
        {
            ID3D12PipelineState pipelineState =
                item.Material.Source.DoubleSided
                    ? _blendPipelineState
                    : _blendSingleSidedPipelineState;

            DrawRenderItem(
                item,
                pipelineState);
        }

        ApplyBloom(
            backBuffer,
            width,
            height);

        PreviewCaptureRequest? previewRequest =
            _pendingPreviewCapture;

        ID3D12Resource? previewReadbackBuffer =
            null;

        uint previewRowPitch =
            0;

        if (previewRequest != null)
        {
            previewReadbackBuffer =
                CreatePreviewReadbackBuffer(
                    width,
                    height,
                    out previewRowPitch);

            _commandList.ResourceBarrierTransition(
                backBuffer,
                ResourceStates.RenderTarget,
                ResourceStates.CopySource);

            var sourceLocation =
                new TextureCopyLocation(
                    backBuffer,
                    0);

            var footprint =
                new SubresourceFootPrint(
                    BackBufferFormat,
                    (uint)width,
                    (uint)height,
                    1,
                    previewRowPitch);

            var placedFootprint =
                new PlacedSubresourceFootPrint
                {
                    Offset =
                        0,

                    Footprint =
                        footprint
                };

            var destinationLocation =
                new TextureCopyLocation(
                    previewReadbackBuffer,
                    placedFootprint);

            _commandList.CopyTextureRegion(
                destinationLocation,
                0,
                0,
                0,
                sourceLocation);

            _commandList.ResourceBarrierTransition(
                backBuffer,
                ResourceStates.CopySource,
                ResourceStates.Present);
        }
        else
        {
            _commandList.ResourceBarrierTransition(
                backBuffer,
                ResourceStates.RenderTarget,
                ResourceStates.Present);
        }

        _commandList.Close();

        _commandQueue.ExecuteCommandList(
            _commandList);

        _swapChain.Present(
            1,
            PresentFlags.None);

        SignalAndWait();

        if (previewRequest != null &&
            previewReadbackBuffer != null)
        {
            try
            {
                SavePreviewReadbackToPng(
                    previewReadbackBuffer,
                    previewRowPitch,
                    width,
                    height,
                    previewRequest);

                previewRequest.Completed =
                    true;

                Debug.WriteLine(
                    $"Preview saved: " +
                    $"{previewRequest.Path}");
            }
            catch (Exception ex)
            {
                previewRequest.Error =
                    ex;

                Debug.WriteLine(
                    $"Preview export failed: {ex}");
            }
            finally
            {
                previewReadbackBuffer.Dispose();

                if (ReferenceEquals(
                    _pendingPreviewCapture,
                    previewRequest))
                {
                    _pendingPreviewCapture =
                        null;
                }
            }
        }

        _frameIndex =
            _swapChain
                .CurrentBackBufferIndex;
    }

    /// <summary>
    /// Binds one mesh, its material descriptors, and constants, then issues the indexed or non-indexed draw call.
    /// </summary>
    private void DrawRenderItem(
        GpuRenderItem item,
        ID3D12PipelineState pipelineState)
    {
        if (_commandList == null ||
            _srvHeap == null)
        {
            return;
        }

        UpdateShaderConstants(
            item);

        _commandList.SetPipelineState(
            pipelineState);

        _commandList.SetGraphicsRootConstantBufferView(
            0,
            item.ConstantBuffer.GPUVirtualAddress);

        var materialHandle =
            _srvHeap
                .GetGPUDescriptorHandleForHeapStart();

        materialHandle.Ptr +=
            item.Material.DescriptorStart *
            _srvDescriptorSize;

        _commandList.SetGraphicsRootDescriptorTable(
            1,
            materialHandle);

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

    /// <summary>
    /// Writes camera, light, environment, transform, and material values into one render-item constant buffer.
    /// </summary>
    private void UpdateShaderConstants(
        GpuRenderItem item)
    {
        System.Numerics.Matrix4x4 viewProjection =
            _camera.GetViewProjectionMatrix();

        viewProjection =
            System.Numerics.Matrix4x4.Transpose(
                viewProjection);

        MaterialData material =
            item.Material.Source;

        var constants =
            new ShaderConstants
            {
                ViewProjection =
                    viewProjection,

                LightViewProjection =
                    System.Numerics.Matrix4x4.Transpose(
                        _lightViewProjection),

                CameraPosition =
                    new System.Numerics.Vector4(
                        _camera.GetCameraPosition(),
                        1.0f),

                LightDirection =
            new System.Numerics.Vector4(
                _light.GetDirectionToLight(),
                0.0f),

                EnvironmentSettings =
    new System.Numerics.Vector4(
        _light.GetEnvironmentRotationRadians(),

        _environmentLightingEnabled
            ? _environmentIntensity
            : 0.0f,

        _directLightEnabled
            ? _directLightIntensity
            : 0.0f,

        Math.Max(
            0,
            _environmentMipCount - 1)),

                BaseColorFactor =
                    material.BaseColorFactor,

                EmissiveFactor =
                    material.EmissiveFactor,

                MaterialFactors =
                    new System.Numerics.Vector4(
                        material.MetallicFactor,
                        material.RoughnessFactor,
                        material.NormalScale,
                        material.AlphaCutoff),

                MaterialFlags =
                    new System.Numerics.Vector4(
                        (float)item.Mesh
                            .ResolvedAlphaMode,

                        material.Unlit
                            ? 1.0f
                            : 0.0f,

                        material.DoubleSided
                            ? 1.0f
                            : 0.0f,

                        material.TransmissionFactor),

                TextureSamplerIndices =
                    new System.Numerics.Vector4(
                        material.BaseColorTextureMapping
                            .GetSamplerIndex(),

                        material.NormalTextureMapping
                            .GetSamplerIndex(),

                        material.MetallicRoughnessTextureMapping
                            .GetSamplerIndex(),

                        material.EmissiveTextureMapping
                            .GetSamplerIndex()),

                ShadowSettings =
                    CreateShadowSettings()
            };

        item.ConstantBuffer.SetData(
            in constants);
    }

    /// <summary>
    /// Enables or disables the directional key light.
    /// </summary>
    public void SetDirectLightEnabled(
        bool enabled)
    {
        bool changed =
            _directLightEnabled !=
            enabled;

        _directLightEnabled =
            enabled;

        if (changed &&
            enabled)
        {
            MarkShadowMapDirty();
        }
    }

    /// <summary>
    /// Sets the directional-light multiplier after clamping it to a safe non-negative range.
    /// </summary>
    public void SetDirectLightIntensity(
        float intensity)
    {
        _directLightIntensity =
            Math.Clamp(
                intensity,
                0.0f,
                3.0f);
    }

}
