using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace FastViewDX12;

/// <summary>
/// Stores the models that make up the editable FastView scene.
/// Each model keeps its original loaded scene and an independent transform.
/// </summary>
public sealed class SceneDocument
{
    private readonly List<SceneModel> _models = new();

    /// <summary>Gets the models in scene order.</summary>
    public IReadOnlyList<SceneModel> Models => _models;

    /// <summary>Removes all existing models and inserts one new root model.</summary>
    public SceneModel ReplaceWith(
        string sourcePath,
        SceneData scene)
    {
        _models.Clear();
        return Add(sourcePath, scene);
    }

    /// <summary>Adds one model without changing any existing scene content.</summary>
    public SceneModel Add(
        string sourcePath,
        SceneData scene)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(scene);

        var model =
            new SceneModel(
                sourcePath,
                scene);

        _models.Add(model);
        return model;
    }

    /// <summary>
    /// Creates the flattened renderer input for the current model list.
    /// Source scenes remain unchanged so future transform edits can rebuild
    /// the combined scene without reloading the glTF files.
    /// </summary>
    public SceneData BuildRenderScene()
    {
        var result =
            new SceneData();

        foreach (SceneModel model in _models)
        {
            AppendModel(
                result,
                model);
        }

        return result;
    }

    private static void AppendModel(
        SceneData destination,
        SceneModel model)
    {
        int materialOffset =
            destination.Materials.Count;

        destination.Materials.AddRange(
            model.SourceScene.Materials);

        Matrix4x4 transform =
            model.CreateTransform();

        Matrix4x4 normalTransform =
            transform;

        if (Matrix4x4.Invert(
            transform,
            out Matrix4x4 inverseTransform))
        {
            normalTransform =
                Matrix4x4.Transpose(
                    inverseTransform);
        }

        bool mirrored =
            transform.GetDeterminant() < 0.0f;

        foreach (MeshData sourceMesh in
                 model.SourceScene.Meshes)
        {
            destination.Meshes.Add(
                CloneMesh(
                    sourceMesh,
                    model,
                    materialOffset,
                    transform,
                    normalTransform,
                    mirrored));
        }
    }

    private static MeshData CloneMesh(
        MeshData source,
        SceneModel model,
        int materialOffset,
        Matrix4x4 transform,
        Matrix4x4 normalTransform,
        bool mirrored)
    {
        Vector3[] positions =
            new Vector3[source.Positions.Length];

        for (int i = 0;
             i < positions.Length;
             i++)
        {
            positions[i] =
                Vector3.Transform(
                    source.Positions[i],
                    transform);
        }

        Vector3[] normals =
            new Vector3[source.Normals.Length];

        for (int i = 0;
             i < normals.Length;
             i++)
        {
            Vector3 normal =
                Vector3.TransformNormal(
                    source.Normals[i],
                    normalTransform);

            normals[i] =
                NormalizeOrFallback(
                    normal,
                    Vector3.UnitY);
        }

        Vector4[] tangents =
            new Vector4[source.Tangents.Length];

        for (int i = 0;
             i < tangents.Length;
             i++)
        {
            Vector4 sourceTangent =
                source.Tangents[i];

            Vector3 tangent =
                Vector3.TransformNormal(
                    new Vector3(
                        sourceTangent.X,
                        sourceTangent.Y,
                        sourceTangent.Z),
                    transform);

            if (i < normals.Length)
            {
                tangent -=
                    normals[i] *
                    Vector3.Dot(
                        normals[i],
                        tangent);
            }

            tangent =
                NormalizeOrFallback(
                    tangent,
                    Vector3.UnitX);

            float handedness =
                sourceTangent.W < 0.0f
                    ? -1.0f
                    : 1.0f;

            if (mirrored)
            {
                handedness =
                    -handedness;
            }

            tangents[i] =
                new Vector4(
                    tangent,
                    handedness);
        }

        return new MeshData
        {
            Name =
                $"{model.Name}/{source.Name}",

            MaterialIndex =
                source.MaterialIndex +
                materialOffset,

            Positions =
                positions,

            Normals =
                normals,

            Tangents =
                tangents,

            TexCoords0 =
                (Vector2[])source.TexCoords0.Clone(),

            TexCoords1 =
                (Vector2[])source.TexCoords1.Clone(),

            Indices =
                (int[])source.Indices.Clone(),

            ResolvedAlphaMode =
                source.ResolvedAlphaMode
        };
    }

    private static Vector3 NormalizeOrFallback(
        Vector3 value,
        Vector3 fallback)
    {
        return value.LengthSquared() > 0.00000001f
            ? Vector3.Normalize(value)
            : fallback;
    }
}

/// <summary>
/// One independently transformable model inside a <see cref="SceneDocument"/>.
/// </summary>
public sealed class SceneModel
{
    /// <summary>Creates a scene-model entry around one loaded glTF scene.</summary>
    public SceneModel(
        string sourcePath,
        SceneData sourceScene)
    {
        Id =
            Guid.NewGuid();

        SourcePath =
            Path.GetFullPath(
                sourcePath);

        Name =
            Path.GetFileNameWithoutExtension(
                sourcePath);

        SourceScene =
            sourceScene;
    }

    /// <summary>Gets the stable model identifier used by later selection tools.</summary>
    public Guid Id { get; }

    /// <summary>Gets the original GLB or glTF path.</summary>
    public string SourcePath { get; }

    /// <summary>Gets or sets the user-visible model name.</summary>
    public string Name { get; set; }

    /// <summary>Gets the immutable scene loaded from the source file.</summary>
    public SceneData SourceScene { get; }

    /// <summary>Gets or sets the model translation in scene units.</summary>
    public Vector3 Position { get; set; }

    /// <summary>Gets or sets XYZ Euler rotation in degrees.</summary>
    public Vector3 RotationDegrees { get; set; }

    /// <summary>Gets or sets independent XYZ scale.</summary>
    public Vector3 Scale { get; set; } =
        Vector3.One;

    /// <summary>Builds the model-to-scene transform using scale, XYZ rotation, and translation.</summary>
    public Matrix4x4 CreateTransform()
    {
        const float degreesToRadians =
            MathF.PI / 180.0f;

        Matrix4x4 rotation =
            Matrix4x4.CreateRotationX(
                RotationDegrees.X *
                degreesToRadians) *
            Matrix4x4.CreateRotationY(
                RotationDegrees.Y *
                degreesToRadians) *
            Matrix4x4.CreateRotationZ(
                RotationDegrees.Z *
                degreesToRadians);

        return
            Matrix4x4.CreateScale(
                Scale) *
            rotation *
            Matrix4x4.CreateTranslation(
                Position);
    }
}
