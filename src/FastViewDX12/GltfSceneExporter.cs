using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FastViewDX12;

/// <summary>
/// Writes the editable FastView scene as one self-contained GLB 2.0 file.
/// Model transforms are already baked by <see cref="SceneDocument.BuildRenderScene"/>,
/// while editor-only grid and gizmo geometry never enter the exported scene.
/// </summary>
public static class GltfSceneExporter
{
    private const uint GlbMagic =
        0x46546C67;

    private const uint GlbVersion =
        2;

    private const uint JsonChunkType =
        0x4E4F534A;

    private const uint BinaryChunkType =
        0x004E4942;

    private const int ArrayBufferTarget =
        34962;

    private const int ElementArrayBufferTarget =
        34963;

    private const int FloatComponentType =
        5126;

    private const int UnsignedShortComponentType =
        5123;

    private const int UnsignedIntComponentType =
        5125;

    /// <summary>
    /// Exports one flattened scene to a binary glTF file with embedded geometry
    /// and material textures.
    /// </summary>
    public static void ExportGlb(
        SceneData scene,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(
            scene);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            outputPath);

        if (scene.Meshes.Count == 0)
        {
            throw new InvalidOperationException(
                "The scene does not contain any meshes to export.");
        }

        var builder =
            new ExportBuilder();

        builder.Build(
            scene);

        string fullPath =
            Path.GetFullPath(
                outputPath);

        string? directory =
            Path.GetDirectoryName(
                fullPath);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        builder.WriteGlb(
            fullPath);
    }

    private sealed class ExportBuilder
    {
        private readonly MemoryStream _binary =
            new();

        private readonly List<BufferViewDefinition> _bufferViews =
            new();

        private readonly List<AccessorDefinition> _accessors =
            new();

        private readonly List<ImageDefinition> _images =
            new();

        private readonly List<SamplerDefinition> _samplers =
            new();

        private readonly List<TextureDefinition> _textures =
            new();

        private readonly List<ExportMaterial> _materials =
            new();

        private readonly List<ExportMesh> _meshes =
            new();

        private readonly Dictionary<string, int> _imageIndices =
            new(
                StringComparer.Ordinal);

        private readonly Dictionary<SamplerKey, int> _samplerIndices =
            new();

        private readonly Dictionary<TextureKey, int> _textureIndices =
            new();

        private readonly HashSet<string> _extensionsUsed =
            new(
                StringComparer.Ordinal);

        public void Build(
            SceneData scene)
        {
            foreach (MaterialData material in
                     scene.Materials)
            {
                _materials.Add(
                    CreateMaterial(
                        material));
            }

            if (_materials.Count == 0)
            {
                _materials.Add(
                    CreateMaterial(
                        new MaterialData
                        {
                            Name =
                                "DefaultMaterial"
                        }));
            }

            foreach (MeshData mesh in
                     scene.Meshes)
            {
                if (mesh.Positions.Length == 0)
                {
                    continue;
                }

                _meshes.Add(
                    CreateMesh(
                        mesh));
            }

            if (_meshes.Count == 0)
            {
                throw new InvalidOperationException(
                    "The scene contains no exportable mesh positions.");
            }
        }

        public void WriteGlb(
            string outputPath)
        {
            byte[] jsonBytes =
                CreateJson();

            byte[] binaryBytes =
                _binary.ToArray();

            int paddedJsonLength =
                Align4(
                    jsonBytes.Length);

            int paddedBinaryLength =
                Align4(
                    binaryBytes.Length);

            uint totalLength =
                checked(
                    (uint)(
                        12 +
                        8 +
                        paddedJsonLength +
                        8 +
                        paddedBinaryLength));

            using var stream =
                new FileStream(
                    outputPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);

            using var writer =
                new BinaryWriter(
                    stream,
                    Encoding.UTF8,
                    leaveOpen: false);

            writer.Write(
                GlbMagic);

            writer.Write(
                GlbVersion);

            writer.Write(
                totalLength);

            writer.Write(
                (uint)paddedJsonLength);

            writer.Write(
                JsonChunkType);

            writer.Write(
                jsonBytes);

            WritePadding(
                writer,
                paddedJsonLength - jsonBytes.Length,
                0x20);

            writer.Write(
                (uint)paddedBinaryLength);

            writer.Write(
                BinaryChunkType);

            writer.Write(
                binaryBytes);

            WritePadding(
                writer,
                paddedBinaryLength - binaryBytes.Length,
                0x00);
        }

        private ExportMaterial CreateMaterial(
            MaterialData source)
        {
            int? baseColorTexture =
                GetTextureIndex(
                    source.BaseColorTextureBytes,
                    source.BaseColorTextureMapping);

            int? normalTexture =
                GetTextureIndex(
                    source.NormalTextureBytes,
                    source.NormalTextureMapping);

            int? metallicRoughnessTexture =
                GetTextureIndex(
                    source.MetallicRoughnessTextureBytes,
                    source.MetallicRoughnessTextureMapping);

            int? emissiveTexture =
                GetTextureIndex(
                    source.EmissiveTextureBytes,
                    source.EmissiveTextureMapping);

            float maximumEmissive =
                MathF.Max(
                    source.EmissiveFactor.X,
                    MathF.Max(
                        source.EmissiveFactor.Y,
                        source.EmissiveFactor.Z));

            float emissiveStrength =
                MathF.Max(
                    1.0f,
                    maximumEmissive);

            Vector3 emissiveFactor =
                new(
                    source.EmissiveFactor.X /
                    emissiveStrength,
                    source.EmissiveFactor.Y /
                    emissiveStrength,
                    source.EmissiveFactor.Z /
                    emissiveStrength);

            if (emissiveStrength >
                1.00001f)
            {
                _extensionsUsed.Add(
                    "KHR_materials_emissive_strength");
            }

            if (source.TransmissionFactor >
                0.00001f)
            {
                _extensionsUsed.Add(
                    "KHR_materials_transmission");
            }

            if (source.Unlit)
            {
                _extensionsUsed.Add(
                    "KHR_materials_unlit");
            }

            if (!MatrixNearlyIdentity(
                    source.BaseColorTextureMapping.Transform) ||
                !MatrixNearlyIdentity(
                    source.NormalTextureMapping.Transform) ||
                !MatrixNearlyIdentity(
                    source.MetallicRoughnessTextureMapping.Transform) ||
                !MatrixNearlyIdentity(
                    source.EmissiveTextureMapping.Transform))
            {
                _extensionsUsed.Add(
                    "KHR_texture_transform");
            }

            return new ExportMaterial(
                source,
                baseColorTexture,
                normalTexture,
                metallicRoughnessTexture,
                emissiveTexture,
                emissiveFactor,
                emissiveStrength);
        }

        private ExportMesh CreateMesh(
            MeshData source)
        {
            int positionAccessor =
                AddVector3Accessor(
                    source.Positions,
                    includeBounds: true);

            int? normalAccessor =
                source.Normals.Length ==
                source.Positions.Length
                    ? AddVector3Accessor(
                        source.Normals,
                        includeBounds: false)
                    : null;

            int? tangentAccessor =
                source.Tangents.Length ==
                source.Positions.Length
                    ? AddVector4Accessor(
                        source.Tangents)
                    : null;

            int? texCoord0Accessor =
                source.TexCoords0.Length ==
                source.Positions.Length
                    ? AddVector2Accessor(
                        source.TexCoords0)
                    : null;

            int? texCoord1Accessor =
                source.TexCoords1.Length ==
                source.Positions.Length
                    ? AddVector2Accessor(
                        source.TexCoords1)
                    : null;

            int? indexAccessor =
                source.Indices.Length > 0
                    ? AddIndexAccessor(
                        source.Indices,
                        source.Positions.Length)
                    : null;

            int materialIndex =
                Math.Clamp(
                    source.MaterialIndex,
                    0,
                    _materials.Count - 1);

            return new ExportMesh(
                source.Name,
                positionAccessor,
                normalAccessor,
                tangentAccessor,
                texCoord0Accessor,
                texCoord1Accessor,
                indexAccessor,
                materialIndex);
        }

        private int AddVector2Accessor(
            Vector2[] values)
        {
            int bufferView =
                AddFloatBufferView(
                    values.Length * 2,
                    writer =>
                    {
                        foreach (Vector2 value in
                                 values)
                        {
                            writer.Write(
                                value.X);

                            writer.Write(
                                value.Y);
                        }
                    });

            return AddAccessor(
                bufferView,
                FloatComponentType,
                values.Length,
                "VEC2");
        }

        private int AddVector3Accessor(
            Vector3[] values,
            bool includeBounds)
        {
            Vector3 min =
                values[0];

            Vector3 max =
                values[0];

            int bufferView =
                AddFloatBufferView(
                    values.Length * 3,
                    writer =>
                    {
                        foreach (Vector3 value in
                                 values)
                        {
                            writer.Write(
                                value.X);

                            writer.Write(
                                value.Y);

                            writer.Write(
                                value.Z);

                            if (includeBounds)
                            {
                                min =
                                    Vector3.Min(
                                        min,
                                        value);

                                max =
                                    Vector3.Max(
                                        max,
                                        value);
                            }
                        }
                    });

            return AddAccessor(
                bufferView,
                FloatComponentType,
                values.Length,
                "VEC3",
                includeBounds
                    ? [min.X, min.Y, min.Z]
                    : null,
                includeBounds
                    ? [max.X, max.Y, max.Z]
                    : null);
        }

        private int AddVector4Accessor(
            Vector4[] values)
        {
            int bufferView =
                AddFloatBufferView(
                    values.Length * 4,
                    writer =>
                    {
                        foreach (Vector4 value in
                                 values)
                        {
                            writer.Write(
                                value.X);

                            writer.Write(
                                value.Y);

                            writer.Write(
                                value.Z);

                            writer.Write(
                                value.W);
                        }
                    });

            return AddAccessor(
                bufferView,
                FloatComponentType,
                values.Length,
                "VEC4");
        }

        private int AddIndexAccessor(
            int[] indices,
            int vertexCount)
        {
            int maximumIndex =
                0;

            foreach (int index in
                     indices)
            {
                if (index < 0 ||
                    index >= vertexCount)
                {
                    throw new InvalidDataException(
                        $"Mesh index {index} is outside the vertex range 0..{vertexCount - 1}.");
                }

                maximumIndex =
                    Math.Max(
                        maximumIndex,
                        index);
            }

            if (maximumIndex <=
                ushort.MaxValue)
            {
                int byteOffset =
                    AlignBinary();

                using (
                    var writer =
                        new BinaryWriter(
                            _binary,
                            Encoding.UTF8,
                            leaveOpen: true))
                {
                    foreach (int index in
                             indices)
                    {
                        writer.Write(
                            (ushort)index);
                    }
                }

                int bufferView =
                    AddBufferView(
                        byteOffset,
                        indices.Length *
                        sizeof(ushort),
                        ElementArrayBufferTarget);

                return AddAccessor(
                    bufferView,
                    UnsignedShortComponentType,
                    indices.Length,
                    "SCALAR");
            }

            int uintByteOffset =
                AlignBinary();

            using (
                var writer =
                    new BinaryWriter(
                        _binary,
                        Encoding.UTF8,
                        leaveOpen: true))
            {
                foreach (int index in
                         indices)
                {
                    writer.Write(
                        (uint)index);
                }
            }

            int uintBufferView =
                AddBufferView(
                    uintByteOffset,
                    indices.Length *
                    sizeof(uint),
                    ElementArrayBufferTarget);

            return AddAccessor(
                uintBufferView,
                UnsignedIntComponentType,
                indices.Length,
                "SCALAR");
        }

        private int AddFloatBufferView(
            int floatCount,
            Action<BinaryWriter> writeValues)
        {
            int byteOffset =
                AlignBinary();

            using (
                var writer =
                    new BinaryWriter(
                        _binary,
                        Encoding.UTF8,
                        leaveOpen: true))
            {
                writeValues(
                    writer);
            }

            return AddBufferView(
                byteOffset,
                checked(
                    floatCount *
                    sizeof(float)),
                ArrayBufferTarget);
        }

        private int? GetTextureIndex(
            byte[]? encodedBytes,
            TextureMappingData mapping)
        {
            if (encodedBytes is not
                { Length: > 0 })
            {
                return null;
            }

            ImageFormatDefinition format =
                DetectImageFormat(
                    encodedBytes);

            string imageHash =
                Convert.ToHexString(
                    SHA256.HashData(
                        encodedBytes));

            string imageKey =
                $"{format.MimeType}:{imageHash}";

            if (!_imageIndices.TryGetValue(
                    imageKey,
                    out int imageIndex))
            {
                int byteOffset =
                    AlignBinary();

                _binary.Write(
                    encodedBytes,
                    0,
                    encodedBytes.Length);

                int bufferView =
                    AddBufferView(
                        byteOffset,
                        encodedBytes.Length,
                        target: null);

                imageIndex =
                    _images.Count;

                _images.Add(
                    new ImageDefinition(
                        bufferView,
                        format.MimeType));

                _imageIndices.Add(
                    imageKey,
                    imageIndex);
            }

            var samplerKey =
                new SamplerKey(
                    ConvertWrapMode(
                        mapping.WrapS),
                    ConvertWrapMode(
                        mapping.WrapT));

            if (!_samplerIndices.TryGetValue(
                    samplerKey,
                    out int samplerIndex))
            {
                samplerIndex =
                    _samplers.Count;

                _samplers.Add(
                    new SamplerDefinition(
                        samplerKey.WrapS,
                        samplerKey.WrapT));

                _samplerIndices.Add(
                    samplerKey,
                    samplerIndex);
            }

            var textureKey =
                new TextureKey(
                    imageIndex,
                    samplerIndex,
                    format.TextureExtension);

            if (_textureIndices.TryGetValue(
                    textureKey,
                    out int textureIndex))
            {
                return textureIndex;
            }

            textureIndex =
                _textures.Count;

            _textures.Add(
                new TextureDefinition(
                    imageIndex,
                    samplerIndex,
                    format.TextureExtension));

            _textureIndices.Add(
                textureKey,
                textureIndex);

            if (format.TextureExtension !=
                null)
            {
                _extensionsUsed.Add(
                    format.TextureExtension);
            }

            return textureIndex;
        }

        private int AddBufferView(
            int byteOffset,
            int byteLength,
            int? target)
        {
            int index =
                _bufferViews.Count;

            _bufferViews.Add(
                new BufferViewDefinition(
                    byteOffset,
                    byteLength,
                    target));

            return index;
        }

        private int AddAccessor(
            int bufferView,
            int componentType,
            int count,
            string type,
            float[]? min = null,
            float[]? max = null)
        {
            int index =
                _accessors.Count;

            _accessors.Add(
                new AccessorDefinition(
                    bufferView,
                    componentType,
                    count,
                    type,
                    min,
                    max));

            return index;
        }

        private int AlignBinary()
        {
            int alignedLength =
                Align4(
                    checked(
                        (int)_binary.Length));

            while (_binary.Length <
                   alignedLength)
            {
                _binary.WriteByte(
                    0);
            }

            return alignedLength;
        }

        private byte[] CreateJson()
        {
            using var stream =
                new MemoryStream();

            using (
                var writer =
                    new Utf8JsonWriter(
                        stream,
                        new JsonWriterOptions
                        {
                            Indented =
                                true
                        }))
            {
                writer.WriteStartObject();

                writer.WritePropertyName(
                    "asset");

                writer.WriteStartObject();
                writer.WriteString(
                    "version",
                    "2.0");
                writer.WriteString(
                    "generator",
                    "FastViewDX12");
                writer.WriteEndObject();

                if (_extensionsUsed.Count >
                    0)
                {
                    writer.WritePropertyName(
                        "extensionsUsed");

                    writer.WriteStartArray();

                    foreach (string extension in
                             _extensionsUsed.OrderBy(
                                 value =>
                                     value,
                                 StringComparer.Ordinal))
                    {
                        writer.WriteStringValue(
                            extension);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteNumber(
                    "scene",
                    0);

                writer.WritePropertyName(
                    "scenes");

                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString(
                    "name",
                    "FastView Scene");
                writer.WritePropertyName(
                    "nodes");
                writer.WriteStartArray();

                for (int index = 0;
                     index < _meshes.Count;
                     index++)
                {
                    writer.WriteNumberValue(
                        index);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndArray();

                WriteNodes(
                    writer);

                WriteMeshes(
                    writer);

                WriteMaterials(
                    writer);

                WriteTextures(
                    writer);

                WriteImages(
                    writer);

                WriteSamplers(
                    writer);

                WriteAccessors(
                    writer);

                WriteBufferViews(
                    writer);

                writer.WritePropertyName(
                    "buffers");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteNumber(
                    "byteLength",
                    checked(
                        (int)_binary.Length));
                writer.WriteEndObject();
                writer.WriteEndArray();

                writer.WriteEndObject();
            }

            return stream.ToArray();
        }

        private void WriteNodes(
            Utf8JsonWriter writer)
        {
            writer.WritePropertyName(
                "nodes");

            writer.WriteStartArray();

            for (int index = 0;
                 index < _meshes.Count;
                 index++)
            {
                writer.WriteStartObject();
                writer.WriteString(
                    "name",
                    _meshes[index].Name);
                writer.WriteNumber(
                    "mesh",
                    index);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private void WriteMeshes(
            Utf8JsonWriter writer)
        {
            writer.WritePropertyName(
                "meshes");

            writer.WriteStartArray();

            foreach (ExportMesh mesh in
                     _meshes)
            {
                writer.WriteStartObject();
                writer.WriteString(
                    "name",
                    mesh.Name);
                writer.WritePropertyName(
                    "primitives");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WritePropertyName(
                    "attributes");
                writer.WriteStartObject();
                writer.WriteNumber(
                    "POSITION",
                    mesh.PositionAccessor);

                WriteOptionalNumber(
                    writer,
                    "NORMAL",
                    mesh.NormalAccessor);

                WriteOptionalNumber(
                    writer,
                    "TANGENT",
                    mesh.TangentAccessor);

                WriteOptionalNumber(
                    writer,
                    "TEXCOORD_0",
                    mesh.TexCoord0Accessor);

                WriteOptionalNumber(
                    writer,
                    "TEXCOORD_1",
                    mesh.TexCoord1Accessor);

                writer.WriteEndObject();

                if (mesh.IndexAccessor.HasValue)
                {
                    writer.WriteNumber(
                        "indices",
                        mesh.IndexAccessor.Value);
                }

                writer.WriteNumber(
                    "material",
                    mesh.MaterialIndex);
                writer.WriteNumber(
                    "mode",
                    4);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private void WriteMaterials(
            Utf8JsonWriter writer)
        {
            writer.WritePropertyName(
                "materials");

            writer.WriteStartArray();

            foreach (ExportMaterial material in
                     _materials)
            {
                MaterialData source =
                    material.Source;

                writer.WriteStartObject();
                writer.WriteString(
                    "name",
                    source.Name);

                writer.WritePropertyName(
                    "pbrMetallicRoughness");
                writer.WriteStartObject();
                WriteVector4(
                    writer,
                    "baseColorFactor",
                    source.BaseColorFactor);
                writer.WriteNumber(
                    "metallicFactor",
                    source.MetallicFactor);
                writer.WriteNumber(
                    "roughnessFactor",
                    source.RoughnessFactor);

                WriteTextureInfo(
                    writer,
                    "baseColorTexture",
                    material.BaseColorTexture,
                    source.BaseColorTextureMapping);

                WriteTextureInfo(
                    writer,
                    "metallicRoughnessTexture",
                    material.MetallicRoughnessTexture,
                    source.MetallicRoughnessTextureMapping);

                writer.WriteEndObject();

                if (material.NormalTexture.HasValue)
                {
                    WriteTextureInfo(
                        writer,
                        "normalTexture",
                        material.NormalTexture,
                        source.NormalTextureMapping,
                        source.NormalScale);
                }

                WriteTextureInfo(
                    writer,
                    "emissiveTexture",
                    material.EmissiveTexture,
                    source.EmissiveTextureMapping);

                WriteVector3(
                    writer,
                    "emissiveFactor",
                    material.EmissiveFactor);

                writer.WriteString(
                    "alphaMode",
                    source.AlphaMode switch
                    {
                        MeshAlphaMode.Mask =>
                            "MASK",

                        MeshAlphaMode.Blend =>
                            "BLEND",

                        _ =>
                            "OPAQUE"
                    });

                if (source.AlphaMode ==
                    MeshAlphaMode.Mask)
                {
                    writer.WriteNumber(
                        "alphaCutoff",
                        source.AlphaCutoff);
                }

                writer.WriteBoolean(
                    "doubleSided",
                    source.DoubleSided);

                bool hasExtensions =
                    material.EmissiveStrength >
                        1.00001f ||
                    source.TransmissionFactor >
                        0.00001f ||
                    source.Unlit;

                if (hasExtensions)
                {
                    writer.WritePropertyName(
                        "extensions");
                    writer.WriteStartObject();

                    if (material.EmissiveStrength >
                        1.00001f)
                    {
                        writer.WritePropertyName(
                            "KHR_materials_emissive_strength");
                        writer.WriteStartObject();
                        writer.WriteNumber(
                            "emissiveStrength",
                            material.EmissiveStrength);
                        writer.WriteEndObject();
                    }

                    if (source.TransmissionFactor >
                        0.00001f)
                    {
                        writer.WritePropertyName(
                            "KHR_materials_transmission");
                        writer.WriteStartObject();
                        writer.WriteNumber(
                            "transmissionFactor",
                            Math.Clamp(
                                source.TransmissionFactor,
                                0.0f,
                                1.0f));
                        writer.WriteEndObject();
                    }

                    if (source.Unlit)
                    {
                        writer.WritePropertyName(
                            "KHR_materials_unlit");
                        writer.WriteStartObject();
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private void WriteTextures(
            Utf8JsonWriter writer)
        {
            if (_textures.Count == 0)
            {
                return;
            }

            writer.WritePropertyName(
                "textures");
            writer.WriteStartArray();

            foreach (TextureDefinition texture in
                     _textures)
            {
                writer.WriteStartObject();
                writer.WriteNumber(
                    "sampler",
                    texture.Sampler);

                if (texture.SourceExtension ==
                    null)
                {
                    writer.WriteNumber(
                        "source",
                        texture.Image);
                }
                else
                {
                    writer.WritePropertyName(
                        "extensions");
                    writer.WriteStartObject();
                    writer.WritePropertyName(
                        texture.SourceExtension);
                    writer.WriteStartObject();
                    writer.WriteNumber(
                        "source",
                        texture.Image);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private void WriteImages(
            Utf8JsonWriter writer)
        {
            if (_images.Count == 0)
            {
                return;
            }

            writer.WritePropertyName(
                "images");
            writer.WriteStartArray();

            foreach (ImageDefinition image in
                     _images)
            {
                writer.WriteStartObject();
                writer.WriteNumber(
                    "bufferView",
                    image.BufferView);
                writer.WriteString(
                    "mimeType",
                    image.MimeType);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private void WriteSamplers(
            Utf8JsonWriter writer)
        {
            if (_samplers.Count == 0)
            {
                return;
            }

            writer.WritePropertyName(
                "samplers");
            writer.WriteStartArray();

            foreach (SamplerDefinition sampler in
                     _samplers)
            {
                writer.WriteStartObject();
                writer.WriteNumber(
                    "wrapS",
                    sampler.WrapS);
                writer.WriteNumber(
                    "wrapT",
                    sampler.WrapT);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private void WriteAccessors(
            Utf8JsonWriter writer)
        {
            writer.WritePropertyName(
                "accessors");
            writer.WriteStartArray();

            foreach (AccessorDefinition accessor in
                     _accessors)
            {
                writer.WriteStartObject();
                writer.WriteNumber(
                    "bufferView",
                    accessor.BufferView);
                writer.WriteNumber(
                    "componentType",
                    accessor.ComponentType);
                writer.WriteNumber(
                    "count",
                    accessor.Count);
                writer.WriteString(
                    "type",
                    accessor.Type);

                WriteFloatArray(
                    writer,
                    "min",
                    accessor.Min);

                WriteFloatArray(
                    writer,
                    "max",
                    accessor.Max);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private void WriteBufferViews(
            Utf8JsonWriter writer)
        {
            writer.WritePropertyName(
                "bufferViews");
            writer.WriteStartArray();

            foreach (BufferViewDefinition bufferView in
                     _bufferViews)
            {
                writer.WriteStartObject();
                writer.WriteNumber(
                    "buffer",
                    0);
                writer.WriteNumber(
                    "byteOffset",
                    bufferView.ByteOffset);
                writer.WriteNumber(
                    "byteLength",
                    bufferView.ByteLength);

                if (bufferView.Target.HasValue)
                {
                    writer.WriteNumber(
                        "target",
                        bufferView.Target.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private void WriteTextureInfo(
            Utf8JsonWriter writer,
            string propertyName,
            int? textureIndex,
            TextureMappingData mapping,
            float? scale = null)
        {
            if (!textureIndex.HasValue)
            {
                return;
            }

            writer.WritePropertyName(
                propertyName);
            writer.WriteStartObject();
            writer.WriteNumber(
                "index",
                textureIndex.Value);

            if (mapping.TextureCoordinate !=
                0)
            {
                writer.WriteNumber(
                    "texCoord",
                    mapping.TextureCoordinate);
            }

            if (scale.HasValue &&
                MathF.Abs(
                    scale.Value -
                    1.0f) >
                0.00001f)
            {
                writer.WriteNumber(
                    "scale",
                    scale.Value);
            }

            if (!MatrixNearlyIdentity(
                    mapping.Transform))
            {
                _extensionsUsed.Add(
                    "KHR_texture_transform");

                DecomposeTextureTransform(
                    mapping.Transform,
                    out Vector2 offset,
                    out Vector2 textureScale,
                    out float rotation);

                writer.WritePropertyName(
                    "extensions");
                writer.WriteStartObject();
                writer.WritePropertyName(
                    "KHR_texture_transform");
                writer.WriteStartObject();

                if (offset.LengthSquared() >
                    0.0000000001f)
                {
                    WriteVector2(
                        writer,
                        "offset",
                        offset);
                }

                if (Vector2.DistanceSquared(
                        textureScale,
                        Vector2.One) >
                    0.0000000001f)
                {
                    WriteVector2(
                        writer,
                        "scale",
                        textureScale);
                }

                if (MathF.Abs(
                        rotation) >
                    0.00001f)
                {
                    writer.WriteNumber(
                        "rotation",
                        rotation);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        private static void DecomposeTextureTransform(
            Matrix3x2 transform,
            out Vector2 offset,
            out Vector2 scale,
            out float rotation)
        {
            offset =
                new Vector2(
                    transform.M31,
                    transform.M32);

            float scaleX =
                MathF.Sqrt(
                    transform.M11 * transform.M11 +
                    transform.M12 * transform.M12);

            float scaleY =
                MathF.Sqrt(
                    transform.M21 * transform.M21 +
                    transform.M22 * transform.M22);

            float determinant =
                transform.M11 * transform.M22 -
                transform.M12 * transform.M21;

            if (determinant <
                0.0f)
            {
                scaleY =
                    -scaleY;
            }

            rotation =
                scaleX >
                0.000001f
                    ? MathF.Atan2(
                        transform.M12,
                        transform.M11)
                    : 0.0f;

            scale =
                new Vector2(
                    scaleX,
                    scaleY);
        }

        private static ImageFormatDefinition DetectImageFormat(
            byte[] bytes)
        {
            if (bytes.Length >= 8 &&
                bytes[0] == 0x89 &&
                bytes[1] == 0x50 &&
                bytes[2] == 0x4E &&
                bytes[3] == 0x47)
            {
                return new ImageFormatDefinition(
                    "image/png",
                    null);
            }

            if (bytes.Length >= 3 &&
                bytes[0] == 0xFF &&
                bytes[1] == 0xD8 &&
                bytes[2] == 0xFF)
            {
                return new ImageFormatDefinition(
                    "image/jpeg",
                    null);
            }

            if (bytes.Length >= 12 &&
                bytes[0] == 0x52 &&
                bytes[1] == 0x49 &&
                bytes[2] == 0x46 &&
                bytes[3] == 0x46 &&
                bytes[8] == 0x57 &&
                bytes[9] == 0x45 &&
                bytes[10] == 0x42 &&
                bytes[11] == 0x50)
            {
                return new ImageFormatDefinition(
                    "image/webp",
                    "EXT_texture_webp");
            }

            ReadOnlySpan<byte> ktx2Signature =
            [
                0xAB,
                0x4B,
                0x54,
                0x58,
                0x20,
                0x32,
                0x30,
                0xBB,
                0x0D,
                0x0A,
                0x1A,
                0x0A
            ];

            if (bytes.AsSpan().StartsWith(
                    ktx2Signature))
            {
                return new ImageFormatDefinition(
                    "image/ktx2",
                    "KHR_texture_basisu");
            }

            throw new InvalidDataException(
                "The scene contains an embedded texture format that FastView cannot export to GLB yet.");
        }

        private static int ConvertWrapMode(
            TextureWrapModeData mode)
        {
            return mode switch
            {
                TextureWrapModeData.ClampToEdge =>
                    33071,

                TextureWrapModeData.MirroredRepeat =>
                    33648,

                _ =>
                    10497
            };
        }

        private static bool MatrixNearlyIdentity(
            Matrix3x2 matrix)
        {
            return
                MathF.Abs(matrix.M11 - 1.0f) < 0.00001f &&
                MathF.Abs(matrix.M12) < 0.00001f &&
                MathF.Abs(matrix.M21) < 0.00001f &&
                MathF.Abs(matrix.M22 - 1.0f) < 0.00001f &&
                MathF.Abs(matrix.M31) < 0.00001f &&
                MathF.Abs(matrix.M32) < 0.00001f;
        }

        private static void WriteOptionalNumber(
            Utf8JsonWriter writer,
            string propertyName,
            int? value)
        {
            if (value.HasValue)
            {
                writer.WriteNumber(
                    propertyName,
                    value.Value);
            }
        }

        private static void WriteFloatArray(
            Utf8JsonWriter writer,
            string propertyName,
            float[]? values)
        {
            if (values == null)
            {
                return;
            }

            writer.WritePropertyName(
                propertyName);
            writer.WriteStartArray();

            foreach (float value in
                     values)
            {
                writer.WriteNumberValue(
                    value);
            }

            writer.WriteEndArray();
        }

        private static void WriteVector2(
            Utf8JsonWriter writer,
            string propertyName,
            Vector2 value)
        {
            writer.WritePropertyName(
                propertyName);
            writer.WriteStartArray();
            writer.WriteNumberValue(
                value.X);
            writer.WriteNumberValue(
                value.Y);
            writer.WriteEndArray();
        }

        private static void WriteVector3(
            Utf8JsonWriter writer,
            string propertyName,
            Vector3 value)
        {
            writer.WritePropertyName(
                propertyName);
            writer.WriteStartArray();
            writer.WriteNumberValue(
                value.X);
            writer.WriteNumberValue(
                value.Y);
            writer.WriteNumberValue(
                value.Z);
            writer.WriteEndArray();
        }

        private static void WriteVector4(
            Utf8JsonWriter writer,
            string propertyName,
            Vector4 value)
        {
            writer.WritePropertyName(
                propertyName);
            writer.WriteStartArray();
            writer.WriteNumberValue(
                value.X);
            writer.WriteNumberValue(
                value.Y);
            writer.WriteNumberValue(
                value.Z);
            writer.WriteNumberValue(
                value.W);
            writer.WriteEndArray();
        }

        private static void WritePadding(
            BinaryWriter writer,
            int byteCount,
            byte value)
        {
            for (int index = 0;
                 index < byteCount;
                 index++)
            {
                writer.Write(
                    value);
            }
        }
    }

    private static int Align4(
        int value)
    {
        return
            (value + 3) &
            ~3;
    }

    private sealed record BufferViewDefinition(
        int ByteOffset,
        int ByteLength,
        int? Target);

    private sealed record AccessorDefinition(
        int BufferView,
        int ComponentType,
        int Count,
        string Type,
        float[]? Min,
        float[]? Max);

    private sealed record ImageDefinition(
        int BufferView,
        string MimeType);

    private sealed record SamplerDefinition(
        int WrapS,
        int WrapT);

    private sealed record TextureDefinition(
        int Image,
        int Sampler,
        string? SourceExtension);

    private sealed record ExportMaterial(
        MaterialData Source,
        int? BaseColorTexture,
        int? NormalTexture,
        int? MetallicRoughnessTexture,
        int? EmissiveTexture,
        Vector3 EmissiveFactor,
        float EmissiveStrength);

    private sealed record ExportMesh(
        string Name,
        int PositionAccessor,
        int? NormalAccessor,
        int? TangentAccessor,
        int? TexCoord0Accessor,
        int? TexCoord1Accessor,
        int? IndexAccessor,
        int MaterialIndex);

    private sealed record ImageFormatDefinition(
        string MimeType,
        string? TextureExtension);

    private readonly record struct SamplerKey(
        int WrapS,
        int WrapT);

    private readonly record struct TextureKey(
        int Image,
        int Sampler,
        string? SourceExtension);
}
