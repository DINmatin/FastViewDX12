using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace FastViewDX12;

/// <summary>
/// CPU scene conversion, material and texture upload, GPU mesh resources, and scene cleanup.
/// </summary>
public sealed partial class Dx12Renderer
{
    private sealed class GpuMaterial : IDisposable
    {
        public MaterialData Source { get; }

        public uint DescriptorStart { get; }

        public DecodedTexture DecodedBaseColor { get; }

        public ID3D12Resource BaseColorTexture { get; }

        public ID3D12Resource NormalTexture { get; }

        public ID3D12Resource MetallicRoughnessTexture { get; }

        public ID3D12Resource EmissiveTexture { get; }

        public GpuMaterial(
            MaterialData source,
            uint descriptorStart,
            DecodedTexture decodedBaseColor,
            ID3D12Resource baseColorTexture,
            ID3D12Resource normalTexture,
            ID3D12Resource metallicRoughnessTexture,
            ID3D12Resource emissiveTexture)
        {
            Source = source;
            DescriptorStart = descriptorStart;
            DecodedBaseColor = decodedBaseColor;
            BaseColorTexture = baseColorTexture;
            NormalTexture = normalTexture;
            MetallicRoughnessTexture = metallicRoughnessTexture;
            EmissiveTexture = emissiveTexture;
        }

        public void Dispose()
        {
            BaseColorTexture.Dispose();
            NormalTexture.Dispose();
            MetallicRoughnessTexture.Dispose();
            EmissiveTexture.Dispose();
        }
    }

    private sealed class GpuRenderItem : IDisposable
    {
        public MeshData Mesh { get; }

        public GpuMaterial Material { get; }

        public ID3D12Resource VertexBuffer { get; }

        public ID3D12Resource? IndexBuffer { get; }

        public ID3D12Resource ConstantBuffer { get; }

        public VertexBufferView VertexBufferView { get; }

        public IndexBufferView IndexBufferView { get; }

        public uint VertexCount { get; }

        public uint IndexCount { get; }

        public bool UsesIndexBuffer { get; }

        public System.Numerics.Vector3 Center { get; }

        public GpuRenderItem(
            MeshData mesh,
            GpuMaterial material,
            ID3D12Resource vertexBuffer,
            ID3D12Resource? indexBuffer,
            ID3D12Resource constantBuffer,
            VertexBufferView vertexBufferView,
            IndexBufferView indexBufferView,
            uint vertexCount,
            uint indexCount,
            bool usesIndexBuffer,
            System.Numerics.Vector3 center)
        {
            Mesh = mesh;
            Material = material;
            VertexBuffer = vertexBuffer;
            IndexBuffer = indexBuffer;
            ConstantBuffer = constantBuffer;
            VertexBufferView = vertexBufferView;
            IndexBufferView = indexBufferView;
            VertexCount = vertexCount;
            IndexCount = indexCount;
            UsesIndexBuffer = usesIndexBuffer;
            Center = center;
        }

        public void Dispose()
        {
            ConstantBuffer.Dispose();
            IndexBuffer?.Dispose();
            VertexBuffer.Dispose();
        }
    }

    /// <summary>
    /// Allocates the shader-visible descriptor heap used by the environment map and material textures.
    /// </summary>
    private void CreateSrvHeap(
        uint materialCount)
    {
        if (_device == null)
        {
            throw new InvalidOperationException(
                "Device is not initialized.");
        }

        _srvHeap?.Dispose();

        uint safeMaterialCount =
            Math.Max(
                1u,
                materialCount);

        uint descriptorCount =
    GlobalTextureDescriptorCount +
    safeMaterialCount *
    TextureDescriptorCountPerMaterial;

        _srvHeap =
            _device.CreateDescriptorHeap(
                new DescriptorHeapDescription(
                    DescriptorHeapType
                        .ConstantBufferViewShaderResourceViewUnorderedAccessView,
                    descriptorCount,
                    DescriptorHeapFlags.ShaderVisible));

        _srvDescriptorSize =
            _device.GetDescriptorHandleIncrementSize(
                DescriptorHeapType
                    .ConstantBufferViewShaderResourceViewUnorderedAccessView);
    }

    /// <summary>
    /// Decodes and uploads one material texture set into contiguous SRV slots.
    /// </summary>
    private GpuMaterial CreateGpuMaterial(
        MaterialData source,
        uint materialIndex)
    {
        DecodedTexture baseColor =
            DecodeTextureOrFallback(
                source.BaseColorTextureBytes,
                DecodedTexture.WhitePixel(),
                $"{source.Name} BaseColor");

        DecodedTexture normal =
            DecodeTextureOrFallback(
                source.NormalTextureBytes,
                DecodedTexture.NeutralNormalPixel(),
                $"{source.Name} Normal");

        DecodedTexture metallicRoughness =
            DecodeTextureOrFallback(
                source.MetallicRoughnessTextureBytes,
                DecodedTexture.DefaultMetallicRoughnessPixel(),
                $"{source.Name} Metallic/Roughness");

        DecodedTexture emissive =
            DecodeTextureOrFallback(
                source.EmissiveTextureBytes,
                CreateBlackPixel(),
                $"{source.Name} Emissive");

        uint descriptorStart =
    GlobalTextureDescriptorCount +
    materialIndex *
    TextureDescriptorCountPerMaterial;

        ID3D12Resource baseColorTexture =
            CreateTexture(
                baseColor,
                descriptorStart +
                BaseColorTextureSlot);

        ID3D12Resource normalTexture =
            CreateTexture(
                normal,
                descriptorStart +
                NormalTextureSlot);

        ID3D12Resource metallicRoughnessTexture =
            CreateTexture(
                metallicRoughness,
                descriptorStart +
                MetallicRoughnessTextureSlot);

        ID3D12Resource emissiveTexture =
            CreateTexture(
                emissive,
                descriptorStart +
                EmissiveTextureSlot);

        return new GpuMaterial(
            source,
            descriptorStart,
            baseColor,
            baseColorTexture,
            normalTexture,
            metallicRoughnessTexture,
            emissiveTexture);
    }

    private static DecodedTexture CreateBlackPixel()
    {
        return new DecodedTexture(
            1,
            1,
            [
                0,
                0,
                0,
                255
            ]);
    }

    private static DecodedTexture DecodeTextureOrFallback(
        byte[]? encodedBytes,
        DecodedTexture fallback,
        string textureName)
    {
        if (encodedBytes is not
            { Length: > 0 })
        {
            Debug.WriteLine(
                $"{textureName} missing. " +
                "Using fallback texture.");

            return fallback;
        }

        try
        {
            DecodedTexture decoded =
                TextureDecoder.Decode(
                    encodedBytes);

            Debug.WriteLine(
                $"{textureName} loaded: " +
                $"{decoded.Width} x " +
                $"{decoded.Height}");

            return decoded;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"{textureName} decode failed: " +
                $"{ex}");

            return fallback;
        }
    }

    private ID3D12Resource CreateTexture(
        DecodedTexture decodedTexture,
        uint descriptorIndex)
    {
        if (_device == null ||
            _commandQueue == null ||
            _srvHeap == null)
        {
            throw new InvalidOperationException(
                "Texture resources are not initialized.");
        }

        ID3D12Resource texture =
            GpuTextureUploader.UploadRgba8(
                _device,
                _commandQueue,
                SignalAndWait,
                decodedTexture);

        var srvDescription =
            new ShaderResourceViewDescription
            {
                Shader4ComponentMapping =
                    ShaderComponentMapping.Default,

                Format =
                    Format.R8G8B8A8_UNorm,

                ViewDimension =
                    Vortice.Direct3D12
                        .ShaderResourceViewDimension.Texture2D
            };

        srvDescription.Texture2D.MipLevels =
            1;

        CpuDescriptorHandle handle =
            _srvHeap
                .GetCPUDescriptorHandleForHeapStart();

        handle.Ptr +=
            descriptorIndex *
            _srvDescriptorSize;

        _device.CreateShaderResourceView(
            texture,
            srvDescription,
            handle);

        return texture;
    }

    /// <summary>
    /// Uploads one mesh primitive and allocates its per-object constant buffer.
    /// </summary>
    private GpuRenderItem CreateRenderItem(
        MeshData mesh,
        GpuMaterial material)
    {
        if (_device == null)
        {
            throw new InvalidOperationException(
                "Device is not initialized.");
        }

        if (mesh.Positions.Length == 0)
        {
            throw new InvalidOperationException(
                $"Mesh '{mesh.Name}' has no positions.");
        }

        var vertices =
            new VertexPositionNormalTangentTexture[
                mesh.Positions.Length];

        bool normalsValid =
            mesh.Normals.Length ==
            mesh.Positions.Length;

        bool tangentsValid =
            mesh.Tangents.Length ==
            mesh.Positions.Length;

        bool texCoordsValid =
            mesh.TexCoords0.Length ==
            mesh.Positions.Length;

        for (int i = 0;
             i < mesh.Positions.Length;
             i++)
        {
            System.Numerics.Vector3 normal =
                normalsValid
                    ? mesh.Normals[i]
                    : System.Numerics.Vector3.UnitY;

            if (normal.LengthSquared() >
                0.000001f)
            {
                normal =
                    System.Numerics.Vector3.Normalize(
                        normal);
            }
            else
            {
                normal =
                    System.Numerics.Vector3.UnitY;
            }

            System.Numerics.Vector4 tangent =
                tangentsValid
                    ? mesh.Tangents[i]
                    : CreateFallbackTangent(
                        normal);

            System.Numerics.Vector2 texCoord =
                texCoordsValid
                    ? mesh.TexCoords0[i]
                    : System.Numerics.Vector2.Zero;

            vertices[i] =
                new VertexPositionNormalTangentTexture(
                    mesh.Positions[i],
                    normal,
                    tangent,
                    texCoord);
        }

        int vertexStride =
            System.Runtime.InteropServices.Marshal
                .SizeOf<VertexPositionNormalTangentTexture>();

        int vertexBufferSize =
            vertices.Length *
            vertexStride;

        ID3D12Resource vertexBuffer =
            _device.CreateCommittedResource(
                new HeapProperties(
                    HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(
                    (ulong)vertexBufferSize),
                ResourceStates.GenericRead);

        vertexBuffer.SetData(
            vertices);

        var vertexBufferView =
            new VertexBufferView
            {
                BufferLocation =
                    vertexBuffer.GPUVirtualAddress,

                SizeInBytes =
                    (uint)vertexBufferSize,

                StrideInBytes =
                    (uint)vertexStride
            };

        ID3D12Resource? indexBuffer =
            null;

        IndexBufferView indexBufferView =
            default;

        uint indexCount =
            0;

        bool usesIndexBuffer =
            mesh.Indices.Length > 0;

        if (usesIndexBuffer)
        {
            int indexBufferSize =
                mesh.Indices.Length *
                sizeof(int);

            indexBuffer =
                _device.CreateCommittedResource(
                    new HeapProperties(
                        HeapType.Upload),
                    HeapFlags.None,
                    ResourceDescription.Buffer(
                        (ulong)indexBufferSize),
                    ResourceStates.GenericRead);

            indexBuffer.SetData(
                mesh.Indices);

            indexBufferView =
                new IndexBufferView
                {
                    BufferLocation =
                        indexBuffer.GPUVirtualAddress,

                    SizeInBytes =
                        (uint)indexBufferSize,

                    Format =
                        Format.R32_UInt
                };

            indexCount =
                (uint)mesh.Indices.Length;
        }

        const int constantBufferSize =
            256;

        ID3D12Resource constantBuffer =
            _device.CreateCommittedResource(
                new HeapProperties(
                    HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(
                    constantBufferSize),
                ResourceStates.GenericRead);

        System.Numerics.Vector3 center =
            CalculateCenter(
                mesh.Positions);

        return new GpuRenderItem(
            mesh,
            material,
            vertexBuffer,
            indexBuffer,
            constantBuffer,
            vertexBufferView,
            indexBufferView,
            (uint)vertices.Length,
            indexCount,
            usesIndexBuffer,
            center);
    }

    private static System.Numerics.Vector4 CreateFallbackTangent(
        System.Numerics.Vector3 normal)
    {
        System.Numerics.Vector3 reference =
            MathF.Abs(normal.Y) < 0.999f
                ? System.Numerics.Vector3.UnitY
                : System.Numerics.Vector3.UnitX;

        System.Numerics.Vector3 tangent =
            System.Numerics.Vector3.Normalize(
                System.Numerics.Vector3.Cross(
                    reference,
                    normal));

        return new System.Numerics.Vector4(
            tangent,
            1.0f);
    }

    private static System.Numerics.Vector3 CalculateCenter(
        System.Numerics.Vector3[] positions)
    {
        System.Numerics.Vector3 min =
            positions[0];

        System.Numerics.Vector3 max =
            positions[0];

        for (int i = 1;
             i < positions.Length;
             i++)
        {
            min =
                System.Numerics.Vector3.Min(
                    min,
                    positions[i]);

            max =
                System.Numerics.Vector3.Max(
                    max,
                    positions[i]);
        }

        return (min + max) * 0.5f;
    }

    /// <summary>
    /// Replaces the current GPU scene with meshes and materials loaded from a glTF document.
    /// </summary>
    /// <param name="scene">CPU-side scene data to upload.</param>
    public void LoadScene(
        SceneData scene)
    {
        ArgumentNullException.ThrowIfNull(
            scene);

        if (!_initialized)
        {
            throw new InvalidOperationException(
                "Renderer must be initialized " +
                "before loading a scene.");
        }

        WaitForGpu();
        DisposeSceneResources();

        _scene =
            scene;

        if (_scene.Materials.Count == 0)
        {
            _scene.Materials.Add(
                new MaterialData
                {
                    Name =
                        "DefaultMaterial"
                });
        }

        CreateSrvHeap(
            (uint)_scene.Materials.Count);

        CreateEnvironmentSrv();

        for (int i = 0;
             i < _scene.Materials.Count;
             i++)
        {
            GpuMaterial gpuMaterial =
                CreateGpuMaterial(
                    _scene.Materials[i],
                    (uint)i);

            _gpuMaterials.Add(
                gpuMaterial);
        }

        foreach (MeshData mesh in
                 _scene.Meshes)
        {
            int materialIndex =
                Math.Clamp(
                    mesh.MaterialIndex,
                    0,
                    _gpuMaterials.Count - 1);

            GpuMaterial gpuMaterial =
                _gpuMaterials[
                    materialIndex];

            mesh.ResolvedAlphaMode =
                ResolveAlphaMode(
                    mesh,
                    gpuMaterial.Source,
                    gpuMaterial.DecodedBaseColor);

            GpuRenderItem renderItem =
                CreateRenderItem(
                    mesh,
                    gpuMaterial);

            if (mesh.ResolvedAlphaMode ==
                MeshAlphaMode.Blend)
            {
                _blendItems.Add(
                    renderItem);
            }
            else
            {
                _opaqueItems.Add(
                    renderItem);
            }

            Debug.WriteLine(
                $"Render item '{mesh.Name}': " +
                $"material={materialIndex}, " +
                $"declaredAlpha={gpuMaterial.Source.AlphaMode}, " +
                $"resolvedAlpha={mesh.ResolvedAlphaMode}");
        }

        _camera.FitToScene(
            _scene);

        Debug.WriteLine(
            $"Renderer scene ready: " +
            $"{_opaqueItems.Count} opaque/mask, " +
            $"{_blendItems.Count} blend, " +
            $"{_gpuMaterials.Count} materials.");
    }

    /// <summary>
    /// Uses the material alpha declaration and decoded base-color alpha to choose the final render queue.
    /// </summary>
    private static MeshAlphaMode ResolveAlphaMode(
        MeshData mesh,
        MaterialData material,
        DecodedTexture baseColorTexture)
    {
        if (material.AlphaMode ==
            MeshAlphaMode.Opaque)
        {
            return MeshAlphaMode.Opaque;
        }

        if (material.AlphaMode ==
            MeshAlphaMode.Mask)
        {
            return MeshAlphaMode.Mask;
        }

        if (material.BaseColorFactor.W <
            0.98f)
        {
            return MeshAlphaMode.Blend;
        }

        if (mesh.TexCoords0.Length == 0 ||
            baseColorTexture.Width <= 0 ||
            baseColorTexture.Height <= 0)
        {
            return MeshAlphaMode.Opaque;
        }

        int transparentSamples =
            0;

        int totalSamples =
            0;

        void TestUv(
            System.Numerics.Vector2 uv)
        {
            float u =
                uv.X -
                MathF.Floor(
                    uv.X);

            float v =
                uv.Y -
                MathF.Floor(
                    uv.Y);

            int x =
                Math.Clamp(
                    (int)MathF.Round(
                        u *
                        (baseColorTexture.Width - 1)),
                    0,
                    baseColorTexture.Width - 1);

            int y =
                Math.Clamp(
                    (int)MathF.Round(
                        v *
                        (baseColorTexture.Height - 1)),
                    0,
                    baseColorTexture.Height - 1);

            int pixelIndex =
                (y *
                 baseColorTexture.Width +
                 x) *
                4;

            byte alpha =
                baseColorTexture
                    .Rgba8[
                        pixelIndex + 3];

            if (alpha < 250)
            {
                transparentSamples++;
            }

            totalSamples++;
        }

        for (int i = 0;
             i < mesh.TexCoords0.Length;
             i++)
        {
            TestUv(
                mesh.TexCoords0[i]);
        }

        for (int i = 0;
             i + 2 < mesh.Indices.Length;
             i += 3)
        {
            int index0 =
                mesh.Indices[i];

            int index1 =
                mesh.Indices[i + 1];

            int index2 =
                mesh.Indices[i + 2];

            if (index0 < 0 ||
                index1 < 0 ||
                index2 < 0 ||
                index0 >= mesh.TexCoords0.Length ||
                index1 >= mesh.TexCoords0.Length ||
                index2 >= mesh.TexCoords0.Length)
            {
                continue;
            }

            System.Numerics.Vector2 centerUv =
                (
                    mesh.TexCoords0[index0] +
                    mesh.TexCoords0[index1] +
                    mesh.TexCoords0[index2]
                ) /
                3.0f;

            TestUv(
                centerUv);
        }

        if (totalSamples == 0)
        {
            return MeshAlphaMode.Opaque;
        }

        float transparentRatio =
            transparentSamples /
            (float)totalSamples;

        return transparentRatio < 0.005f
            ? MeshAlphaMode.Opaque
            : MeshAlphaMode.Blend;
    }

    /// <summary>
    /// Disposes all GPU resources that belong to the currently loaded scene.
    /// </summary>
    private void DisposeSceneResources()
    {
        foreach (GpuRenderItem item in
                 _opaqueItems)
        {
            item.Dispose();
        }

        foreach (GpuRenderItem item in
                 _blendItems)
        {
            item.Dispose();
        }

        foreach (GpuMaterial material in
                 _gpuMaterials)
        {
            material.Dispose();
        }

        _opaqueItems.Clear();
        _blendItems.Clear();
        _gpuMaterials.Clear();
    }

}
