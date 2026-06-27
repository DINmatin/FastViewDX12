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

    /// <summary>Gets the model selected by the scene panel, if any.</summary>
    public SceneModel? SelectedModel { get; private set; }

    /// <summary>Removes all existing models and inserts one new root model.</summary>
    public SceneModel ReplaceWith(
        string sourcePath,
        SceneData scene)
    {
        _models.Clear();
        SelectedModel =
            null;

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
        SelectedModel =
            model;

        return model;
    }

    /// <summary>Selects a model already contained in the document.</summary>
    public void Select(
        SceneModel? model)
    {
        SelectedModel =
            model != null &&
            _models.Contains(model)
                ? model
                : null;
    }

    /// <summary>Removes one model and selects the nearest remaining entry.</summary>
    public bool Remove(
        Guid modelId)
    {
        int index =
            _models.FindIndex(
                model =>
                    model.Id == modelId);

        if (index < 0)
        {
            return false;
        }

        _models.RemoveAt(index);

        if (_models.Count == 0)
        {
            SelectedModel =
                null;
        }
        else
        {
            int nextIndex =
                Math.Min(
                    index,
                    _models.Count - 1);

            SelectedModel =
                _models[nextIndex];
        }

        return true;
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
    private Vector3 _rotationDegrees;

    private Vector3 _scale =
        Vector3.One;

    // Store the exact affine linear transform separately from translation.
    // Pure TRS edits rebuild this matrix, while global non-uniform scale can
    // keep the shear component that a Vector3 scale alone cannot represent.
    private Matrix4x4 _linearTransform =
        Matrix4x4.Identity;

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

    /// <summary>
    /// Gets or sets the inspector's XYZ Euler rotation in degrees. Editing this
    /// value intentionally returns the model to a pure scale/rotation matrix.
    /// </summary>
    public Vector3 RotationDegrees
    {
        get =>
            _rotationDegrees;

        set
        {
            _rotationDegrees =
                value;

            RebuildLinearTransformFromInspector();
        }
    }

    /// <summary>
    /// Gets or sets the inspector's independent XYZ scale. Editing this value
    /// intentionally returns the model to a pure scale/rotation matrix.
    /// </summary>
    public Vector3 Scale
    {
        get =>
            _scale;

        set
        {
            _scale =
                value;

            RebuildLinearTransformFromInspector();
        }
    }

    /// <summary>
    /// Gets the exact model-space-to-world linear transform without translation.
    /// It may contain shear after a global non-uniform scale operation.
    /// </summary>
    public Matrix4x4 LinearTransform =>
        _linearTransform;

    public override string ToString()
    {
        return Name;
    }

    /// <summary>
    /// Replaces the exact linear transform used by the renderer. The inspector
    /// rotation and scale are updated to the nearest readable TRS values without
    /// discarding the exact matrix.
    /// </summary>
    public void SetLinearTransform(
        Matrix4x4 linearTransform)
    {
        linearTransform.M14 =
            0.0f;

        linearTransform.M24 =
            0.0f;

        linearTransform.M34 =
            0.0f;

        linearTransform.M41 =
            0.0f;

        linearTransform.M42 =
            0.0f;

        linearTransform.M43 =
            0.0f;

        linearTransform.M44 =
            1.0f;

        _linearTransform =
            linearTransform;

        UpdateInspectorValuesFromLinearTransform();
    }

    /// <summary>Builds the exact model-to-scene transform.</summary>
    public Matrix4x4 CreateTransform()
    {
        return
            _linearTransform *
            Matrix4x4.CreateTranslation(
                Position);
    }

    private void RebuildLinearTransformFromInspector()
    {
        const float degreesToRadians =
            MathF.PI / 180.0f;

        Matrix4x4 rotation =
            Matrix4x4.CreateRotationX(
                _rotationDegrees.X *
                degreesToRadians) *
            Matrix4x4.CreateRotationY(
                _rotationDegrees.Y *
                degreesToRadians) *
            Matrix4x4.CreateRotationZ(
                _rotationDegrees.Z *
                degreesToRadians);

        _linearTransform =
            Matrix4x4.CreateScale(
                _scale) *
            rotation;
    }

    private void UpdateInspectorValuesFromLinearTransform()
    {
        Vector3 rowX =
            new(
                _linearTransform.M11,
                _linearTransform.M12,
                _linearTransform.M13);

        Vector3 rowY =
            new(
                _linearTransform.M21,
                _linearTransform.M22,
                _linearTransform.M23);

        Vector3 rowZ =
            new(
                _linearTransform.M31,
                _linearTransform.M32,
                _linearTransform.M33);

        float scaleX =
            MathF.Max(
                rowX.Length(),
                0.000001f);

        float scaleY =
            MathF.Max(
                rowY.Length(),
                0.000001f);

        float scaleZ =
            MathF.Max(
                rowZ.Length(),
                0.000001f);

        float signX =
            SignOrPositive(
                _scale.X);

        float signY =
            SignOrPositive(
                _scale.Y);

        float signZ =
            SignOrPositive(
                _scale.Z);

        float determinant =
            Determinant3x3(
                _linearTransform);

        float requestedSignProduct =
            determinant < 0.0f
                ? -1.0f
                : 1.0f;

        float currentSignProduct =
            signX *
            signY *
            signZ;

        if (currentSignProduct !=
            requestedSignProduct)
        {
            if (scaleX >= scaleY &&
                scaleX >= scaleZ)
            {
                signX =
                    -signX;
            }
            else if (scaleY >= scaleZ)
            {
                signY =
                    -signY;
            }
            else
            {
                signZ =
                    -signZ;
            }
        }

        Vector3 approximateScale =
            new(
                scaleX * signX,
                scaleY * signY,
                scaleZ * signZ);

        Vector3 axisX =
            NormalizeOrFallback(
                rowX /
                approximateScale.X,
                Vector3.UnitX);

        Vector3 axisYSource =
            rowY /
            approximateScale.Y;

        Vector3 axisY =
            axisYSource -
            axisX *
            Vector3.Dot(
                axisYSource,
                axisX);

        if (axisY.LengthSquared() <
            0.00000001f)
        {
            Vector3 axisZSource =
                rowZ /
                approximateScale.Z;

            axisY =
                Vector3.Cross(
                    axisZSource,
                    axisX);
        }

        axisY =
            NormalizeOrFallback(
                axisY,
                Vector3.UnitY);

        Vector3 axisZ =
            NormalizeOrFallback(
                Vector3.Cross(
                    axisX,
                    axisY),
                Vector3.UnitZ);

        Vector3 expectedAxisZ =
            NormalizeOrFallback(
                rowZ /
                approximateScale.Z,
                Vector3.UnitZ);

        if (Vector3.Dot(
                axisZ,
                expectedAxisZ) <
            0.0f)
        {
            axisY =
                -axisY;

            axisZ =
                -axisZ;
        }

        Matrix4x4 approximateRotation =
            new(
                axisX.X,
                axisX.Y,
                axisX.Z,
                0.0f,
                axisY.X,
                axisY.Y,
                axisY.Z,
                0.0f,
                axisZ.X,
                axisZ.Y,
                axisZ.Z,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                1.0f);

        _rotationDegrees =
            ExtractEulerRotationDegrees(
                approximateRotation,
                _rotationDegrees);

        _scale =
            approximateScale;
    }

    private static float SignOrPositive(
        float value)
    {
        return value < 0.0f
            ? -1.0f
            : 1.0f;
    }

    private static float Determinant3x3(
        Matrix4x4 matrix)
    {
        return
            matrix.M11 *
                (matrix.M22 * matrix.M33 -
                 matrix.M23 * matrix.M32) -
            matrix.M12 *
                (matrix.M21 * matrix.M33 -
                 matrix.M23 * matrix.M31) +
            matrix.M13 *
                (matrix.M21 * matrix.M32 -
                 matrix.M22 * matrix.M31);
    }

    private static Vector3 NormalizeOrFallback(
        Vector3 value,
        Vector3 fallback)
    {
        return value.LengthSquared() >
            0.00000001f
            ? Vector3.Normalize(
                value)
            : fallback;
    }

    private static Vector3 ExtractEulerRotationDegrees(
        Matrix4x4 rotation,
        Vector3 referenceDegrees)
    {
        const float radiansToDegrees =
            180.0f / MathF.PI;

        float sinY =
            Math.Clamp(
                -rotation.M13,
                -1.0f,
                1.0f);

        float y =
            MathF.Asin(
                sinY);

        float cosY =
            MathF.Cos(
                y);

        float x;
        float z;

        if (MathF.Abs(cosY) >
            0.00001f)
        {
            x =
                MathF.Atan2(
                    rotation.M23,
                    rotation.M33);

            z =
                MathF.Atan2(
                    rotation.M12,
                    rotation.M11);
        }
        else if (sinY >=
            0.0f)
        {
            z =
                referenceDegrees.Z /
                radiansToDegrees;

            float xMinusZ =
                MathF.Atan2(
                    rotation.M21,
                    rotation.M22);

            x =
                xMinusZ +
                z;
        }
        else
        {
            z =
                referenceDegrees.Z /
                radiansToDegrees;

            float xPlusZ =
                MathF.Atan2(
                    -rotation.M21,
                    rotation.M22);

            x =
                xPlusZ -
                z;
        }

        Vector3 primary =
            new(
                x * radiansToDegrees,
                y * radiansToDegrees,
                z * radiansToDegrees);

        primary =
            UnwrapEulerNear(
                primary,
                referenceDegrees);

        if (MathF.Abs(cosY) <=
            0.00001f)
        {
            return primary;
        }

        Vector3 alternate =
            new(
                primary.X + 180.0f,
                180.0f - primary.Y,
                primary.Z + 180.0f);

        alternate =
            UnwrapEulerNear(
                alternate,
                referenceDegrees);

        return Vector3.DistanceSquared(
                alternate,
                referenceDegrees) <
            Vector3.DistanceSquared(
                primary,
                referenceDegrees)
                ? alternate
                : primary;
    }

    private static Vector3 UnwrapEulerNear(
        Vector3 value,
        Vector3 reference)
    {
        value.X =
            UnwrapDegreesNear(
                value.X,
                reference.X);

        value.Y =
            UnwrapDegreesNear(
                value.Y,
                reference.Y);

        value.Z =
            UnwrapDegreesNear(
                value.Z,
                reference.Z);

        return value;
    }

    private static float UnwrapDegreesNear(
        float degrees,
        float referenceDegrees)
    {
        while (degrees -
               referenceDegrees >
               180.0f)
        {
            degrees -=
                360.0f;
        }

        while (degrees -
               referenceDegrees <
               -180.0f)
        {
            degrees +=
                360.0f;
        }

        return degrees;
    }
}
