using SharpAssimp;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;

namespace FastViewDX12;

public static class AssimpFbxSceneLoader
{
    private const PostProcessSteps ImportSteps =
        PostProcessSteps.Triangulate |
        PostProcessSteps.JoinIdenticalVertices |
        PostProcessSteps.GenerateSmoothNormals |
        PostProcessSteps.CalculateTangentSpace |
        PostProcessSteps.PreTransformVertices |
        PostProcessSteps.ImproveCacheLocality |
        PostProcessSteps.FindDegenerates |
        PostProcessSteps.FindInvalidData |
        PostProcessSteps.SortByPrimitiveType |
        PostProcessSteps.FlipUVs;

    public static SceneData LoadFromFile(
        string path,
        IProgress<ModelLoadProgress>? progress = null,
        ModelLoadPerformanceReport? performance = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is empty.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("FBX file not found.", path);
        }

        string fullPath = Path.GetFullPath(path);
        string fileName = Path.GetFileName(fullPath);

        progress?.Report(new ModelLoadProgress(
            ModelLoadStage.ReadingFile,
            "Reading FBX file",
            fileName));

        cancellationToken.ThrowIfCancellationRequested();

        SharpAssimp.Scene imported;

        using (var context = new AssimpContext())
        using (performance?.Measure("File I/O + FBX parsing"))
        {
            progress?.Report(new ModelLoadProgress(
                ModelLoadStage.ParsingScene,
                "Parsing FBX scene",
                fileName));

            imported = context.ImportFile(fullPath, ImportSteps);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (imported == null || !imported.HasMeshes)
        {
            throw new InvalidDataException("The FBX file contains no meshes.");
        }

        var result = new SceneData();
        string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;

        using (performance?.Measure("Mesh/material processing"))
        {
            ReadMaterials(imported, directory, result, cancellationToken);

            if (result.Materials.Count == 0)
            {
                result.Materials.Add(CreateDefaultMaterial());
            }

            for (int meshIndex = 0; meshIndex < imported.Meshes.Count; meshIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SharpAssimp.Mesh source = imported.Meshes[meshIndex];

                if (!source.HasVertices || !source.HasFaces)
                {
                    continue;
                }

                MeshData mesh = ConvertMesh(source, meshIndex, result.Materials.Count);
                result.Meshes.Add(mesh);

                progress?.Report(new ModelLoadProgress(
                    ModelLoadStage.ProcessingMeshes,
                    $"Processing meshes {meshIndex + 1}/{imported.Meshes.Count}",
                    fileName,
                    meshIndex + 1,
                    imported.Meshes.Count));

                Debug.WriteLine(
                    $"FBX mesh '{mesh.Name}': vertices={mesh.Positions.Length}, " +
                    $"indices={mesh.Indices.Length}, material={mesh.MaterialIndex}");
            }
        }

        if (result.Meshes.Count == 0)
        {
            throw new InvalidDataException("The FBX file contains no triangle meshes.");
        }

        return result;
    }

    private static void ReadMaterials(
        SharpAssimp.Scene imported,
        string directory,
        SceneData result,
        CancellationToken cancellationToken)
    {
        foreach (SharpAssimp.Material source in imported.Materials)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float opacity = source.HasOpacity
                ? Math.Clamp(source.Opacity, 0.0f, 1.0f)
                : source.HasTransparencyFactor
                    ? Math.Clamp(1.0f - source.TransparencyFactor, 0.0f, 1.0f)
                    : 1.0f;

            Vector4 diffuse = source.HasColorDiffuse
                ? source.ColorDiffuse
                : Vector4.One;

            var material = new MaterialData
            {
                Name = source.HasName && !string.IsNullOrWhiteSpace(source.Name)
                    ? source.Name
                    : $"Material_{result.Materials.Count}",
                BaseColorFactor = new Vector4(
                    diffuse.X,
                    diffuse.Y,
                    diffuse.Z,
                    diffuse.W * opacity),
                EmissiveFactor = source.HasColorEmissive
                    ? new Vector4(
                        source.ColorEmissive.X,
                        source.ColorEmissive.Y,
                        source.ColorEmissive.Z,
                        1.0f)
                    : new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                MetallicFactor = TryGetFloat(source, "$mat.metallicFactor", 0.0f),
                RoughnessFactor = GetRoughness(source),
                NormalScale = source.HasBumpScaling ? source.BumpScaling : 1.0f,
                AlphaMode = opacity < 0.999f
                    ? MeshAlphaMode.Blend
                    : MeshAlphaMode.Opaque,
                DoubleSided = source.HasTwoSided && source.IsTwoSided
            };

            TextureSlot? baseColorSlot = GetBaseColorTexture(source);
            TextureSlot? normalSlot = GetNormalTexture(source);
            TextureSlot? emissiveSlot = GetEmissiveTexture(source);

            material.BaseColorTextureBytes = ReadTexture(
                baseColorSlot,
                imported,
                directory);
            material.NormalTextureBytes = ReadTexture(
                normalSlot,
                imported,
                directory);
            material.EmissiveTextureBytes = ReadTexture(
                emissiveSlot,
                imported,
                directory);

            result.Materials.Add(material);
        }
    }

    private static MeshData ConvertMesh(
        SharpAssimp.Mesh source,
        int meshIndex,
        int materialCount)
    {
        Vector3[] positions = source.Vertices.ToArray();
        int[] indices = TriangulateFaces(source.Faces);

        Vector3[] normals = source.HasNormals &&
            source.Normals.Count == positions.Length
                ? NormalizeNormals(source.Normals)
                : MeshGeometryUtilities.CalculateNormals(positions, indices);

        Vector2[] texCoords = GetTextureCoordinates(source, positions.Length);
        Vector4[] tangents = GetTangents(
            source,
            positions,
            normals,
            texCoords,
            indices);

        int materialIndex = source.MaterialIndex >= 0 &&
            source.MaterialIndex < materialCount
                ? source.MaterialIndex
                : 0;

        return new MeshData
        {
            Name = string.IsNullOrWhiteSpace(source.Name)
                ? $"Mesh_{meshIndex}"
                : source.Name,
            MaterialIndex = materialIndex,
            Positions = positions,
            Normals = normals,
            Tangents = tangents,
            TexCoords0 = texCoords,
            Indices = indices
        };
    }

    private static int[] TriangulateFaces(IReadOnlyList<Face> faces)
    {
        var indices = new List<int>(faces.Count * 3);

        foreach (Face face in faces)
        {
            if (face.Indices.Count < 3)
            {
                continue;
            }

            for (int i = 1; i + 1 < face.Indices.Count; i++)
            {
                indices.Add(face.Indices[0]);
                indices.Add(face.Indices[i]);
                indices.Add(face.Indices[i + 1]);
            }
        }

        return indices.ToArray();
    }

    private static Vector3[] NormalizeNormals(IReadOnlyList<Vector3> source)
    {
        var normals = new Vector3[source.Count];

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = source[i].LengthSquared() > 0.00000001f
                ? Vector3.Normalize(source[i])
                : Vector3.UnitY;
        }

        return normals;
    }

    private static Vector2[] GetTextureCoordinates(
        SharpAssimp.Mesh source,
        int vertexCount)
    {
        if (source.TextureCoordinateChannelCount == 0 ||
            source.TextureCoordinateChannels.Length == 0 ||
            source.TextureCoordinateChannels[0].Count != vertexCount)
        {
            return Array.Empty<Vector2>();
        }

        return source.TextureCoordinateChannels[0]
            .Select(value => new Vector2(value.X, value.Y))
            .ToArray();
    }

    private static Vector4[] GetTangents(
        SharpAssimp.Mesh source,
        Vector3[] positions,
        Vector3[] normals,
        Vector2[] texCoords,
        int[] indices)
    {
        if (!source.HasTangentBasis ||
            source.Tangents.Count != positions.Length ||
            source.BiTangents.Count != positions.Length)
        {
            return MeshGeometryUtilities.CalculateTangents(
                positions,
                normals,
                texCoords,
                indices);
        }

        var tangents = new Vector4[positions.Length];

        for (int i = 0; i < tangents.Length; i++)
        {
            Vector3 normal = normals[i];
            Vector3 tangent = source.Tangents[i] -
                normal * Vector3.Dot(normal, source.Tangents[i]);

            if (tangent.LengthSquared() <= 0.00000001f)
            {
                return MeshGeometryUtilities.CalculateTangents(
                    positions,
                    normals,
                    texCoords,
                    indices);
            }

            tangent = Vector3.Normalize(tangent);
            float handedness = Vector3.Dot(
                Vector3.Cross(normal, tangent),
                source.BiTangents[i]) < 0.0f
                    ? -1.0f
                    : 1.0f;

            tangents[i] = new Vector4(tangent, handedness);
        }

        return tangents;
    }

    private static TextureSlot? GetBaseColorTexture(SharpAssimp.Material material)
    {
        if (material.IsPBRMaterial && material.PBR.HasTextureBaseColor)
        {
            return material.PBR.TextureBaseColor;
        }

        return material.HasTextureDiffuse ? material.TextureDiffuse : null;
    }

    private static TextureSlot? GetNormalTexture(SharpAssimp.Material material)
    {
        if (material.IsPBRMaterial && material.PBR.HasTextureNormalCamera)
        {
            return material.PBR.TextureNormalCamera;
        }

        if (material.HasTextureNormal)
        {
            return material.TextureNormal;
        }

        return material.HasTextureHeight ? material.TextureHeight : null;
    }

    private static TextureSlot? GetEmissiveTexture(SharpAssimp.Material material)
    {
        if (material.IsPBRMaterial && material.PBR.HasTextureEmissionColor)
        {
            return material.PBR.TextureEmissionColor;
        }

        return material.HasTextureEmissive ? material.TextureEmissive : null;
    }

    private static byte[]? ReadTexture(
        TextureSlot? textureSlot,
        SharpAssimp.Scene scene,
        string directory)
    {
        if (!textureSlot.HasValue || string.IsNullOrWhiteSpace(textureSlot.Value.FilePath))
        {
            return null;
        }

        string reference = textureSlot.Value.FilePath;
        EmbeddedTexture? embedded = scene.GetEmbeddedTexture(reference);

        if (embedded != null)
        {
            if (embedded.HasCompressedData)
            {
                return embedded.CompressedData!.ToArray();
            }

            if (embedded.HasNonCompressedData)
            {
                return EncodeTopDownBgraBitmap(
                    embedded.Width,
                    embedded.Height,
                    embedded.NonCompressedData!);
            }
        }

        string normalized = reference
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        foreach (string candidate in GetTextureCandidates(normalized, directory))
        {
            if (File.Exists(candidate))
            {
                return File.ReadAllBytes(candidate);
            }
        }

        Debug.WriteLine($"FBX texture was not found: {reference}");
        return null;
    }

    private static IEnumerable<string> GetTextureCandidates(
        string reference,
        string directory)
    {
        if (Path.IsPathRooted(reference))
        {
            yield return reference;
        }
        else
        {
            string? combined = TryGetFullPath(directory, reference);

            if (combined != null)
            {
                yield return combined;
            }
        }

        string fileName = Path.GetFileName(reference);

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            string? besideModel = TryGetFullPath(directory, fileName);

            if (besideModel != null)
            {
                yield return besideModel;
            }
        }
    }

    private static string? TryGetFullPath(string directory, string path)
    {
        try
        {
            return Path.GetFullPath(Path.Combine(directory, path));
        }
        catch
        {
            return null;
        }
    }

    private static byte[] EncodeTopDownBgraBitmap(
        int width,
        int height,
        IReadOnlyList<Texel> pixels)
    {
        if (width <= 0 || height <= 0 || pixels.Count < width * height)
        {
            throw new InvalidDataException("Embedded FBX texture data is invalid.");
        }

        const int headerSize = 54;
        int pixelBytes = checked(width * height * 4);
        byte[] bitmap = new byte[checked(headerSize + pixelBytes)];
        bitmap[0] = (byte)'B';
        bitmap[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(2), bitmap.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(10), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(22), -height);
        BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(28), 32);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(34), pixelBytes);

        int destination = headerSize;

        for (int i = 0; i < width * height; i++)
        {
            Texel pixel = pixels[i];
            bitmap[destination++] = pixel.B;
            bitmap[destination++] = pixel.G;
            bitmap[destination++] = pixel.R;
            bitmap[destination++] = pixel.A;
        }

        return bitmap;
    }

    private static float GetRoughness(SharpAssimp.Material material)
    {
        float roughness = TryGetFloat(material, "$mat.roughnessFactor", float.NaN);

        if (float.IsFinite(roughness))
        {
            return Math.Clamp(roughness, 0.04f, 1.0f);
        }

        if (material.HasShininess)
        {
            return Math.Clamp(
                MathF.Sqrt(2.0f / (Math.Max(0.0f, material.Shininess) + 2.0f)),
                0.04f,
                1.0f);
        }

        return 0.8f;
    }

    private static float TryGetFloat(
        SharpAssimp.Material material,
        string propertyName,
        float fallback)
    {
        try
        {
            MaterialProperty? property = material.GetProperty(propertyName);
            return property?.GetFloatValue() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static MaterialData CreateDefaultMaterial() =>
        new()
        {
            Name = "DefaultMaterial",
            MetallicFactor = 0.0f,
            RoughnessFactor = 0.8f
        };
}

