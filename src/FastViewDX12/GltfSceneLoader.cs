using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;

namespace FastViewDX12;

/// <summary>
/// Converts a SharpGLTF scene graph into renderer-neutral meshes, materials, texture bytes, and resolved vertex attributes.
/// </summary>
public static class GltfSceneLoader
{
    /// <summary>
    /// Loads the default glTF scene, traverses its visual nodes, and returns flattened CPU scene data.
    /// </summary>
    public static SceneData LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Path is empty.",
                nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "GLB/GLTF file not found.",
                path);
        }

        ModelRoot model = ModelRoot.Load(path);

        Scene? scene =
            model.DefaultScene ??
            model.LogicalScenes.FirstOrDefault();

        if (scene == null)
        {
            throw new InvalidDataException(
                "No scene found in glTF file.");
        }

        var result = new SceneData();
        var materialIndices = new Dictionary<Material, int>();

        int defaultMaterialIndex = -1;

        foreach (Node rootNode in scene.VisualChildren)
        {
            TraverseNode(
                rootNode,
                result,
                materialIndices,
                ref defaultMaterialIndex);
        }

        if (result.Materials.Count == 0)
        {
            result.Materials.Add(
                CreateDefaultMaterial());
        }

        return result;
    }

    /// <summary>
    /// Recursively visits the visual hierarchy while preserving each node world transform.
    /// </summary>
    private static void TraverseNode(
        Node node,
        SceneData sceneData,
        Dictionary<Material, int> materialIndices,
        ref int defaultMaterialIndex)
    {
        if (node.Mesh != null)
        {
            AddNodeMesh(
                node,
                sceneData,
                materialIndices,
                ref defaultMaterialIndex);
        }

        foreach (Node child in node.VisualChildren)
        {
            TraverseNode(
                child,
                sceneData,
                materialIndices,
                ref defaultMaterialIndex);
        }
    }

    /// <summary>
    /// Converts every primitive attached to one node into a separate renderer mesh.
    /// </summary>
    private static void AddNodeMesh(
        Node node,
        SceneData sceneData,
        Dictionary<Material, int> materialIndices,
        ref int defaultMaterialIndex)
    {
        Matrix4x4 world = node.WorldMatrix;

        string nodeName =
            string.IsNullOrWhiteSpace(node.Name)
                ? "Node"
                : node.Name;

        int primitiveNumber = 0;

        foreach (MeshPrimitive primitive in node.Mesh!.Primitives)
        {
            Vector3[] positions =
                TryGetVector3Accessor(
                    primitive,
                    "POSITION");

            if (positions.Length == 0)
            {
                primitiveNumber++;
                continue;
            }

            Vector3[] normals =
                TryGetVector3Accessor(
                    primitive,
                    "NORMAL");

            Vector4[] sourceTangents =
                TryGetVector4Accessor(
                    primitive,
                    "TANGENT");

            Vector2[] texCoords0 =
                TryGetVector2Accessor(
                    primitive,
                    "TEXCOORD_0");

            int[] indices =
                TryGetIndices(
                    primitive,
                    positions.Length);

            TransformPositions(
                positions,
                world);

            normals = PrepareNormals(
                positions,
                normals,
                indices,
                world);

            Vector4[] tangents =
                PrepareTangents(
                    sourceTangents,
                    positions,
                    normals,
                    texCoords0,
                    indices,
                    world);

            int materialIndex =
                GetOrCreateMaterialIndex(
                    primitive.Material,
                    sceneData,
                    materialIndices,
                    ref defaultMaterialIndex);

            string meshName =
                primitiveNumber == 0
                    ? nodeName
                    : $"{nodeName}_Primitive_{primitiveNumber}";

            var mesh = new MeshData
            {
                Name = meshName,
                MaterialIndex = materialIndex,
                Positions = positions,
                Normals = normals,
                Tangents = tangents,
                TexCoords0 = texCoords0,
                Indices = indices
            };

            Debug.WriteLine(
                $"glTF mesh '{meshName}': " +
                $"vertices={positions.Length}, " +
                $"normals={normals.Length}, " +
                $"tangents={tangents.Length}, " +
                $"uvs={texCoords0.Length}, " +
                $"indices={indices.Length}, " +
                $"material={materialIndex}");

            sceneData.Meshes.Add(mesh);
            primitiveNumber++;
        }
    }

    /// <summary>
    /// Deduplicates glTF materials and creates a default material for primitives without one.
    /// </summary>
    private static int GetOrCreateMaterialIndex(
        Material? material,
        SceneData sceneData,
        Dictionary<Material, int> materialIndices,
        ref int defaultMaterialIndex)
    {
        if (material == null)
        {
            if (defaultMaterialIndex < 0)
            {
                defaultMaterialIndex =
                    sceneData.Materials.Count;

                sceneData.Materials.Add(
                    CreateDefaultMaterial());
            }

            return defaultMaterialIndex;
        }

        if (materialIndices.TryGetValue(
            material,
            out int existingIndex))
        {
            return existingIndex;
        }

        int newIndex =
            sceneData.Materials.Count;

        sceneData.Materials.Add(
            ReadMaterial(material));

        materialIndices.Add(
            material,
            newIndex);

        return newIndex;
    }

    /// <summary>
    /// Creates a neutral physically based material for missing glTF materials.
    /// </summary>
    private static MaterialData CreateDefaultMaterial()
    {
        return new MaterialData
        {
            Name = "DefaultMaterial"
        };
    }

    /// <summary>
    /// Extracts PBR factors, alpha settings, flags, and supported image channels from a glTF material.
    /// </summary>
    private static MaterialData ReadMaterial(
        Material material)
    {
        var result =
            new MaterialData
            {
                Name =
                    string.IsNullOrWhiteSpace(material.Name)
                        ? "Material"
                        : material.Name,

                AlphaMode =
                    material.Alpha switch
                    {
                        SharpGLTF.Schema2.AlphaMode.MASK =>
                            MeshAlphaMode.Mask,

                        SharpGLTF.Schema2.AlphaMode.BLEND =>
                            MeshAlphaMode.Blend,

                        _ =>
                            MeshAlphaMode.Opaque
                    },

                AlphaCutoff =
                    material.AlphaCutoff,

                DoubleSided =
                    material.DoubleSided,

                Unlit =
                    material.Unlit
            };

        MaterialChannel? baseColorChannel =
            material.FindChannel(
                "BaseColor");

        if (baseColorChannel.HasValue)
        {
            result.BaseColorFactor =
                baseColorChannel.Value.Color;

            result.BaseColorTextureBytes =
                ReadChannelTexture(
                    baseColorChannel.Value,
                    "BaseColor");
        }

        MaterialChannel? normalChannel =
            material.FindChannel(
                "Normal");

        if (normalChannel.HasValue)
        {
            result.NormalScale =
                GetFactorOrDefault(
                    normalChannel.Value,
                    "NormalScale",
                    1.0f);

            result.NormalTextureBytes =
                ReadChannelTexture(
                    normalChannel.Value,
                    "Normal");
        }

        MaterialChannel? metallicRoughnessChannel =
            material.FindChannel(
                "MetallicRoughness");

        if (metallicRoughnessChannel.HasValue)
        {
            result.MetallicFactor =
                GetFactorOrDefault(
                    metallicRoughnessChannel.Value,
                    "MetallicFactor",
                    1.0f);

            result.RoughnessFactor =
                GetFactorOrDefault(
                    metallicRoughnessChannel.Value,
                    "RoughnessFactor",
                    1.0f);

            result.MetallicRoughnessTextureBytes =
                ReadChannelTexture(
                    metallicRoughnessChannel.Value,
                    "MetallicRoughness");
        }

        MaterialChannel? emissiveChannel =
            material.FindChannel(
                "Emissive");

        if (emissiveChannel.HasValue)
        {
            result.EmissiveFactor =
                emissiveChannel.Value.Color;

            result.EmissiveTextureBytes =
                ReadChannelTexture(
                    emissiveChannel.Value,
                    "Emissive");
        }

        Debug.WriteLine(
            $"glTF material '{result.Name}': " +
            $"alpha={result.AlphaMode}, " +
            $"baseColor={result.BaseColorTextureBytes?.Length ?? 0} bytes, " +
            $"normal={result.NormalTextureBytes?.Length ?? 0} bytes, " +
            $"metallicRoughness={result.MetallicRoughnessTextureBytes?.Length ?? 0} bytes, " +
            $"emissive={result.EmissiveTextureBytes?.Length ?? 0} bytes");

        return result;
    }

    /// <summary>
    /// Transforms model-space positions into flattened world space.
    /// </summary>
    private static void TransformPositions(
        Vector3[] positions,
        Matrix4x4 world)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] =
                Vector3.Transform(
                    positions[i],
                    world);
        }
    }

    /// <summary>
    /// Uses source normals when valid or generates smooth normals, then applies the inverse-transpose transform.
    /// </summary>
    private static Vector3[] PrepareNormals(
        Vector3[] positions,
        Vector3[] sourceNormals,
        int[] indices,
        Matrix4x4 world)
    {
        if (sourceNormals.Length != positions.Length)
        {
            return CalculateSmoothNormals(
                positions,
                indices);
        }

        Matrix4x4 normalMatrix = world;

        if (Matrix4x4.Invert(
            world,
            out Matrix4x4 inverseWorld))
        {
            normalMatrix =
                Matrix4x4.Transpose(
                    inverseWorld);
        }

        var result =
            new Vector3[sourceNormals.Length];

        for (int i = 0; i < sourceNormals.Length; i++)
        {
            Vector3 normal =
                Vector3.TransformNormal(
                    sourceNormals[i],
                    normalMatrix);

            result[i] =
                NormalizeOrFallback(
                    normal,
                    Vector3.UnitY);
        }

        return result;
    }

    /// <summary>
    /// Accumulates area-weighted triangle normals and normalizes the result per vertex.
    /// </summary>
    private static Vector3[] CalculateSmoothNormals(
        Vector3[] positions,
        int[] indices)
    {
        var normals =
            new Vector3[positions.Length];

        for (int i = 0;
             i + 2 < indices.Length;
             i += 3)
        {
            int index0 = indices[i];
            int index1 = indices[i + 1];
            int index2 = indices[i + 2];

            if (!IsValidIndex(index0, positions.Length) ||
                !IsValidIndex(index1, positions.Length) ||
                !IsValidIndex(index2, positions.Length))
            {
                continue;
            }

            Vector3 edge1 =
                positions[index1] -
                positions[index0];

            Vector3 edge2 =
                positions[index2] -
                positions[index0];

            Vector3 faceNormal =
                Vector3.Cross(
                    edge1,
                    edge2);

            if (faceNormal.LengthSquared() <
                0.0000000001f)
            {
                continue;
            }

            normals[index0] += faceNormal;
            normals[index1] += faceNormal;
            normals[index2] += faceNormal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] =
                NormalizeOrFallback(
                    normals[i],
                    Vector3.UnitY);
        }

        return normals;
    }

    /// <summary>
    /// Uses valid source tangents or generates tangents from positions, UVs, indices, and normals.
    /// </summary>
    private static Vector4[] PrepareTangents(
        Vector4[] sourceTangents,
        Vector3[] positions,
        Vector3[] normals,
        Vector2[] texCoords,
        int[] indices,
        Matrix4x4 world)
    {
        bool sourceTangentsValid =
            sourceTangents.Length ==
            positions.Length;

        bool normalsValid =
            normals.Length ==
            positions.Length;

        if (!sourceTangentsValid ||
            !normalsValid)
        {
            Debug.WriteLine(
                "No valid glTF tangents found. " +
                "Calculating fallback tangents.");

            return CalculateTangents(
                positions,
                normals,
                texCoords,
                indices);
        }

        var result =
            new Vector4[sourceTangents.Length];

        bool mirroredTransform =
            world.GetDeterminant() < 0.0f;

        for (int i = 0; i < sourceTangents.Length; i++)
        {
            Vector4 sourceTangent =
                sourceTangents[i];

            Vector3 tangent =
                new Vector3(
                    sourceTangent.X,
                    sourceTangent.Y,
                    sourceTangent.Z);

            tangent =
                Vector3.TransformNormal(
                    tangent,
                    world);

            Vector3 normal =
                normals[i];

            tangent -=
                normal *
                Vector3.Dot(
                    normal,
                    tangent);

            if (tangent.LengthSquared() <
                0.00000001f)
            {
                tangent =
                    CreateFallbackTangent(
                        normal);
            }
            else
            {
                tangent =
                    Vector3.Normalize(
                        tangent);
            }

            float handedness =
                sourceTangent.W < 0.0f
                    ? -1.0f
                    : 1.0f;

            if (mirroredTransform)
            {
                handedness =
                    -handedness;
            }

            result[i] =
                new Vector4(
                    tangent,
                    handedness);
        }

        return result;
    }

    /// <summary>
    /// Builds an orthonormal tangent basis and handedness for every vertex.
    /// </summary>
    private static Vector4[] CalculateTangents(
        Vector3[] positions,
        Vector3[] normals,
        Vector2[] texCoords,
        int[] indices)
    {
        var result =
            new Vector4[positions.Length];

        if (texCoords.Length != positions.Length ||
            normals.Length != positions.Length)
        {
            FillFallbackTangents(
                result,
                normals);

            return result;
        }

        var tangentSum =
            new Vector3[positions.Length];

        var bitangentSum =
            new Vector3[positions.Length];

        for (int i = 0;
             i + 2 < indices.Length;
             i += 3)
        {
            int index0 = indices[i];
            int index1 = indices[i + 1];
            int index2 = indices[i + 2];

            if (!IsValidIndex(index0, positions.Length) ||
                !IsValidIndex(index1, positions.Length) ||
                !IsValidIndex(index2, positions.Length))
            {
                continue;
            }

            Vector3 position0 =
                positions[index0];

            Vector3 position1 =
                positions[index1];

            Vector3 position2 =
                positions[index2];

            Vector2 uv0 =
                texCoords[index0];

            Vector2 uv1 =
                texCoords[index1];

            Vector2 uv2 =
                texCoords[index2];

            Vector3 edge1 =
                position1 - position0;

            Vector3 edge2 =
                position2 - position0;

            Vector2 deltaUv1 =
                uv1 - uv0;

            Vector2 deltaUv2 =
                uv2 - uv0;

            float determinant =
                deltaUv1.X * deltaUv2.Y -
                deltaUv1.Y * deltaUv2.X;

            if (MathF.Abs(determinant) <
                0.00000001f)
            {
                continue;
            }

            float inverseDeterminant =
                1.0f / determinant;

            Vector3 tangent =
                (edge1 * deltaUv2.Y -
                 edge2 * deltaUv1.Y) *
                inverseDeterminant;

            Vector3 bitangent =
                (edge2 * deltaUv1.X -
                 edge1 * deltaUv2.X) *
                inverseDeterminant;

            tangentSum[index0] += tangent;
            tangentSum[index1] += tangent;
            tangentSum[index2] += tangent;

            bitangentSum[index0] += bitangent;
            bitangentSum[index1] += bitangent;
            bitangentSum[index2] += bitangent;
        }

        for (int i = 0; i < result.Length; i++)
        {
            Vector3 normal =
                NormalizeOrFallback(
                    normals[i],
                    Vector3.UnitY);

            Vector3 tangent =
                tangentSum[i];

            tangent -=
                normal *
                Vector3.Dot(
                    normal,
                    tangent);

            if (tangent.LengthSquared() <
                0.00000001f)
            {
                tangent =
                    CreateFallbackTangent(
                        normal);
            }
            else
            {
                tangent =
                    Vector3.Normalize(
                        tangent);
            }

            Vector3 bitangent =
                bitangentSum[i];

            float handedness =
                Vector3.Dot(
                    Vector3.Cross(
                        normal,
                        tangent),
                    bitangent) < 0.0f
                    ? -1.0f
                    : 1.0f;

            result[i] =
                new Vector4(
                    tangent,
                    handedness);
        }

        return result;
    }

    /// <summary>
    /// Creates stable tangents when UV data is missing or degenerate.
    /// </summary>
    private static void FillFallbackTangents(
        Vector4[] tangents,
        Vector3[] normals)
    {
        for (int i = 0; i < tangents.Length; i++)
        {
            Vector3 normal =
                normals.Length == tangents.Length
                    ? NormalizeOrFallback(
                        normals[i],
                        Vector3.UnitY)
                    : Vector3.UnitY;

            Vector3 tangent =
                CreateFallbackTangent(
                    normal);

            tangents[i] =
                new Vector4(
                    tangent,
                    1.0f);
        }
    }

    /// <summary>
    /// Chooses an axis that is not parallel to the normal and constructs a normalized tangent.
    /// </summary>
    private static Vector3 CreateFallbackTangent(
        Vector3 normal)
    {
        Vector3 reference =
            MathF.Abs(normal.Y) < 0.999f
                ? Vector3.UnitY
                : Vector3.UnitX;

        return Vector3.Normalize(
            Vector3.Cross(
                reference,
                normal));
    }

    /// <summary>
    /// Reads a material factor array while supplying a complete fallback vector.
    /// </summary>
    private static float GetFactorOrDefault(
        MaterialChannel channel,
        string factorName,
        float fallback)
    {
        try
        {
            return channel.GetFactor(
                factorName);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Returns encoded image bytes for one material channel, or null when no supported image exists.
    /// </summary>
    private static byte[]? ReadChannelTexture(
        MaterialChannel channel,
        string channelName)
    {
        if (channel.TextureCoordinate != 0)
        {
            Debug.WriteLine(
                $"{channelName} uses TEXCOORD_" +
                $"{channel.TextureCoordinate}. " +
                "The viewer currently supports only TEXCOORD_0.");
        }

        Texture? texture =
            channel.Texture;

        if (texture == null)
        {
            return null;
        }

        SharpGLTF.Schema2.Image? image =
            SelectSupportedImage(
                texture);

        if (image == null)
        {
            Debug.WriteLine(
                $"{channelName}: no supported PNG/JPEG image found.");

            return null;
        }

        SharpGLTF.Memory.MemoryImage content =
            image.Content;

        if (content.IsEmpty)
        {
            return null;
        }

        return content.Content.ToArray();
    }

    /// <summary>
    /// Selects the first channel image that FastView can decode.
    /// </summary>
    private static SharpGLTF.Schema2.Image? SelectSupportedImage(
        Texture texture)
    {
        SharpGLTF.Schema2.Image? primary =
            texture.PrimaryImage;

        if (IsSupportedImage(primary))
        {
            return primary;
        }

        SharpGLTF.Schema2.Image? fallback =
            texture.FallbackImage;

        if (IsSupportedImage(fallback))
        {
            return fallback;
        }

        return null;
    }

    /// <summary>
    /// Checks MIME type and encoded data before accepting an image.
    /// </summary>
    private static bool IsSupportedImage(
        SharpGLTF.Schema2.Image? image)
    {
        if (image == null)
        {
            return false;
        }

        SharpGLTF.Memory.MemoryImage content =
            image.Content;

        return !content.IsEmpty &&
               (content.IsPng ||
                content.IsJpg);
    }

    /// <summary>
    /// Normalizes a vector unless it is non-finite or too small.
    /// </summary>
    private static Vector3 NormalizeOrFallback(
        Vector3 value,
        Vector3 fallback)
    {
        return value.LengthSquared() >
               0.00000001f
            ? Vector3.Normalize(value)
            : fallback;
    }

    /// <summary>
    /// Checks whether an index is inside a vertex array.
    /// </summary>
    private static bool IsValidIndex(
        int index,
        int length)
    {
        return index >= 0 &&
               index < length;
    }

    /// <summary>
    /// Reads a VEC4 vertex accessor or returns an empty array.
    /// </summary>
    private static Vector4[] TryGetVector4Accessor(
        MeshPrimitive primitive,
        string semantic)
    {
        try
        {
            Accessor? accessor =
                primitive.GetVertexAccessor(
                    semantic);

            if (accessor == null)
            {
                return Array.Empty<Vector4>();
            }

            return accessor
                .AsVector4Array()
                .ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Could not read {semantic}: {ex.Message}");

            return Array.Empty<Vector4>();
        }
    }

    /// <summary>
    /// Reads a VEC3 vertex accessor or returns an empty array.
    /// </summary>
    private static Vector3[] TryGetVector3Accessor(
        MeshPrimitive primitive,
        string semantic)
    {
        try
        {
            Accessor? accessor =
                primitive.GetVertexAccessor(
                    semantic);

            if (accessor == null)
            {
                return Array.Empty<Vector3>();
            }

            return accessor
                .AsVector3Array()
                .ToArray();
        }
        catch
        {
            return Array.Empty<Vector3>();
        }
    }

    /// <summary>
    /// Reads a VEC2 vertex accessor or returns an empty array.
    /// </summary>
    private static Vector2[] TryGetVector2Accessor(
        MeshPrimitive primitive,
        string semantic)
    {
        try
        {
            Accessor? accessor =
                primitive.GetVertexAccessor(
                    semantic);

            if (accessor == null)
            {
                return Array.Empty<Vector2>();
            }

            return accessor
                .AsVector2Array()
                .ToArray();
        }
        catch
        {
            return Array.Empty<Vector2>();
        }
    }

    /// <summary>
    /// Reads primitive indices or creates a sequential non-indexed fallback.
    /// </summary>
    private static int[] TryGetIndices(
        MeshPrimitive primitive,
        int vertexCount)
    {
        try
        {
            if (primitive.IndexAccessor == null)
            {
                return Enumerable
                    .Range(
                        0,
                        vertexCount)
                    .ToArray();
            }

            return primitive
                .IndexAccessor
                .AsIndicesArray()
                .Select(index => (int)index)
                .ToArray();
        }
        catch
        {
            return Enumerable
                .Range(
                    0,
                    vertexCount)
                .ToArray();
        }
    }
}
